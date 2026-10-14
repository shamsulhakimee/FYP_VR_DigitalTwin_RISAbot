using System.Text.Json.Serialization;

namespace RisaBotApp.Models;

/// <summary>Response from GET /api/scan.</summary>
public class ScanResponse
{
    [JsonPropertyName("networks")]
    public List<WifiNetwork> Networks { get; set; } = [];
}

/// <summary>One WiFi network entry from the scan result.</summary>
public class WifiNetwork
{
    [JsonPropertyName("ssid")]
    public string Ssid { get; set; } = string.Empty;

    /// <summary>Signal strength 0–100 (nmcli scale).</summary>
    [JsonPropertyName("signal")]
    public int Signal { get; set; }

    /// <summary>Security type string, e.g. "WPA2" or "Open".</summary>
    [JsonPropertyName("security")]
    public string Security { get; set; } = string.Empty;

    // ---- Computed helpers for UI binding ----

    /// <summary>True when no password is required.</summary>
    [JsonIgnore]
    public bool IsOpen => Security.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
                          string.IsNullOrWhiteSpace(Security);

    /// <summary>Signal bars 1–4 for icon selection.</summary>
    [JsonIgnore]
    public int SignalBars => Signal switch
    {
        >= 75 => 4,
        >= 50 => 3,
        >= 25 => 2,
        _     => 1
    };

    // SignalIcon used to live here, returning wifi_1.png … wifi_4.png. Those were MAUI
    // Resources/Images entries that this project never carried over — the strength meter is
    // drawn as geometry in WifiSetupView now — so all it did was name four files that do not
    // exist.
}
