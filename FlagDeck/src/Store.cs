using System.IO.Compression;
using System.Text;
using System.Text.Json;
namespace FlagDeck;

public static class Store
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlagDeck");
    static string DataPath => Path.Combine(Dir, "data.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public static AppData Data = new();
    public static Preset? Current;
    public static bool Suppress;
    public static event Action? Changed;

    public static void Load()
    {
        Suppress = true;
        try { if (File.Exists(DataPath)) Data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(DataPath), Opt) ?? new(); }
        catch { try { File.Copy(DataPath, DataPath + ".bak", true); } catch { } Data = new(); }
        if (Data.Presets.Count == 0) Data.Presets.Add(new Preset { Name = "My flags" });
        Suppress = false;
    }
    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = DataPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Opt));
        File.Move(tmp, DataPath, true);
    }
    public static void Touch()
    {
        if (Suppress) return;
        foreach (var p in Data.Presets) p.RefreshCount();
        Changed?.Invoke();
    }

    /// Enabled presets merged top to bottom; later presets win.
    public static Dictionary<string, string> Compose()
    {
        var r = new Dictionary<string, string>();
        foreach (var p in Data.Presets.Where(p => p.Enabled))
            foreach (var kv in p.Flags) r[kv.Key] = kv.Value;
        return r;
    }

    public static Preset HotkeyPreset()
    {
        var p = Data.Presets.FirstOrDefault(x => x.Id == "hotkey-flags");
        if (p == null) { p = new Preset { Id = "hotkey-flags", Name = "Hotkey flags" }; Data.Presets.Add(p); }
        return p;
    }

    // Sharing: "FD1:<base64(gzip(json))>" or a plain Bloxstrap-style flag JSON.
    public static string Export(Preset p)
    {
        var json = JsonSerializer.Serialize(new { v = 1, name = p.Name, flags = p.Flags });
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true)) gz.Write(Encoding.UTF8.GetBytes(json));
        return "FD1:" + Convert.ToBase64String(ms.ToArray());
    }

    public static Preset? Import(string text, string fallbackName = "Imported")
    {
        text = text.Trim();
        if (text.StartsWith("FD1:"))
        {
            using var inp = new MemoryStream(Convert.FromBase64String(text[4..]));
            using var gz = new GZipStream(inp, CompressionMode.Decompress);
            using var rd = new StreamReader(gz, Encoding.UTF8);
            text = rd.ReadToEnd();
        }
        if (!text.StartsWith("{")) text = "{" + text.TrimEnd(',') + "}";
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;
        var p = new Preset { Name = fallbackName };
        JsonElement flags = root;
        if (root.TryGetProperty("flags", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            flags = f;
            if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) p.Name = n.GetString()!;
        }
        foreach (var kv in flags.EnumerateObject())
            p.Flags[kv.Name] = kv.Value.ValueKind switch
            {
                JsonValueKind.String => kv.Value.GetString() ?? "",
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => kv.Value.GetRawText()
            };
        return p.Flags.Count == 0 ? null : p;
    }
}
