using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FlagDeck;

public static class Net
{
    public static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FlagDeck/1.0");
        return c;
    }
}

public static class Catalog
{
    // Community-tracked list of every flag Roblox ships (with current default values).
    const string Url = "https://raw.githubusercontent.com/MaximumADHD/Roblox-FFlag-Tracker/main/PCDesktopClient.json";
    static readonly Regex NameRx = new(@"^(?:DF|SF|F)(Flag|Int|String|Log)", RegexOptions.Compiled);
    static string Cache => Path.Combine(Store.Dir, "catalog.json");
    static string EtagPath => Path.Combine(Store.Dir, "catalog.etag");

    public static string TypeOf(string name) { var m = NameRx.Match(name); return m.Success ? m.Groups[1].Value : "Other"; }

    public static List<(string Name, string Default)> Load()
    {
        try { if (File.Exists(Cache)) return Parse(File.ReadAllBytes(Cache)); } catch { }
        return new();
    }

    static List<(string Name, string Default)> Parse(byte[] bytes)
    {
        using var d = JsonDocument.Parse(bytes);
        var root = d.RootElement; var best = root; int bc = 0;
        if (root.ValueKind == JsonValueKind.Object)
        {
            bc = root.EnumerateObject().Count();
            foreach (var p in root.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object) { var c = p.Value.EnumerateObject().Count(); if (c > bc) { best = p.Value; bc = c; } }
        }
        var list = new List<(string Name, string Default)>();
        if (best.ValueKind == JsonValueKind.Object)
            foreach (var p in best.EnumerateObject())
            {
                if (!NameRx.IsMatch(p.Name)) continue;
                var v = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.True => "True",
                    JsonValueKind.False => "False",
                    JsonValueKind.Number => p.Value.GetRawText(),
                    _ => ""
                };
                list.Add((p.Name, v));
            }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static async Task<(bool Changed, string Message)> UpdateAsync()
    {
        try
        {
            Directory.CreateDirectory(Store.Dir);
            var req = new HttpRequestMessage(HttpMethod.Get, Url);
            if (File.Exists(EtagPath) && File.Exists(Cache)) req.Headers.TryAddWithoutValidation("If-None-Match", File.ReadAllText(EtagPath));
            using var res = await Net.Http.SendAsync(req);
            Store.Data.Settings.LastCatalogCheck = DateTime.UtcNow;
            if (res.StatusCode == HttpStatusCode.NotModified) return (false, "Flag list is already up to date.");
            res.EnsureSuccessStatusCode();
            var bytes = await res.Content.ReadAsByteArrayAsync();
            var parsed = Parse(bytes);
            if (parsed.Count < 500) return (false, "Downloaded list looked wrong; kept the previous one.");
            File.WriteAllBytes(Cache, bytes);
            if (res.Headers.ETag != null) File.WriteAllText(EtagPath, res.Headers.ETag.ToString());
            return (true, $"Flag list updated: {parsed.Count:N0} flags.");
        }
        catch (Exception ex) { return (false, "Couldn't update flag list: " + ex.Message); }
    }
}

public record ApplyResult(int Written, int Flags, string Names);

public static class Targets
{
    /// Finds Bloxstrap / Voidstrap / Fishstrap / any bootstrapper with a Modifications folder, plus plain Roblox.
    public static List<Target> Discover()
    {
        var list = new List<Target>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        try
        {
            foreach (var d in Directory.GetDirectories(local))
            {
                var name = Path.GetFileName(d);
                if (name.Equals("Roblox", StringComparison.OrdinalIgnoreCase)) continue;
                var mods = Path.Combine(d, "Modifications");
                if (Directory.Exists(mods))
                    list.Add(new Target { Id = name.ToLowerInvariant(), Name = name, Dir = Path.Combine(mods, "ClientSettings") });
            }
        }
        catch { }
        try
        {
            var vers = Path.Combine(local, "Roblox", "Versions");
            if (Directory.Exists(vers))
            {
                var latest = Directory.GetDirectories(vers, "version-*")
                    .Where(v => File.Exists(Path.Combine(v, "RobloxPlayerBeta.exe")))
                    .OrderByDescending(v => Directory.GetLastWriteTimeUtc(v)).FirstOrDefault();
                if (latest != null) list.Add(new Target { Id = "roblox", Name = "Roblox (direct)", Dir = Path.Combine(latest, "ClientSettings"), Vanilla = true });
            }
        }
        catch { }
        var s = Store.Data.Settings;
        if (!s.TargetsInit && list.Count > 0)
        {
            var pick = list.Where(t => !t.Vanilla).ToList();
            if (pick.Count == 0) pick = list;
            s.TargetIds = pick.Select(t => t.Id).ToList();
            s.TargetsInit = true;
        }
        return list;
    }

    public static ApplyResult Apply(bool onlyIfChanged)
    {
        var flags = Store.Compose();
        var json = JsonSerializer.Serialize(flags, new JsonSerializerOptions { WriteIndented = true });
        int written = 0; var names = new List<string>();
        foreach (var t in Discover().Where(t => t.Selected))
        {
            try
            {
                Directory.CreateDirectory(t.Dir);
                var path = Path.Combine(t.Dir, "ClientAppSettings.json");
                if (onlyIfChanged && File.Exists(path) && File.ReadAllText(path) == json) { names.Add(t.Name); continue; }
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, true);
                written++; names.Add(t.Name);
            }
            catch (Exception ex) { names.Add($"{t.Name} (failed: {ex.Message})"); }
        }
        if (names.Count == 0) throw new InvalidOperationException("No Roblox install or bootstrapper selected. See Settings > Targets.");
        return new ApplyResult(written, flags.Count, string.Join(", ", names));
    }
}

public static class AppUpdater
{
    public const string Repo = "YOUR-USER/FlagDeck";   // set to your GitHub repo
    public record Release(string Version, string InstallerUrl, string? ShaUrl, string Page);
    public static Version Current => typeof(AppUpdater).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public static async Task<Release?> CheckAsync()
    {
        if (Repo.StartsWith("YOUR-USER")) throw new InvalidOperationException("Set AppUpdater.Repo to your GitHub repo first.");
        var json = await Net.Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        using var d = JsonDocument.Parse(json);
        var root = d.RootElement;
        var tag = root.GetProperty("tag_name").GetString()!.TrimStart('v');
        if (!Version.TryParse(tag, out var v) || v <= Current) return null;
        string? exe = null, sha = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString()!; var u = a.GetProperty("browser_download_url").GetString()!;
            if (n.EndsWith("-Setup.exe")) exe = u; else if (n.EndsWith("-Setup.exe.sha256")) sha = u;
        }
        return exe == null ? null : new Release(tag, exe, sha, root.GetProperty("html_url").GetString()!);
    }

    public static async Task<bool> InstallAsync(Release r)
    {
        if (r.ShaUrl == null) { Process.Start(new ProcessStartInfo(r.Page) { UseShellExecute = true }); return false; }
        var bytes = await Net.Http.GetByteArrayAsync(r.InstallerUrl);
        var expected = (await Net.Http.GetStringAsync(r.ShaUrl)).Trim().Split(' ', '\t')[0];
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Checksum mismatch. Update aborted.");
        var path = Path.Combine(Path.GetTempPath(), "FlagDeck-Setup.exe");
        File.WriteAllBytes(path, bytes);
        Process.Start(new ProcessStartInfo(path, "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true });
        return true;
    }
}
