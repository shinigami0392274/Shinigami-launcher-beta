using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace FlagDeck;

public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    const int WM_HOTKEY = 0x0312;
    IntPtr _hwnd; HwndSource? _src;
    readonly Dictionary<int, HotkeyBinding> _map = new();
    public event Action<HotkeyBinding>? Fired;

    public void Attach(Window w)
    {
        _hwnd = new WindowInteropHelper(w).EnsureHandle();
        _src = HwndSource.FromHwnd(_hwnd);
        _src.AddHook(Hook);
    }

    IntPtr Hook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == WM_HOTKEY && _map.TryGetValue(wp.ToInt32(), out var b)) { handled = true; Fired?.Invoke(b); }
        return IntPtr.Zero;
    }

    public List<string> Register(IEnumerable<HotkeyBinding> binds)
    {
        Clear();
        var failed = new List<string>(); int id = 1;
        foreach (var b in binds)
        {
            if (!TryParse(b.Gesture, out var mod, out var vk)) { failed.Add(b.Gesture); continue; }
            if (RegisterHotKey(_hwnd, id, mod | 0x4000, vk)) _map[id++] = b; else failed.Add(b.Gesture);
        }
        return failed;
    }

    public void Clear() { foreach (var id in _map.Keys) UnregisterHotKey(_hwnd, id); _map.Clear(); }

    static bool TryParse(string g, out uint mod, out uint vk)
    {
        mod = 0; vk = 0;
        var parts = g.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var p in parts[..^1])
            switch (p) { case "Alt": mod |= 1; break; case "Ctrl": mod |= 2; break; case "Shift": mod |= 4; break; case "Win": mod |= 8; break; default: return false; }
        if (!Enum.TryParse<Key>(parts[^1], out var key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public void Dispose() { Clear(); _src?.RemoveHook(Hook); }
}

public static class Actions
{
    static Preset? Find(string id) => Store.Data.Presets.FirstOrDefault(p => p.Id == id);

    public static string Run(HotkeyBinding b)
    {
        switch (b.Action)
        {
            case "TogglePreset":
            {
                var p = Find(b.Target.Split(',')[0]); if (p == null) return "Preset not found";
                p.Enabled = !p.Enabled; return $"{p.Name}: {(p.Enabled ? "ON" : "OFF")}";
            }
            case "CyclePresets":
            {
                var list = b.Target.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Find).Where(p => p != null).Cast<Preset>().ToList();
                if (list.Count == 0) return "No presets to cycle";
                int cur = list.FindIndex(p => p.Enabled);
                var next = list[(cur + 1) % list.Count];
                foreach (var p in list) p.Enabled = p == next;
                return $"Preset: {next.Name}";
            }
            case "ToggleFlag":
            {
                // "FlagName=value" toggles on/off.  "FlagName=1|2|3" cycles values, then off.
                var i = b.Target.IndexOf('='); if (i < 1) return "Bad flag binding";
                var name = b.Target[..i].Trim(); var vals = b.Target[(i + 1)..].Split('|', StringSplitOptions.TrimEntries);
                var hp = Store.HotkeyPreset();
                int idx = hp.Flags.TryGetValue(name, out var cur) ? Array.IndexOf(vals, cur) : -1;
                int nxt = idx + 1;
                if (nxt >= vals.Length) { hp.Flags.Remove(name); hp.Enabled = true; return $"{name}: OFF"; }
                hp.Flags[name] = vals[nxt]; hp.Enabled = true; return $"{name} = {vals[nxt]}";
            }
        }
        return "";
    }
}

public static class Toast
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    public static void Show(string text)
    {
        var app = Application.Current;
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false,
            ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight
        };
        w.Content = new Border
        {
            Background = (Brush)app.FindResource("Surface"), BorderBrush = (Brush)app.FindResource("Accent"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 10, 16, 10),
            Child = new TextBlock { Text = text, FontSize = 14, Foreground = (Brush)app.FindResource("Text"), FontFamily = new FontFamily("Segoe UI") }
        };
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80); // NOACTIVATE | TOOLWINDOW
        };
        w.Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            w.Left = wa.Right - w.ActualWidth - 20; w.Top = wa.Bottom - w.ActualHeight - 20;
        };
        w.Show();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        t.Tick += (_, _) => { t.Stop(); w.Close(); };
        t.Start();
    }
}
