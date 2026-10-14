using System.IO;
using System.Text.Json;

namespace RisaBotApp.Services;

/// <summary>
/// Replacement for MAUI <c>Preferences</c>.
///
/// MAUI gave every platform a key/value store backed by the OS (SharedPreferences,
/// the WinRT ApplicationData container, …). WPF has no such thing, so the app owns a
/// single JSON file under %APPDATA%\RisaBot\settings.json and keeps it in memory.
///
/// The whole file is rewritten on each Set. That is fine here: the store holds one
/// key (the paired robot) and is written on pairing events, not on a timer.
///
/// Writes go through a temp file plus <see cref="File.Replace"/> so a crash mid-write
/// can't leave a truncated settings.json behind — the previous pairing survives.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _values;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RisaBot");

        Directory.CreateDirectory(dir);
        _path   = Path.Combine(dir, "settings.json");
        _values = Load(_path);
    }

    // ── Read ────────────────────────────────────────────────────────────────

    public string Get(string key, string fallback = "")
    {
        lock (_gate)
            return _values.TryGetValue(key, out var v) ? v : fallback;
    }

    public bool Contains(string key)
    {
        lock (_gate)
            return _values.ContainsKey(key);
    }

    // ── Write ───────────────────────────────────────────────────────────────

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
            Flush();
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            if (_values.Remove(key)) Flush();
        }
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private static Dictionary<string, string> Load(string path)
    {
        // A missing or corrupt file means "no settings yet", not a fatal error.
        // Losing a pairing is recoverable; refusing to start is not.
        try
        {
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private void Flush()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_values, _json));

            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else                    File.Move(tmp, _path);
        }
        catch
        {
            // Disk full, read-only profile, AV lock — the in-memory copy is still
            // correct for this session, so swallow rather than take the app down.
        }
    }
}
