// Implicit usings for a WPF SDK project do not include System.Net.Http — that set is
// specific to Microsoft.NET.Sdk.Web. Importing the .Json sub-namespace does not pull the
// parent in either, so HttpClient itself has to be named explicitly.
using System.IO;   // nor System.IO — File, for streaming logger downloads to disk
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using RisaBotApp.Models;

namespace RisaBotApp.Services;

/// <summary>
/// Centralised HTTP client for all RISA-Bot backend REST calls.
/// Supports port 8000 (WiFi provisioning portal) and port 8080 (ROS 2 dashboard node data & camera feeds).
/// </summary>
public class ApiClient
{
    // ── Defaults ─────────────────────────────────────────────────────────────
    private const string DefaultHotspotUrl  = "http://192.168.4.1:8000";
    private const int    DiscoveryTimeoutMs = 5_000;
    private const int    RosDataTimeoutMs   = 1_500;   // fast timeout for 500ms live polling
    // The scan plot polls at 250ms, so a request still in flight when the next one is due
    // is already useless — better to drop it than to queue behind it.
    private const int    LidarTimeoutMs     = 1_000;
    private const int    ScanTimeoutMs      = 30_000;
    private const int    ConnectTimeoutMs   = 60_000;
    private const int    DefaultTimeoutMs   = 10_000;
    // systemctl start/stop blocks until the whole node graph is up or reaped, which
    // routinely outruns DefaultTimeoutMs. Undershooting reports a timeout for an
    // action that actually succeeded, leaving the toggle out of sync with the robot.
    private const int    LaunchActionTimeoutMs = 90_000;
    // save_defaults reads every live parameter off the node graph, rewrites params.yaml and
    // flushes it to the robot's SD card. On a loaded RDK X5 that has been measured past ten
    // seconds, and a timeout here is the worst possible outcome — the write may well have
    // happened, so the app would report failure over a file it had just overwritten.
    private const int    SaveDefaultsTimeoutMs = 30_000;
    // /api/model/info hashes the model on the robot before answering, which on an RDK X5
    // under load is slower than a plain status read.
    private const int    ModelInfoTimeoutMs = 20_000;
    // 7.5 MB over the robot's own AP is comfortably under a minute, but "comfortably" does
    // not describe a competition venue with forty radios in one hall. The restart variant adds
    // the systemctl restart, which blocks until the whole node graph is up.
    private const int    ModelUploadTimeoutMs = 180_000;
    private const int    ModelUploadWithRestartTimeoutMs = 300_000;
    private const int    UploadChunkBytes = 64 * 1024;
    // Mirrors the bounds the robot enforces, so an obviously wrong file is rejected here
    // rather than after pushing megabytes at it.
    private const long   ModelMinBytes = 64 * 1024;
    private const long   ModelMaxBytes = 256L * 1024 * 1024;
    // The Map page polls status at 1 s, so a request still in flight when the next is due
    // is already worthless — the same reasoning as LidarTimeoutMs.
    private const int    SlamStatusTimeoutMs = 2_000;
    // A 15x15 m arena at 5 cm is ~90k cells; as a PNG that is tens of KB, but over the
    // robot's own AP in a busy hall it still deserves more room than a status read.
    private const int    SlamMapTimeoutMs = 15_000;
    // save/restart/pause each cross a ROS service call whose own spin-wait allows 8 s, on
    // top of slam_toolbox writing a .pgm to the SD card while the scan matcher is running.
    private const int    SlamServiceTimeoutMs = 45_000;

    private readonly HttpClient _http;
    private string _baseUrl = DefaultHotspotUrl;

    /// <summary>
    /// Separate client for model transfers, for the same reason MjpegStreamReader has its own:
    /// the DI-registered HttpClient carries a 10 s Timeout, and that is a hard ceiling on the
    /// whole operation which no per-request CancellationTokenSource can lift. Uploading 7.5 MB
    /// through it would fail at ten seconds every time. Timeout is infinite here and the real
    /// deadline comes from the CTS at each call site.
    ///
    /// Lazy so the socket handler is only built if someone actually opens the model screen.
    /// </summary>
    private static readonly Lazy<HttpClient> _uploadHttp = new(() =>
        new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

    private static HttpClient UploadHttp => _uploadHttp.Value;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(HttpClient httpClient)
    {
        _http = httpClient;
    }

    // ── Base URL management ───────────────────────────────────────────────────

    public void UpdateBaseUrl(string url)
    {
        _baseUrl = url.TrimEnd('/');
    }

    public string CurrentBaseUrl => _baseUrl;

    /// <summary>
    /// Derived base URL targeting the ROS 2 dashboard server on port 8080
    /// (e.g. "http://risabot10.local:8080").
    /// </summary>
    public string Port8080BaseUrl
    {
        get
        {
            try
            {
                var uri = new Uri(_baseUrl);
                return $"{uri.Scheme}://{uri.Host}:8080";
            }
            catch
            {
                return "http://192.168.4.1:8080";
            }
        }
    }

    /// <summary>
    /// Base URL for the go2rtc stream server on port 1984.
    /// </summary>
    public string Go2rtcBaseUrl
    {
        get
        {
            try
            {
                var uri = new Uri(_baseUrl);
                return $"{uri.Scheme}://{uri.Host}:1984";
            }
            catch
            {
                return "http://192.168.4.1:1984";
            }
        }
    }

    /// <summary>
    /// MJPEG stream URL served by go2rtc on port 1984.
    /// Single stream slot (astra_camera) — URL never changes between view switches.
    /// View switching is handled server-side by ros2go2rtc_bridge via ROS parameter.
    /// Hits the raw endpoint rather than go2rtc's stream.html player page: the WebView
    /// renders the multipart stream directly instead of loading a JS player on top of it.
    /// </summary>
    public string CameraStreamUrl => $"{Go2rtcBaseUrl}/api/stream.mjpeg?src=astra_camera";

    // ── Port 8080 ROS 2 Live Endpoints ────────────────────────────────────────

    /// <summary>GET http://<robot_host>:8080/data — live ROS 2 telemetry data.</summary>
    public async Task<ApiResult<RosDashboardData>> GetRosDashboardDataAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RosDataTimeoutMs);
        var url = $"{Port8080BaseUrl}/data";
        return await DirectGetAsync<RosDashboardData>(url, cts.Token);
    }

    /// <summary>GET http://<robot_host>:8080/api/set_cam_view?view={view} — switch active camera node processing view.</summary>
    public async Task<ApiResult<bool>> SetCameraViewAsync(string view, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        var url = $"{Port8080BaseUrl}/api/set_cam_view?view={view}";
        try
        {
            var res = await _http.GetAsync(url, cts.Token);
            return res.IsSuccessStatusCode ? ApiResult<bool>.Success(true) : ApiResult<bool>.Failure("Failed to set view");
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure(ex.Message);
        }
    }

    /// <summary>POST http://<robot_host>:8080/api/reset_odom — reset odometry distance & position counters.</summary>
    public async Task<ApiResult<bool>> ResetOdometryAsync(CancellationToken ct = default) =>
        await DashboardPostAsync($"{Port8080BaseUrl}/api/reset_odom", new { },
                                 DefaultTimeoutMs, ct);

    /// <summary>POST http://<robot_host>:8080/api/reset_competition — send competition command (RESET, LAP1, LAP2).</summary>
    public async Task<ApiResult<bool>> SendCompetitionCommandAsync(string command, CancellationToken ct = default)
    {
        // Goes through the ok-checking helper rather than DirectPostAsync<_, bool>: an
        // unrecognised command comes back 200 with {"ok": false}, which the plain helper
        // would report as a success.
        return await DashboardPostAsync($"{Port8080BaseUrl}/api/reset_competition",
                                        new { command }, DefaultTimeoutMs, ct);
    }

    /// <summary>GET http://<robot_host>:8080/lidar_data — one LiDAR scan in the robot frame,
    /// plus the tunnel wall-follower's debug overlay when it is running.</summary>
    public async Task<ApiResult<LidarData>> GetLidarDataAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(LidarTimeoutMs);
        return await DirectGetAsync<LidarData>($"{Port8080BaseUrl}/lidar_data", cts.Token);
    }

    // ── Port 8080 IMU calibration ─────────────────────────────────────────────

    /// <summary>
    /// POST /api/calibrate_imu. Three steps in a fixed order, each requiring the robot to
    /// be physically held in a known attitude first: zero it flat, then nose straight up
    /// for pitch, then on its right side for roll. The result lives in RAM only —
    /// <see cref="SaveParameterDefaultsAsync"/> is what makes it survive a restart.
    /// </summary>
    public async Task<ApiResult<bool>> CalibrateImuZeroAsync(CancellationToken ct = default) =>
        await DashboardPostAsync($"{Port8080BaseUrl}/api/calibrate_imu",
                                 new ImuCalibrationRequest { Action = "zero" },
                                 DefaultTimeoutMs, ct);

    /// <summary>POST /api/calibrate_imu — map one axis, with the robot held at
    /// <paramref name="targetDegrees"/> on it. Axis is "pitch" or "roll".</summary>
    public async Task<ApiResult<bool>> CalibrateImuAxisAsync(
        string axis, double targetDegrees = 90.0, CancellationToken ct = default) =>
        await DashboardPostAsync($"{Port8080BaseUrl}/api/calibrate_imu",
                                 new ImuCalibrationRequest
                                 {
                                     Action = "set_scale",
                                     Axis   = axis,
                                     Target = targetDegrees,
                                 },
                                 DefaultTimeoutMs, ct);

    // ── Port 8080 parameters ──────────────────────────────────────────────────

    /// <summary>GET /api/get_param?node=…&amp;param=… — read one live ROS parameter, along
    /// with its packaged default when params.yaml declares one.</summary>
    public async Task<ApiResult<ParamResponse>> GetParameterAsync(
        string node, string param, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        var url = $"{Port8080BaseUrl}/api/get_param" +
                  $"?node={Uri.EscapeDataString(node)}&param={Uri.EscapeDataString(param)}";
        return await DirectGetAsync<ParamResponse>(url, cts.Token);
    }

    /// <summary>
    /// POST /api/set_param — change one live ROS parameter. Takes effect immediately and
    /// is lost on the next restart; see <see cref="SaveParameterDefaultsAsync"/>.
    /// The value is sent as text because the robot stringifies it either way.
    /// </summary>
    public async Task<ApiResult<bool>> SetParameterAsync(
        string node, string param, string value, CancellationToken ct = default) =>
        await DashboardPostAsync($"{Port8080BaseUrl}/api/set_param",
                                 new SetParamRequest { Node = node, Param = param, Value = value },
                                 DefaultTimeoutMs, ct);

    /// <summary>
    /// POST /api/save_defaults — write every live parameter back into config/params.yaml on the
    /// robot. This edits the source tree, so it is the one call here that outlives a rebuild.
    ///
    /// Unlike the other dashboard POSTs this does not go through DashboardPostAsync: the
    /// response body carries how many parameters were actually rewritten and the path it wrote
    /// to, and collapsing that to a bool would throw away the only evidence the operator has
    /// that anything happened.
    /// </summary>
    public async Task<ApiResult<SaveDefaultsResponse>> SaveParameterDefaultsAsync(
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(SaveDefaultsTimeoutMs);

        var res = await DirectPostAsync<object, SaveDefaultsResponse>(
            $"{Port8080BaseUrl}/api/save_defaults", new { }, cts.Token);

        if (!res.IsSuccess)  return ApiResult<SaveDefaultsResponse>.Failure(res.ErrorMessage!);
        if (res.Data is null) return ApiResult<SaveDefaultsResponse>.Failure("Robot returned an empty response.");

        // Same trap as everywhere else on port 8080: failures arrive as 200 with ok=false.
        return res.Data.Ok
            ? ApiResult<SaveDefaultsResponse>.Success(res.Data)
            : ApiResult<SaveDefaultsResponse>.Failure(
                res.Data.Error ?? res.Data.Msg ?? "Robot rejected the request.");
    }

    // ── Port 8080 record and playback ─────────────────────────────────────────

    /// <summary>
    /// POST /api/record_playback. Bare actions are record, stop, playback, save and list;
    /// save, load, delete and set_active take a name. Passing a name with a bare-only
    /// action, or omitting one from a named action, is rejected as an invalid command
    /// rather than defaulted — hence the two separate methods.
    /// </summary>
    public async Task<ApiResult<bool>> SendRecordPlaybackCommandAsync(
        string action, CancellationToken ct = default) =>
        await DashboardPostAsync($"{Port8080BaseUrl}/api/record_playback",
                                 new RecordPlaybackRequest { Action = action },
                                 DefaultTimeoutMs, ct);

    /// <summary>POST /api/record_playback with a recording name — save, load, delete or
    /// set_active.</summary>
    public async Task<ApiResult<bool>> SendRecordPlaybackCommandAsync(
        string action, string name, CancellationToken ct = default) =>
        await DashboardPostAsync($"{Port8080BaseUrl}/api/record_playback",
                                 new RecordPlaybackRequest { Action = action, Name = name },
                                 DefaultTimeoutMs, ct);

    /// <summary>GET /api/recording_data?name=… — the stored samples of one saved recording.</summary>
    public async Task<ApiResult<RecordingDataResponse>> GetRecordingDataAsync(
        string name, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        var url = $"{Port8080BaseUrl}/api/recording_data?name={Uri.EscapeDataString(name)}";
        return await DirectGetAsync<RecordingDataResponse>(url, cts.Token);
    }

    // ── Port 8080 subsystem data logger ───────────────────────────────────────

    /// <summary>POST /api/logger/start — begin a logging session. The label is a prefix,
    /// not the session name; the robot returns the name it actually used.</summary>
    public async Task<ApiResult<LoggerStartResponse>> StartLoggerAsync(
        string label, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        return await DirectPostAsync<object, LoggerStartResponse>(
            $"{Port8080BaseUrl}/api/logger/start", new { label }, cts.Token);
    }

    /// <summary>POST /api/logger/stop — close the session and flush its CSVs.</summary>
    public async Task<ApiResult<bool>> StopLoggerAsync(CancellationToken ct = default) =>
        await DashboardPostAsync($"{Port8080BaseUrl}/api/logger/stop", new { },
                                 DefaultTimeoutMs, ct);

    /// <summary>GET /api/logger/status — whether a session is running, and how big it is.</summary>
    public async Task<ApiResult<LoggerStatusResponse>> GetLoggerStatusAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        return await DirectGetAsync<LoggerStatusResponse>(
            $"{Port8080BaseUrl}/api/logger/status", cts.Token);
    }

    /// <summary>GET /api/logger/list — every completed session, newest first.</summary>
    public async Task<ApiResult<LoggerListResponse>> GetLoggerSessionsAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        return await DirectGetAsync<LoggerListResponse>(
            $"{Port8080BaseUrl}/api/logger/list", cts.Token);
    }

    /// <summary>
    /// URL of one session's CSV. Not fetched here — this returns the address so the caller
    /// can hand it to the shell, since the response is a file download rather than JSON
    /// and the useful thing to do with it is save it, not parse it.
    /// </summary>
    public string BuildLoggerDownloadUrl(string session, string fileName = "lane_follower.csv") =>
        $"{Port8080BaseUrl}/api/logger/download" +
        $"?session={Uri.EscapeDataString(session)}&file={Uri.EscapeDataString(fileName)}";

    /// <summary>
    /// Saves one session CSV to a local path.
    ///
    /// Streamed to the file rather than buffered into a byte[]: a long PID run at 10 Hz is a
    /// few MB per subsystem, and there is nothing to inspect in transit.
    ///
    /// A missing file is a 404 from the robot, which is a normal answer rather than a fault —
    /// the logger only writes the five subsystems it knows about, and an interrupted session
    /// (power cut mid-run, so no session_meta.json) can be listed with fewer files than that.
    /// Callers get <c>false</c> for it and are expected to carry on with the rest.
    /// </summary>
    public async Task<ApiResult<bool>> DownloadLoggerFileAsync(
        string session, string fileName, string destinationPath, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeoutMs);

            var url = BuildLoggerDownloadUrl(session, fileName);
            using var response = await _http.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return ApiResult<bool>.Failure($"{fileName} is not in this session.");

            response.EnsureSuccessStatusCode();

            // Written to a temp file and moved into place so a failure part-way through does
            // not leave a truncated CSV that looks like a complete one.
            var temp = destinationPath + ".part";

            await using (var source = await response.Content.ReadAsStreamAsync(cts.Token))
            await using (var target = File.Create(temp))
            {
                await source.CopyToAsync(target, cts.Token);
            }

            File.Move(temp, destinationPath, overwrite: true);
            return ApiResult<bool>.Success(true);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<bool>.Failure($"{fileName} timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<bool>.Failure($"{fileName}: network error — {ex.Message}");
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure($"{fileName}: {ex.Message}");
        }
    }

    // ── Port 8080 SLAM / mapping ──────────────────────────────────────────────

    /// <summary>
    /// GET /api/slam/status — map geometry, robot pose in the map frame, and liveness.
    ///
    /// Polled once a second and deliberately given a short deadline: a status read that
    /// outlives its own poll interval is stale by the time it lands, and the page has a
    /// perfectly good "SLAM not running" state to fall back to.
    /// </summary>
    public async Task<ApiResult<SlamStatus>> GetSlamStatusAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(SlamStatusTimeoutMs);
        return await DirectGetAsync<SlamStatus>($"{Port8080BaseUrl}/api/slam/status", cts.Token);
    }

    /// <summary>
    /// GET /api/slam/map.png — the occupancy grid as a greyscale PNG.
    ///
    /// A 404 is a normal answer, not a fault: slam_toolbox publishes nothing until it has
    /// processed a scan, and the stack may have been launched with <c>slam:=false</c>. The
    /// caller should already have decided from <see cref="GetSlamStatusAsync"/> that the
    /// sequence number moved, so this is only paid when there is something new to see.
    /// </summary>
    public async Task<ApiResult<SlamMapImage>> GetSlamMapImageAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(SlamMapTimeoutMs);

            using var response = await _http.GetAsync(
                $"{Port8080BaseUrl}/api/slam/map.png", cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return ApiResult<SlamMapImage>.Failure("No map yet.");

            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);

            // The robot stamps the sequence it rendered, which may already be ahead of the
            // status that triggered this fetch. Trusting the header rather than the status
            // keeps the cache key honest and avoids re-fetching the same bytes forever.
            var seq = 0;
            if (response.Headers.TryGetValues("X-Map-Seq", out var values))
                int.TryParse(values.FirstOrDefault(), out seq);

            return ApiResult<SlamMapImage>.Success(new SlamMapImage { Png = bytes, Seq = seq });
        }
        catch (TaskCanceledException)
        {
            return ApiResult<SlamMapImage>.Failure("Map request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<SlamMapImage>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ApiResult<SlamMapImage>.Failure($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>GET /api/slam/maps — maps already saved in ~/risabot_maps on the robot.</summary>
    public async Task<ApiResult<SlamMapsResponse>> GetSavedMapsAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        return await DirectGetAsync<SlamMapsResponse>(
            $"{Port8080BaseUrl}/api/slam/maps", cts.Token);
    }

    /// <summary>
    /// POST /api/slam/save — write {name}.pgm + {name}.yaml on the robot.
    ///
    /// Through UploadHttp, not the injected client: this crosses a ROS service call whose
    /// bounded spin-wait alone allows eight seconds, and the DI client's 10 s Timeout is a
    /// hard ceiling no CancellationTokenSource here could raise. Timing out on a save that
    /// actually succeeded is the worst outcome — the operator would press it again.
    /// </summary>
    public async Task<ApiResult<SlamSaveResponse>> SaveSlamMapAsync(
        string name, CancellationToken ct = default)
    {
        var res = await UncappedPostAsync<object, SlamSaveResponse>(
            $"{Port8080BaseUrl}/api/slam/save", new { name }, SlamServiceTimeoutMs, ct);

        if (!res.IsSuccess) return res;
        if (res.Data is null) return ApiResult<SlamSaveResponse>.Failure("Robot returned an empty response.");

        return res.Data.Ok
            ? res
            : ApiResult<SlamSaveResponse>.Failure(res.Data.Error ?? "Robot could not save the map.");
    }

    /// <summary>
    /// POST /api/slam/restart — clear the pose graph and start mapping again from here.
    ///
    /// Destructive: the session's map is gone. The failure message is worth surfacing
    /// verbatim, because the expected one — a slam_toolbox build with no reset service —
    /// is a permanent condition the UI should disable the button for, not a transient error.
    /// </summary>
    public async Task<ApiResult<bool>> RestartSlamAsync(CancellationToken ct = default) =>
        await UncappedDashboardPostAsync($"{Port8080BaseUrl}/api/slam/restart",
                                         new { }, SlamServiceTimeoutMs, ct);

    /// <summary>
    /// POST /api/slam/pause — stop or resume scan processing.
    ///
    /// The robot converts this absolute request into the toggle slam_toolbox actually
    /// exposes, so sending the state already in force is a no-op rather than an inversion.
    /// </summary>
    public async Task<ApiResult<bool>> SetSlamPausedAsync(bool paused, CancellationToken ct = default) =>
        await UncappedDashboardPostAsync($"{Port8080BaseUrl}/api/slam/pause",
                                         new { paused }, SlamServiceTimeoutMs, ct);

    // ── Port 8000 Provisioning / Portal Endpoints ─────────────────────────────

    public async Task<ApiResult<RobotInfo>> GetRobotInfoAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DiscoveryTimeoutMs);
        return await GetAsync<RobotInfo>("/api/robot_info", cts.Token);
    }

    public async Task<ApiResult<StatusResponse>> GetStatusAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        return await GetAsync<StatusResponse>("/api/status", cts.Token);
    }

    public async Task<ApiResult<ScanResponse>> ScanNetworksAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ScanTimeoutMs);
        return await GetAsync<ScanResponse>("/api/scan", cts.Token);
    }

    public async Task<ApiResult<ConnectResponse>> ConnectAsync(string ssid, string password, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ConnectTimeoutMs);
        var body = new ConnectRequest { Ssid = ssid, Password = password };
        return await PostAsync<ConnectRequest, ConnectResponse>("/api/connect", body, cts.Token);
    }

    public async Task<ApiResult<LogsResponse>> GetLogsAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        return await GetAsync<LogsResponse>("/api/logs", cts.Token);
    }

    public async Task<ApiResult<LaunchStatusResponse>> GetLaunchStatusAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeoutMs);
        return await GetAsync<LaunchStatusResponse>("/api/launch_status", cts.Token);
    }

    public async Task<ApiResult<LaunchActionResponse>> StartLaunchAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(LaunchActionTimeoutMs);
        return await PostAsync<object, LaunchActionResponse>("/api/launch_start", new { }, cts.Token);
    }

    public async Task<ApiResult<LaunchActionResponse>> StopLaunchAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(LaunchActionTimeoutMs);
        return await PostAsync<object, LaunchActionResponse>("/api/launch_stop", new { }, cts.Token);
    }

    /// <summary>
    /// Restarts risabot.service and reports the state it settled into.
    ///
    /// This needs a real budget, not the default 10 s. The handler runs
    /// `systemctl restart risabot.service` and then `is-active`, each with a 20 s server-side
    /// timeout, so the request can legitimately be in flight for up to 40 s — and a ROS 2
    /// bringup routinely takes more than ten seconds to come back. Under the default the call
    /// reported "Request timed out" while the restart was in fact succeeding, which is the
    /// worst of both: the operator is told it failed and retries, restarting the stack again.
    ///
    /// UploadHttp rather than the injected client for the same reason as the model endpoints —
    /// that client's 10 s Timeout is a ceiling CancelAfter cannot raise, so passing a longer
    /// token through it would change nothing.
    /// </summary>
    public async Task<ApiResult<RestartRosResponse>> RestartRosAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(LaunchActionTimeoutMs);

        try
        {
            using var response = await UploadHttp.PostAsync(
                BuildUrl("/api/restart_ros"), content: null, cts.Token);

            if (!response.IsSuccessStatusCode)
                return ApiResult<RestartRosResponse>.Failure(
                    await DescribeFailureAsync(response, cts.Token));

            var data = await response.Content.ReadFromJsonAsync<RestartRosResponse>(_json, cts.Token);
            return data is null
                ? ApiResult<RestartRosResponse>.Failure("Robot returned an empty response.")
                : ApiResult<RestartRosResponse>.Success(data);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult<RestartRosResponse>.Failure("Cancelled.");
        }
        catch (OperationCanceledException)
        {
            return ApiResult<RestartRosResponse>.Failure(
                "Restart timed out. The stack may still be coming up — check the Logs tab.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<RestartRosResponse>.Failure($"Network error: {ex.Message}");
        }
    }

    // ── BPU model management (port 8000) ──────────────────────────────────────

    /// <summary>
    /// What YOLO model is on the robot's disk. Hashing 7.5 MB on an RDK X5 is not free, so
    /// <paramref name="includeSha256"/> exists for callers that only want size and mtime.
    /// </summary>
    public async Task<ApiResult<ModelInfoResponse>> GetModelInfoAsync(
        bool includeSha256 = true, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ModelInfoTimeoutMs);

        // Deliberately not the shared GetAsync<T>: that uses the injected HttpClient, whose
        // 10 s Timeout is a hard ceiling that no CancelAfter can raise, so routing through it
        // would silently shorten the 20 s budget above to 10 s. Hashing the model is quick on
        // a warm page cache but this is an ARM board reading 7.5 MB off eMMC, and the reply is
        // what the whole card renders from — worth the headroom it is written to have.
        var url = BuildUrl($"/api/model/info?include_sha256={(includeSha256 ? "true" : "false")}");
        try
        {
            using var response = await UploadHttp.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
                return ApiResult<ModelInfoResponse>.Failure(
                    await DescribeFailureAsync(response, cts.Token));

            var data = await response.Content.ReadFromJsonAsync<ModelInfoResponse>(_json, cts.Token);
            return data is null
                ? ApiResult<ModelInfoResponse>.Failure("Robot returned an empty response.")
                : ApiResult<ModelInfoResponse>.Success(data);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult<ModelInfoResponse>.Failure("Cancelled.");
        }
        catch (OperationCanceledException)
        {
            return ApiResult<ModelInfoResponse>.Failure("Timed out reading the robot's model.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<ModelInfoResponse>.Failure($"Could not reach the robot: {ex.Message}");
        }
    }

    /// <summary>
    /// Replaces the robot's BPU model with a local <c>.bin</c>.
    ///
    /// The body is the raw file, not multipart. The robot reads it with request.stream()
    /// rather than FastAPI's UploadFile, which would drag in python-multipart — a dependency
    /// that is not guaranteed present on the robot and would be a miserable thing to discover
    /// from the field.
    ///
    /// The digest is computed here and sent along so the robot can refuse a truncated
    /// transfer. That matters more than it looks: without it a dropped Wi-Fi link mid-upload
    /// would install a half-written model over a working one, and the symptom would be the
    /// detector failing to load at the next competition run, not an error here.
    ///
    /// <paramref name="restartAfter"/> restarts risabot.service, which is the only way the new
    /// model actually gets used — dnn.load() runs once in SignageDetector.__init__.
    /// </summary>
    public async Task<ApiResult<ModelUploadResponse>> UploadModelAsync(
        string localFilePath,
        bool restartAfter = false,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(localFilePath))
            return ApiResult<ModelUploadResponse>.Failure("That file no longer exists.");

        var info = new FileInfo(localFilePath);
        if (info.Length < ModelMinBytes)
            return ApiResult<ModelUploadResponse>.Failure(
                $"{info.Name} is only {info.Length:N0} bytes — too small to be a compiled model. " +
                "Did you pick the ONNX or a stub by mistake?");
        if (info.Length > ModelMaxBytes)
            return ApiResult<ModelUploadResponse>.Failure(
                $"{info.Name} is {info.Length / (1024.0 * 1024.0):F1} MB, over the " +
                $"{ModelMaxBytes / (1024 * 1024)} MB limit.");

        try
        {
            // Hashed before the upload starts rather than alongside it: the robot needs the
            // digest up front to verify against, and reading the file twice costs
            // milliseconds against a transfer measured in seconds.
            var sha = await ComputeSha256Async(localFilePath, ct);

            var url = BuildUrl("/api/model/upload")
                      + $"?restart={(restartAfter ? "true" : "false")}"
                      + $"&sha256={sha}"
                      + $"&filename={Uri.EscapeDataString(info.Name)}";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(restartAfter ? ModelUploadWithRestartTimeoutMs : ModelUploadTimeoutMs);

            await using var file = new FileStream(
                localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: UploadChunkBytes, useAsync: true);

            using var content = new StreamContent(
                new ProgressStream(file, info.Length, progress), UploadChunkBytes);
            content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentLength = info.Length;

            using var response = await UploadHttp.PostAsync(url, content, cts.Token);

            if (!response.IsSuccessStatusCode)
                return ApiResult<ModelUploadResponse>.Failure(
                    await DescribeFailureAsync(response, cts.Token));

            var data = await response.Content.ReadFromJsonAsync<ModelUploadResponse>(_json, cts.Token);
            return data is null
                ? ApiResult<ModelUploadResponse>.Failure("Robot returned an empty response.")
                : ApiResult<ModelUploadResponse>.Success(data);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult<ModelUploadResponse>.Failure("Upload cancelled.");
        }
        catch (OperationCanceledException)
        {
            return ApiResult<ModelUploadResponse>.Failure(
                "Upload timed out. The robot keeps its previous model when an upload does " +
                "not complete, so nothing was broken — check the link and try again.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<ModelUploadResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (IOException ex)
        {
            return ApiResult<ModelUploadResponse>.Failure($"Could not read {info.Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ApiResult<ModelUploadResponse>.Failure($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores the model the last upload displaced. Cheap insurance: a model can compile,
    /// upload and load and still be wrong — <c>signage_detector.py</c> hardcodes class
    /// indices 0-9, so a model trained with a different class order mismaps silently rather
    /// than failing. Getting back to the previous one should not need a laptop and an SFTP
    /// session.
    /// </summary>
    public async Task<ApiResult<ModelUploadResponse>> RollbackModelAsync(
        bool restartAfter = false, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(restartAfter ? ModelUploadWithRestartTimeoutMs : DefaultTimeoutMs);

        var url = BuildUrl("/api/model/rollback")
                  + $"?restart={(restartAfter ? "true" : "false")}";

        try
        {
            using var response = await UploadHttp.PostAsync(url, content: null, cts.Token);

            if (!response.IsSuccessStatusCode)
                return ApiResult<ModelUploadResponse>.Failure(
                    await DescribeFailureAsync(response, cts.Token));

            var data = await response.Content.ReadFromJsonAsync<ModelUploadResponse>(_json, cts.Token);
            return data is null
                ? ApiResult<ModelUploadResponse>.Failure("Robot returned an empty response.")
                : ApiResult<ModelUploadResponse>.Success(data);
        }
        catch (OperationCanceledException)
        {
            return ApiResult<ModelUploadResponse>.Failure("Rollback timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<ModelUploadResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ApiResult<ModelUploadResponse>.Failure($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>SHA-256 of a local file, streamed so a large model never lands in memory.</summary>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: UploadChunkBytes, useAsync: true);

        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Turns a failed response into something worth showing the operator.
    ///
    /// EnsureSuccessStatusCode is deliberately not used on the model endpoints: FastAPI puts
    /// the actual reason in the body as <c>{"detail": …}</c>, and those reasons are the whole
    /// point here — "this is an ONNX file, compile it first" is a fix, whereas "400 Bad
    /// Request" is a shrug.
    /// </summary>
    private static async Task<string> DescribeFailureAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("detail", out var detail))
                {
                    var text = detail.ValueKind == JsonValueKind.String
                        ? detail.GetString()
                        : detail.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text!;
                }
            }
        }
        catch
        {
            // Not JSON, or the body was already consumed — fall through to the status line.
        }

        return $"Robot rejected the request ({(int)response.StatusCode} {response.ReasonPhrase}).";
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private string BuildUrl(string path) =>
        _baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    private async Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken ct) =>
        await DirectGetAsync<T>(BuildUrl(path), ct);

    private async Task<ApiResult<TResponse>> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct) =>
        await DirectPostAsync<TRequest, TResponse>(BuildUrl(path), body, ct);

    /// <summary>
    /// POST to a port-8080 endpoint and surface the body's <c>ok</c> flag as the result.
    ///
    /// The dashboard node never signals failure with a status code — every handler in
    /// do_POST wraps itself in a try/except and replies 200 with <c>{"ok": false,
    /// "error": …}</c>. Going through DirectPostAsync&lt;_, bool&gt; instead would report
    /// success for a command the robot rejected outright, which is worse than an error:
    /// the UI would move on as though the robot had complied.
    /// </summary>
    private async Task<ApiResult<bool>> DashboardPostAsync<TRequest>(
        string fullUrl, TRequest body, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var res = await DirectPostAsync<TRequest, DashboardApiResponse>(fullUrl, body, cts.Token);
        if (!res.IsSuccess)
            return ApiResult<bool>.Failure(res.ErrorMessage!);

        // A handler that returns an empty body deserialises to null; treat the silence as
        // failure rather than inventing an ok that was never sent.
        if (res.Data is null)
            return ApiResult<bool>.Failure("Robot returned an empty response.");

        return res.Data.Ok
            ? ApiResult<bool>.Success(true)
            : ApiResult<bool>.Failure(res.Data.Error ?? res.Data.Msg ?? "Robot rejected the request.");
    }

    /// <summary>
    /// <see cref="DirectPostAsync"/> over <see cref="UploadHttp"/>.
    ///
    /// Exists because the injected client's 10 s Timeout cannot be raised per request, and
    /// several dashboard endpoints — anything that crosses a ROS service call — legitimately
    /// take longer. The real deadline is the CancellationTokenSource built here.
    /// </summary>
    private async Task<ApiResult<TResponse>> UncappedPostAsync<TRequest, TResponse>(
        string fullUrl, TRequest body, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var payload = JsonSerializer.Serialize(body, _json);
            using var content = new StringContent(
                payload, System.Text.Encoding.UTF8, "application/json");

            using var response = await UploadHttp.PostAsync(fullUrl, content, cts.Token);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<TResponse>(_json, cts.Token);
            return data is null
                ? ApiResult<TResponse>.Failure("Robot returned an empty response.")
                : ApiResult<TResponse>.Success(data);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<TResponse>.Failure("Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<TResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ApiResult<TResponse>.Failure($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// <see cref="DashboardPostAsync"/>'s ok-checking, over the uncapped client. Same
    /// reasoning as <see cref="UncappedPostAsync"/>: the dashboard replies 200 with
    /// <c>{"ok": false, "error": …}</c>, so the body has to be read either way.
    /// </summary>
    private async Task<ApiResult<bool>> UncappedDashboardPostAsync<TRequest>(
        string fullUrl, TRequest body, int timeoutMs, CancellationToken ct)
    {
        var res = await UncappedPostAsync<TRequest, DashboardApiResponse>(
            fullUrl, body, timeoutMs, ct);

        if (!res.IsSuccess)   return ApiResult<bool>.Failure(res.ErrorMessage!);
        if (res.Data is null) return ApiResult<bool>.Failure("Robot returned an empty response.");

        return res.Data.Ok
            ? ApiResult<bool>.Success(true)
            : ApiResult<bool>.Failure(res.Data.Error ?? res.Data.Msg ?? "Robot rejected the request.");
    }

    private async Task<ApiResult<T>> DirectGetAsync<T>(string fullUrl, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetAsync(fullUrl, ct);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<T>(_json, ct);
            return ApiResult<T>.Success(data!);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<T>.Failure("Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<T>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Failure($"Unexpected error: {ex.Message}");
        }
    }

    private async Task<ApiResult<TResponse>> DirectPostAsync<TRequest, TResponse>(string fullUrl, TRequest body, CancellationToken ct)
    {
        try
        {
            // StringContent, not PostAsJsonAsync: JsonContent cannot compute its own length, so
            // HttpClient falls back to Transfer-Encoding: chunked. The dashboard on :8080 is a
            // stdlib BaseHTTPRequestHandler, which reads Content-Length and nothing else — it
            // sees an empty body and every POST comes back 200 with
            // {"ok": false, "error": "Expecting value: line 1 column 1 (char 0)"}.
            using var content = new StringContent(
                JsonSerializer.Serialize(body, _json), System.Text.Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(fullUrl, content, ct);
            response.EnsureSuccessStatusCode();
            if (typeof(TResponse) == typeof(bool))
            {
                return ApiResult<TResponse>.Success((TResponse)(object)true);
            }
            var data = await response.Content.ReadFromJsonAsync<TResponse>(_json, ct);
            return ApiResult<TResponse>.Success(data!);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<TResponse>.Failure("Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<TResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ApiResult<TResponse>.Failure($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Read-only pass-through that reports how far through the upload we are.
    ///
    /// This is the only way to get a real progress bar out of HttpClient: the send side has no
    /// callback, so the measurement has to come from the stream being drained. The alternative
    /// — a fake animated bar — would be actively misleading on the exact failure this screen
    /// exists to survive, a transfer that stalls at 60% on a bad link.
    ///
    /// Deliberately not seekable, so StreamContent takes the single-pass path and never tries
    /// to rewind. It also does not dispose the stream it wraps; the caller's `await using`
    /// owns that.
    /// </summary>
    private sealed class ProgressStream(Stream inner, long total, IProgress<double>? progress)
        : Stream
    {
        private long _read;
        private double _lastReported = -1;

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;

        public override long Length            => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException();
                                       set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) =>
            Advance(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken ct = default) =>
            Advance(await inner.ReadAsync(buffer, ct));

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken ct) =>
            Advance(await inner.ReadAsync(buffer.AsMemory(offset, count), ct));

        private int Advance(int count)
        {
            if (count <= 0 || progress is null || total <= 0) return count;

            _read += count;
            var fraction = Math.Min(1.0, (double)_read / total);

            // Every 64 KB chunk of a 7.5 MB file is ~0.8%, so this barely throttles today —
            // it is here so a smaller model or a larger buffer cannot turn into a flood of
            // dispatcher marshalling for sub-pixel movements.
            if (fraction - _lastReported < 0.005 && fraction < 1.0) return count;

            _lastReported = fraction;
            progress.Report(fraction);
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// Wraps an API call result — either a typed value or an error message.
/// </summary>
public class ApiResult<T>
{
    public T?      Data         { get; private init; }
    public string? ErrorMessage { get; private init; }
    public bool    IsSuccess    => ErrorMessage is null;

    public static ApiResult<T> Success(T data)    => new() { Data = data };
    public static ApiResult<T> Failure(string err) => new() { ErrorMessage = err };
}
