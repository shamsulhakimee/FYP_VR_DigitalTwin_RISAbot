using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using RisaBotApp.Models;

namespace RisaBotApp.Services;

/// <summary>
/// Encapsulates parallel network scanning for all available RISA-Bots on the local Wi-Fi or hotspot.
/// Enables NoMachine-style choice-based device selection.
/// </summary>
public class RobotDiscoveryService
{
    private readonly ApiClient _api;

    // Fast probe timeout for parallel scanning (2.5 seconds per probe)
    private const int ScanProbeTimeoutMs = 2_500;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    // Common default candidates to scan in parallel
    private static readonly string[] DefaultMdnsCandidates =
    [
        "risabot10.local",
        "risabot9.local",
        "risabot8.local",
        "risabot7.local",
        "risabot6.local",
        "risabot5.local",
        "risabot4.local",
        "risabot3.local",
        "risabot2.local",
        "risabot1.local",
        "risabot.local",
    ];

    public RobotDiscoveryService(ApiClient api)
    {
        _api = api;
    }

    // ── Single probe helper ──────────────────────────────────────────────────

    /// <summary>
    /// Probes a single HTTP target URL and returns a DiscoveredRobotDevice if reachable, null otherwise.
    /// Uses a short-lived HttpClient so shared singleton state is never touched.
    /// </summary>
    private static async Task<DiscoveredRobotDevice?> ProbeDeviceAsync(string targetHostOrIp, bool isHotspot, PairedRobot? pairedRobot, CancellationToken ct)
    {
        try
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(ScanProbeTimeoutMs) };
            var baseUrl = $"http://{targetHostOrIp}:8000";
            var url = $"{baseUrl}/api/robot_info";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ScanProbeTimeoutMs);

            var response = await client.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var info = await response.Content.ReadFromJsonAsync<RobotInfo>(_json, cts.Token);
            if (info is null) return null;

            bool isPaired = pairedRobot is not null && (
                string.Equals(pairedRobot.Serial, info.Serial, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pairedRobot.MdnsHostname, info.MdnsHostname, StringComparison.OrdinalIgnoreCase)
            );

            return new DiscoveredRobotDevice
            {
                Info      = info,
                BaseUrl   = baseUrl,
                IsHotspot = isHotspot,
                IsPaired  = isPaired,
            };
        }
        catch
        {
            return null; // Silent catch — candidate unreachable
        }
    }

    /// <summary>
    /// Resolves a .local mDNS hostname to an IP string using Dns.GetHostAddresses as DNS fallback.
    /// </summary>
    private static async Task<string?> TryResolveMdnsToIpAsync(string hostname, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1_500);

            var addresses = await Dns.GetHostAddressesAsync(hostname, cts.Token);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return ipv4?.ToString();
        }
        catch
        {
            return null;
        }
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Scans the local network and hotspot in parallel to discover ALL online RISA-Bots.
    /// Returns a list of DiscoveredRobotDevice choices for the NoMachine-style selection grid.
    /// </summary>
    public async Task<List<DiscoveredRobotDevice>> ScanAllRobotsAsync(PairedRobot? pairedRobot, CancellationToken ct = default)
    {
        var candidates = new List<(string host, bool isHotspot)>();

        // 1. Remembered paired robot host & IP
        if (pairedRobot is not null)
        {
            if (!string.IsNullOrWhiteSpace(pairedRobot.MdnsHostname))
                candidates.Add((pairedRobot.MdnsHostname, false));
            if (!string.IsNullOrWhiteSpace(pairedRobot.Wlan0Ip))
                candidates.Add((pairedRobot.Wlan0Ip, false));
        }

        // 2. Provisioning AP Hotspot (192.168.4.1)
        candidates.Add(("192.168.4.1", true));

        // 3. Known mDNS candidate hostnames
        foreach (var mdns in DefaultMdnsCandidates)
        {
            if (!candidates.Any(c => string.Equals(c.host, mdns, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add((mdns, false));
            }
        }

        // Run all probes in parallel
        var probeTasks = candidates.Select(async c =>
        {
            // Resolve .local ourselves instead of handing the hostname to HttpClient.
            // Avahi advertises AAAA records once the network hands out IPv6, but every
            // service on the robot binds 0.0.0.0, so a v6 connect blackholes and burns
            // the entire probe budget before v4 is ever attempted — the robot then looks
            // offline on any IPv6-capable network. Probing the resolved address also
            // keeps BaseUrl v4-only, which matters because the camera WebView is pointed
            // at a URL derived from it and would otherwise hang the same way.
            if (c.host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                var ip = await TryResolveMdnsToIpAsync(c.host, ct);
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    var viaIp = await ProbeDeviceAsync(ip, c.isHotspot, pairedRobot, ct);
                    if (viaIp is not null) return viaIp;
                }
            }

            // Hostname probe still runs when resolution failed outright, since Windows
            // mDNS is flaky and HttpClient sometimes resolves what Dns cannot.
            return await ProbeDeviceAsync(c.host, c.isHotspot, pairedRobot, ct);
        });

        var results = await Task.WhenAll(probeTasks);

        // Filter nulls and deduplicate by Serial / MdnsHostname
        var discoveredList = new List<DiscoveredRobotDevice>();
        var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dev in results.Where(r => r is not null)!)
        {
            var key = !string.IsNullOrWhiteSpace(dev!.Info.Serial) && dev.Info.Serial != "unknown"
                ? dev.Info.Serial
                : dev.Info.MdnsHostname;

            if (seenSerials.Add(key))
            {
                discoveredList.Add(dev);
            }
        }

        // Sort: Paired devices first, then local Wi-Fi devices, then hotspot devices
        return discoveredList
            .OrderByDescending(d => d.IsPaired)
            .ThenBy(d => d.IsHotspot)
            .ThenBy(d => d.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Attempts to connect to a user-entered manual hostname or IP address directly.
    /// </summary>
    public async Task<DiscoveredRobotDevice?> TryManualConnectAsync(string inputHostOrIp, PairedRobot? pairedRobot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputHostOrIp)) return null;

        var clean = inputHostOrIp.Trim()
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        if (clean.EndsWith(":8000"))
            clean = clean.Replace(":8000", "");

        // Same v4-first ordering as the parallel scan — see ScanAllRobotsAsync.
        if (clean.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            var ip = await TryResolveMdnsToIpAsync(clean, ct);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                var viaIp = await ProbeDeviceAsync(ip, false, pairedRobot, ct);
                if (viaIp is not null) return viaIp;
            }
        }

        return await ProbeDeviceAsync(clean, false, pairedRobot, ct);
    }
}
