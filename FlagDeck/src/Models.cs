using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
namespace FlagDeck;

public class Preset : INotifyPropertyChanged
{
    string _name = "New preset"; bool _enabled = true;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set { _name = value; N(nameof(Name)); Store.Touch(); } }
    public bool Enabled { get => _enabled; set { _enabled = value; N(nameof(Enabled)); Store.Touch(); } }
    public Dictionary<string, string> Flags { get; set; } = new();
    [JsonIgnore] public int Count => Flags.Count;
    public event PropertyChangedEventHandler? PropertyChanged;
    void N(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public void RefreshCount() => N(nameof(Count));
}

public class HotkeyBinding
{
    public string Gesture { get; set; } = "";
    public string Action { get; set; } = "TogglePreset"; // TogglePreset | CyclePresets | ToggleFlag
    public string Target { get; set; } = "";
    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (Action == "ToggleFlag") return "Flag: " + Target;
            var names = Target.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => Store.Data.Presets.FirstOrDefault(p => p.Id == id)?.Name ?? "(deleted)");
            return (Action == "CyclePresets" ? "Cycle: " : "Toggle: ") + string.Join(" → ", names);
        }
    }
}

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public bool AutoUpdateFlags { get; set; } = true;
    public bool AutoUpdateApp { get; set; } = false;
    public bool AutoApply { get; set; } = true;
    public bool KeepApplied { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool TargetsInit { get; set; } = false;
    public List<string> TargetIds { get; set; } = new();
    public DateTime LastCatalogCheck { get; set; } = DateTime.MinValue;
}

public class AppData
{
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<Preset> Presets { get; set; } = new();
    public ObservableCollection<HotkeyBinding> Hotkeys { get; set; } = new();
}

public class FlagRow : INotifyPropertyChanged
{
    public string Name { get; }
    public string Default { get; }
    public string Type { get; }
    public FlagRow(string name, string def) { Name = name; Default = def; Type = Catalog.TypeOf(name); }

    public bool Enabled
    {
        get => Store.Current?.Flags.ContainsKey(Name) == true;
        set
        {
            var p = Store.Current; if (p == null) return;
            if (value) { if (!p.Flags.ContainsKey(Name)) p.Flags[Name] = Suggest(); } else p.Flags.Remove(Name);
            Raise(); Store.Touch();
        }
    }
    public string Value
    {
        get => Store.Current != null && Store.Current.Flags.TryGetValue(Name, out var v) ? v : "";
        set
        {
            var p = Store.Current; if (p == null) return;
            value = (value ?? "").Trim();
            if (value.Length == 0) p.Flags.Remove(Name); else p.Flags[Name] = value;
            Raise(); Store.Touch();
        }
    }
    string Suggest() => Type == "Flag" ? (Default.Equals("true", StringComparison.OrdinalIgnoreCase) ? "False" : "True") : Default;
    void Raise() { PropertyChanged?.Invoke(this, new(nameof(Enabled))); PropertyChanged?.Invoke(this, new(nameof(Value))); }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class Target
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Dir { get; set; } = "";
    public bool Vanilla { get; set; }
    public bool Selected
    {
        get => Store.Data.Settings.TargetIds.Contains(Id);
        set
        {
            var l = Store.Data.Settings.TargetIds;
            if (value && !l.Contains(Id)) l.Add(Id); else if (!value) l.Remove(Id);
            Store.Touch();
        }
    }
}
