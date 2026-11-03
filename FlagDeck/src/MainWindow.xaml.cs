using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
namespace FlagDeck;

public partial class MainWindow : Window
{
    readonly List<FlagRow> all = new();
    readonly Dictionary<string, FlagRow> byName = new();
    string typeFilter = "All";
    bool ready, exiting;
    readonly DispatcherTimer searchT = new() { Interval = TimeSpan.FromMilliseconds(110) };
    readonly DispatcherTimer saveT = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer keepT = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly HotkeyService hk = new();
    System.Windows.Forms.NotifyIcon? tray;
    AppUpdater.Release? pending;
    static AppSettings S => Store.Data.Settings;
    Preset? Cur => PresetList.SelectedItem as Preset;

    public MainWindow()
    {
        InitializeComponent();
        PresetList.ItemsSource = Store.Data.Presets;
        HkPresets.ItemsSource = Store.Data.Presets;
        HkList.ItemsSource = Store.Data.Hotkeys;
        PresetList.SelectedIndex = 0;
        Store.Current = Cur;
        VersionText.Text = $"FlagDeck v{AppUpdater.Current.ToString(3)}";
        LoadSettingsUi();
        RebuildRows();

        searchT.Tick += (_, _) => { searchT.Stop(); RefreshFilter(); };
        saveT.Tick += (_, _) => { saveT.Stop(); Store.Save(); if (S.AutoApply) ApplyNow(false); };
        keepT.Tick += (_, _) => { if (S.KeepApplied) ApplyNow(true); };
        keepT.Start();
        Store.Changed += () => { saveT.Stop(); saveT.Start(); };

        hk.Attach(this);
        hk.Fired += OnHotkey;
        RegisterHotkeys();
        SetupTray();

        ready = true;
        RefreshFilter();
        Dispatcher.BeginInvoke(new Action(async () => await StartupTasks()), DispatcherPriority.Background);
    }

    // ---------- startup / updates ----------
    async Task StartupTasks()
    {
        ApplyNow(false);
        if (S.AutoUpdateFlags && (all.Count == 0 || DateTime.UtcNow - S.LastCatalogCheck > TimeSpan.FromHours(12)))
        {
            Status("Downloading the latest flag list…");
            var r = await Catalog.UpdateAsync();
            Status(r.Message);
            if (r.Changed) { RebuildRows(); RefreshFilter(); }
        }
        if (S.AutoUpdateApp && !AppUpdater.Repo.StartsWith("YOUR-USER"))
        {
            try
            {
                pending = await AppUpdater.CheckAsync();
                if (pending != null && await AppUpdater.InstallAsync(pending)) ExitApp();
            }
            catch (Exception ex) { Status("App update failed: " + ex.Message); }
        }
    }

    async void UpdateFlags_Click(object s, RoutedEventArgs e)
    {
        Status("Downloading the latest flag list…");
        var r = await Catalog.UpdateAsync(); Status(r.Message);
        if (r.Changed) { RebuildRows(); RefreshFilter(); }
    }

    async void CheckApp_Click(object s, RoutedEventArgs e)
    {
        try
        {
            pending = await AppUpdater.CheckAsync();
            InstallBtn.Visibility = pending == null ? Visibility.Collapsed : Visibility.Visible;
            Status(pending == null ? "FlagDeck is up to date." : $"Version {pending.Version} is available.");
        }
        catch (Exception ex) { Status(ex.Message); }
    }

    async void InstallUpdate_Click(object s, RoutedEventArgs e)
    {
        if (pending == null) return;
        try { Status("Downloading update…"); if (await AppUpdater.InstallAsync(pending)) ExitApp(); }
        catch (Exception ex) { Status(ex.Message); }
    }

    // ---------- rows / search ----------
    void RebuildRows()
    {
        all.Clear(); byName.Clear();
        foreach (var (n, d) in Catalog.Load()) { var r = new FlagRow(n, d); all.Add(r); byName.TryAdd(n, r); }
    }

    static bool Match(string name, string[] terms)
    {
        foreach (var t in terms) if (name.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    void RefreshFilter()
    {
        if (!ready) return;
        var cur = Store.Current;
        var q = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var edited = EditedOnly.IsChecked == true;
        var res = new List<FlagRow>(512);
        if (cur != null)
            foreach (var k in cur.Flags.Keys)
            {
                var row = byName.TryGetValue(k, out var r) ? r : new FlagRow(k, "");
                if ((typeFilter == "All" || row.Type == typeFilter) && Match(row.Name, q)) res.Add(row);
            }
        if (!edited)
            foreach (var r in all)
            {
                if (typeFilter != "All" && r.Type != typeFilter) continue;
                if (cur != null && cur.Flags.ContainsKey(r.Name)) continue;
                if (Match(r.Name, q)) res.Add(r);
            }
        FlagGrid.ItemsSource = res;
        CountText.Text = all.Count == 0
            ? "No flag list yet. Open Settings → Update flag list (or add flags manually)."
            : $"{res.Count:N0} shown of {all.Count:N0}";
    }

    void Search_Changed(object s, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchT.Stop(); searchT.Start();
    }
    void Filter_Changed(object s, RoutedEventArgs e) => RefreshFilter();
    void Type_Checked(object s, RoutedEventArgs e) { typeFilter = (string)((RadioButton)s).Tag; RefreshFilter(); }
    void Cell_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) ((TextBox)s).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); }

    void AddCustom_Click(object s, RoutedEventArgs e)
    {
        var cur = Cur; var t = CustomBox.Text.Trim().Trim(',');
        if (cur == null || t.Length == 0) return;
        t = t.Replace("\"", "");
        int i = t.IndexOfAny(new[] { '=', ':' });
        var name = (i < 0 ? t : t[..i]).Trim(); var val = i < 0 ? "True" : t[(i + 1)..].Trim();
        if (name.Length == 0) return;
        cur.Flags[name] = val; CustomBox.Clear();
        Store.Touch(); SearchBox.Text = name; RefreshFilter();
    }
    void CustomBox_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) AddCustom_Click(s, e); }

    // ---------- presets ----------
    void PresetList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        Store.Current = Cur; RefreshFilter();
    }

    void NewPreset_Click(object s, RoutedEventArgs e) { var p = new Preset { Name = "New preset" }; Store.Data.Presets.Add(p); PresetList.SelectedItem = p; Store.Touch(); }

    void DupPreset_Click(object s, RoutedEventArgs e)
    {
        if (Cur is not { } c) return;
        var p = new Preset { Name = c.Name + " copy", Flags = new Dictionary<string, string>(c.Flags), Enabled = false };
        Store.Data.Presets.Add(p); PresetList.SelectedItem = p; Store.Touch();
    }

    void Move(int d)
    {
        if (Cur is not { } c) return;
        var l = Store.Data.Presets; int i = l.IndexOf(c), j = i + d;
        if (j < 0 || j >= l.Count) return;
        l.Move(i, j); PresetList.SelectedItem = c; Store.Touch();
    }
    void MoveUp_Click(object s, RoutedEventArgs e) => Move(-1);
    void MoveDown_Click(object s, RoutedEventArgs e) => Move(1);

    void Merge_Click(object s, RoutedEventArgs e)
    {
        var sel = PresetList.SelectedItems.Cast<Preset>().OrderBy(p => Store.Data.Presets.IndexOf(p)).ToList();
        if (sel.Count < 2) { Status("Ctrl+click two or more presets to merge them."); return; }
        var m = new Preset { Name = "Merged: " + string.Join(" + ", sel.Select(p => p.Name)) };
        foreach (var p in sel) foreach (var kv in p.Flags) m.Flags[kv.Key] = kv.Value;
        Store.Data.Presets.Add(m); PresetList.SelectedItem = m; Store.Touch();
        Status($"Merged {sel.Count} presets ({m.Flags.Count} flags). Later presets won conflicts.");
    }

    void DeletePreset_Click(object s, RoutedEventArgs e)
    {
        var sel = PresetList.SelectedItems.Cast<Preset>().ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show(this, $"Delete {sel.Count} preset(s)?", "FlagDeck", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var p in sel) Store.Data.Presets.Remove(p);
        if (Store.Data.Presets.Count == 0) Store.Data.Presets.Add(new Preset { Name = "My flags" });
        PresetList.SelectedIndex = 0; Store.Current = Cur; RefreshFilter(); Store.Touch();
    }

    void Share_Click(object s, RoutedEventArgs e)
    {
        if (Cur is not { } c) return;
        Clipboard.SetText(Store.Export(c));
        Status($"Share code for “{c.Name}” copied. Paste it anywhere; others use Import.");
    }

    void Import_Click(object s, RoutedEventArgs e)
    {
        Preset? p = null;
        try { if (Clipboard.ContainsText()) p = Store.Import(Clipboard.GetText()); } catch { }
        if (p == null)
        {
            var dlg = new OpenFileDialog { Filter = "FastFlag JSON|*.json;*.txt|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            try { p = Store.Import(File.ReadAllText(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName)); }
            catch (Exception ex) { Status("Import failed: " + ex.Message); return; }
        }
        if (p == null) { Status("Nothing to import."); return; }
        p.Enabled = false;
        Store.Data.Presets.Add(p); PresetList.SelectedItem = p; Store.Touch();
        Status($"Imported “{p.Name}” with {p.Flags.Count} flags (left off; tick it to enable).");
    }

    // ---------- applying ----------
    void ApplyNow_Click(object s, RoutedEventArgs e) { Store.Save(); ApplyNow(false); }

    void ApplyNow(bool quiet)
    {
        try
        {
            var r = Targets.Apply(quiet);
            if (!quiet || r.Written > 0) Status($"Applied {r.Flags} flags → {r.Names} · {DateTime.Now:HH:mm:ss}  (restart Roblox to load them)");
        }
        catch (Exception ex) { Status(ex.Message); }
        UpdateTargetsText();
    }

    void Status(string t) => StatusText.Text = t;

    void UpdateTargetsText()
    {
        var sel = Targets.Discover().Where(t => t.Selected).Select(t => t.Name).ToList();
        TargetsText.Text = sel.Count == 0 ? "No Roblox install found" : "Writing to:\n" + string.Join(", ", sel);
    }

    // ---------- hotkeys ----------
    void RegisterHotkeys()
    {
        if (!S.HotkeysEnabled) { hk.Clear(); return; }
        var bad = hk.Register(Store.Data.Hotkeys);
        if (bad.Count > 0) Status("Couldn't register: " + string.Join(", ", bad) + " (already used by another app?)");
    }

    void OnHotkey(HotkeyBinding b)
    {
        Dispatcher.Invoke(() =>
        {
            var msg = Actions.Run(b);
            Store.Touch(); Store.Save();
            ApplyNow(false);
            RefreshFilter();
            Toast.Show(msg);
        });
    }

    void HkGesture_PreviewKeyDown(object s, KeyEventArgs e)
    {
        e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None) return;
        if (k is Key.Escape or Key.Back) { HkGesture.Text = ""; return; }
        var parts = new List<string>(); var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(k.ToString());
        HkGesture.Text = string.Join("+", parts);
    }

    void AddHotkey_Click(object s, RoutedEventArgs e)
    {
        if (HkGesture.Text.Length == 0) { Status("Click the shortcut box and press a key combo first."); return; }
        var sel = HkPresets.SelectedItems.Cast<Preset>().OrderBy(p => Store.Data.Presets.IndexOf(p)).ToList();
        var b = new HotkeyBinding { Gesture = HkGesture.Text };
        if (HkFlag.IsChecked == true)
        {
            if (!HkFlagText.Text.Contains('=')) { Status("Use the form FlagName=Value or FlagName=1|2|3"); return; }
            b.Action = "ToggleFlag"; b.Target = HkFlagText.Text.Trim();
        }
        else if (HkCycle.IsChecked == true)
        {
            if (sel.Count < 2) { Status("Select two or more presets to cycle."); return; }
            b.Action = "CyclePresets"; b.Target = string.Join(",", sel.Select(p => p.Id));
        }
        else
        {
            if (sel.Count < 1) { Status("Select a preset to toggle."); return; }
            b.Action = "TogglePreset"; b.Target = sel[0].Id;
        }
        if (Store.Data.Hotkeys.Any(h => h.Gesture == b.Gesture)) { Status("That shortcut is already used."); return; }
        Store.Data.Hotkeys.Add(b); HkGesture.Clear(); Store.Touch(); RegisterHotkeys();
    }

    void RemoveHotkey_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is HotkeyBinding b) { Store.Data.Hotkeys.Remove(b); Store.Touch(); RegisterHotkeys(); }
    }

    // ---------- settings ----------
    void LoadSettingsUi()
    {
        OptAutoApply.IsChecked = S.AutoApply; OptKeep.IsChecked = S.KeepApplied; OptHotkeys.IsChecked = S.HotkeysEnabled;
        OptTray.IsChecked = S.CloseToTray; OptAutoFlags.IsChecked = S.AutoUpdateFlags; OptAutoApp.IsChecked = S.AutoUpdateApp;
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        OptStartup.IsChecked = k?.GetValue("FlagDeck") != null;
        TargetList.ItemsSource = Targets.Discover();
        UpdateTargetsText();
    }

    void Rescan_Click(object s, RoutedEventArgs e) { TargetList.ItemsSource = Targets.Discover(); UpdateTargetsText(); }

    void Opt_Click(object s, RoutedEventArgs e)
    {
        var cb = (CheckBox)s; var v = cb.IsChecked == true;
        switch ((string)cb.Tag)
        {
            case "AutoApply": S.AutoApply = v; break;
            case "KeepApplied": S.KeepApplied = v; break;
            case "HotkeysEnabled": S.HotkeysEnabled = v; RegisterHotkeys(); break;
            case "CloseToTray": S.CloseToTray = v; break;
            case "AutoUpdateFlags": S.AutoUpdateFlags = v; break;
            case "AutoUpdateApp": S.AutoUpdateApp = v; break;
            case "StartWithWindows":
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (v) k?.SetValue("FlagDeck", $"\"{Environment.ProcessPath}\" --minimized"); else k?.DeleteValue("FlagDeck", false);
                }
                break;
        }
        Store.Touch();
    }

    void Theme_Click(object s, RoutedEventArgs e)
    {
        S.Theme = (string)((Button)s).Tag; App.ApplyTheme(S.Theme); Store.Touch();
    }

    void Nav_Checked(object s, RoutedEventArgs e)
    {
        if (ViewFlags == null || ViewHotkeys == null || ViewSettings == null) return;
        var tag = (string)((RadioButton)s).Tag;
        ViewFlags.Visibility = tag == "Flags" ? Visibility.Visible : Visibility.Collapsed;
        ViewHotkeys.Visibility = tag == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        ViewSettings.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- tray / lifecycle ----------
    void SetupTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open FlagDeck", null, (_, _) => ShowMain());
        menu.Items.Add("Apply now", null, (_, _) => Dispatcher.Invoke(() => ApplyNow(false)));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "FlagDeck", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowMain();
    }

    void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }

    void ExitApp()
    {
        exiting = true;
        try { Store.Save(); } catch { }
        hk.Dispose(); tray?.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!exiting && S.CloseToTray) { e.Cancel = true; Hide(); return; }
        if (!exiting) ExitApp();
        base.OnClosing(e);
    }
}
