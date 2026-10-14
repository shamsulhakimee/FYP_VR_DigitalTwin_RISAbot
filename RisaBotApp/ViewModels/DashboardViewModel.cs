using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Models;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// ViewModel for DashboardPage.
/// Integrates with port 8080 ROS 2 web dashboard node (dashboard.py), polling live telemetry
/// every 500ms and streaming live camera feeds.
/// </summary>
public partial class DashboardViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ApiClient           _api;
    private readonly RobotPairingService _pairing;
    private readonly DialogService       _dialogs;
    private readonly NavigationService   _nav;

    private CancellationTokenSource? _pollCts;
    private const int PollingIntervalMs = 500; // 500ms fast live telemetry loop

    // The scan plot rides its own loop at the web dashboard's 250 ms. It is a separate
    // request from /data and a separate timer so that neither rate has to be a compromise:
    // a plot that lags half a second reads as broken, while a speed readout that lags half
    // a second does not, and doubling the telemetry rate to suit the plot would double the
    // load for the fourteen values that did not need it.
    private CancellationTokenSource? _lidarCts;
    private const int LidarIntervalMs = 250;

    // Network/service status changes slowly, so it rides a slower cadence than telemetry.
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(3);
    private DateTime _lastStatusPoll = DateTime.MinValue;

    // How long to wait for the dashboard node to answer after systemd reports the
    // service active. The perception group alone is 3-5s delayed and the signage
    // detector loads a YOLOv5 model, so this needs generous headroom.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
    private DateTime _readyDeadline = DateTime.MaxValue;

    public DashboardViewModel(
        ApiClient api,
        RobotPairingService pairing,
        DialogService dialogs,
        NavigationService nav)
    {
        _api     = api;
        _pairing = pairing;
        _dialogs = dialogs;
        _nav     = nav;

        LoadPairedRobotInfo();
    }

    // ── Live ROS 2 Telemetry Properties (Port 8080 /data) ─────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchStatusDetail))]
    [NotifyPropertyChangedFor(nameof(IsCameraLive))]
    [NotifyPropertyChangedFor(nameof(IsCameraStreaming))]
    private bool _rosActive;

    [ObservableProperty] private bool   _autoMode;
    [ObservableProperty] private string _stateName            = "STANDBY";
    [ObservableProperty] private int    _lap                  = 1;
    [ObservableProperty] private double _speed;
    [ObservableProperty] private double _distance;
    [ObservableProperty] private double _odomX;
    [ObservableProperty] private double _odomY;
    [ObservableProperty] private double _odomYaw;
    [ObservableProperty] private double _imuRoll;
    [ObservableProperty] private double _imuPitch;
    [ObservableProperty] private double _imuYaw;
    [ObservableProperty] private double _laneError;

    // State-machine card. StateDist stays a string all the way through: the robot formats
    // it to two decimals and never parses it back, so converting here would only add a way
    // to fail on a value nothing computes with.
    [ObservableProperty] private int    _stateTime;
    [ObservableProperty] private string _stateDist  = "0.00";
    [ObservableProperty] private string _stopReason = string.Empty;

    // The velocity auto_driver is actually asking for, as opposed to the odometry-derived
    // Speed above. The two disagree while the robot is accelerating or being overridden by
    // the safety controller, and that disagreement is the interesting part.
    [ObservableProperty] private double _cmdLinX;
    [ObservableProperty] private double _cmdAngZ;

    // Perception badges
    [ObservableProperty] private string _trafficLight         = "NONE";
    [ObservableProperty] private bool?  _boomGate;
    [ObservableProperty] private bool?  _tunnelDetected;
    [ObservableProperty] private bool?  _parkingSignDetected;
    [ObservableProperty] private bool?  _lidarObstacle;
    [ObservableProperty] private bool?  _cameraObstacle;
    [ObservableProperty] private bool?  _fusedObstacle;
    [ObservableProperty] private bool?  _obstructionActive;
    [ObservableProperty] private bool?  _parkingComplete;

    // Health and safety
    [ObservableProperty] private bool?  _healthOk;

    /// <summary>
    /// Names of the streams that have gone quiet, joined for display. Kept as a string
    /// rather than the list itself because it is bound to one line of text; the list would
    /// need a converter to get there and nothing else wants the individual names.
    /// </summary>
    [ObservableProperty] private string _healthStaleSummary = string.Empty;

    [ObservableProperty] private bool   _cmdSafetyEstop;
    [ObservableProperty] private int    _cmdSafetyTimeoutCount;

    // Manual control. CtrlStateName is the joystick's own notion of what it is doing, which
    // is not the same thing as the state machine's — the pad can be cycling through modes
    // while auto_driver ignores it entirely.
    [ObservableProperty] private int    _speedPct      = 25;
    [ObservableProperty] private string _ctrlStateName = "LANE_FOLLOW";

    // Gamepad. Replaced wholesale on each poll rather than mutated: GamepadView reacts to
    // the property changing, and an ObservableCollection edited in place would not raise
    // that. They are also read from a background-fetched object, so handing over a fresh
    // immutable list is the safer of the two.
    [ObservableProperty] private IReadOnlyList<int>    _buttons = [];
    [ObservableProperty] private IReadOnlyList<double> _axes    = [];

    /// <summary>
    /// The most recent scan, or null before the first poll answers. LidarView renders the
    /// empty plot for null, which is what should be on screen while waiting rather than a
    /// blank rectangle.
    /// </summary>
    [ObservableProperty] private LidarData? _lidarScan;

    /// <summary>
    /// Live state of the CSV data logger, lifted straight out of the /data payload.
    ///
    /// This view-model does nothing with it — it is here because the telemetry poll is the
    /// only thing in the app that talks to /data, and <c>DataLoggerViewModel</c> watches this
    /// property rather than opening a second poll of its own for a value already on the wire.
    /// Null means the state is unknown (no /data, or a robot build without a logger), which
    /// the Data Logger screen reports differently from idle.
    /// </summary>
    [ObservableProperty] private LoggerStatus? _loggerStatus;

    // Network / Portal Properties (Port 8000)
    [ObservableProperty] private string  _wlan0State          = "connected";
    [ObservableProperty] private string  _connectedSsid       = "—";
    [ObservableProperty] private bool    _internetOnline;
    [ObservableProperty] private string? _telemetryError;

    // ── Camera Tab Properties & Controls ────────────────────────────────────

    // MjpegView consumes the endpoint directly. The MAUI build wrapped it in a one-line
    // HTML document because Android's WebView would not render multipart/x-mixed-replace
    // as a top-level navigation, only as an <img> source. With a native decoder that whole
    // workaround — and the CameraStreamHtml/CameraStreamBaseUrl pair that fed it — is gone.
    [ObservableProperty] private string _cameraStreamUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDisplayView))]
    private string _activeCamView = "raw";

    /// <summary>
    /// Which of the two pictures the middle column is showing. They are exclusive rather
    /// than stacked: side by side they filled the window top to bottom and left both of
    /// them smaller than either wanted to be — the scan plot had to come down from the
    /// web canvas's 320px to fit at all.
    ///
    /// This is local state, not a robot view. The five camera tabs POST to
    /// /api/set_cam_view, which accepts exactly raw / line_follower / obstacle / signage /
    /// traffic_light and answers 200 to anything else while quietly blanking every debug
    /// overlay. So "lidar" must never reach that endpoint, which is why the LiDAR tab
    /// drives this flag instead of ActiveCamView.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDisplayView))]
    [NotifyPropertyChangedFor(nameof(IsCameraView))]
    [NotifyPropertyChangedFor(nameof(IsCameraStreaming))]
    private bool _isLidarView;

    public bool IsCameraView => !IsLidarView;

    /// <summary>
    /// What the six tab buttons check against — the five camera view names plus "lidar".
    /// One property rather than six bindings of mixed type, so that selecting the LiDAR tab
    /// visibly deselects the camera tab and back again, instead of relying on RadioButton
    /// group behaviour to keep two independent sources looking consistent.
    /// </summary>
    public string ActiveDisplayView => IsLidarView ? "lidar" : ActiveCamView;

    // ── Controls Tab ────────────────────────────────────────────────────────

    [ObservableProperty] private bool    _launchActive;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchStatusDetail))]
    private string _serviceState = "active";
    [ObservableProperty] private bool    _isActionBusy;
    [ObservableProperty] private string? _actionError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchStatusText))]
    [NotifyPropertyChangedFor(nameof(LaunchBadgeText))]
    [NotifyPropertyChangedFor(nameof(LaunchStatusDetail))]
    [NotifyPropertyChangedFor(nameof(LaunchStatusColor))]
    [NotifyPropertyChangedFor(nameof(LaunchButtonText))]
    [NotifyPropertyChangedFor(nameof(LaunchButtonColor))]
    [NotifyPropertyChangedFor(nameof(IsLaunchTransitioning))]
    [NotifyPropertyChangedFor(nameof(IsCameraLive))]
    [NotifyPropertyChangedFor(nameof(IsCameraStreaming))]
    [NotifyCanExecuteChangedFor(nameof(ToggleLaunchCommand))]
    private LaunchState _launchState = LaunchState.Stopped;

    public string LaunchStatusText => LaunchState switch
    {
        LaunchState.Running  => "SYSTEM RUNNING",
        LaunchState.Starting => "STARTING…",
        LaunchState.Stopping => "STOPPING…",
        _                    => "SYSTEM STOPPED",
    };

    /// <summary>Short form for the header pill, which is visible on every tab.</summary>
    public string LaunchBadgeText => LaunchState switch
    {
        LaunchState.Running  => "RUNNING",
        LaunchState.Starting => "STARTING",
        LaunchState.Stopping => "STOPPING",
        _                    => "STOPPED",
    };

    public string LaunchStatusDetail => LaunchState switch
    {
        LaunchState.Starting => "Service up — waiting for ROS nodes to respond",
        LaunchState.Stopping => "Shutting down ROS nodes",
        LaunchState.Running  => RosActive
            ? $"systemd: {ServiceState} · ROS nodes online"
            : $"systemd: {ServiceState} · ROS nodes not responding",
        _                    => $"systemd: {ServiceState} · no ROS nodes running",
    };

    // Colours stay as hex strings and are turned into brushes by StringToBrushConverter at
    // the binding. Handing a WPF Brush out of a view-model would drag System.Windows.Media
    // in here, and a Brush created on the poll thread cannot be applied to the UI anyway
    // unless it is frozen first — a string sidesteps both problems. This also matches
    // DiscoveredRobotDevice.StatusBadgeColor, which was already a string in the MAUI app.
    public string LaunchStatusColor => LaunchState switch
    {
        LaunchState.Running  => "#00C853",
        LaunchState.Starting => "#FF6D00",
        LaunchState.Stopping => "#FF6D00",
        _                    => "#FF5252",
    };

    public string LaunchButtonText => LaunchState switch
    {
        LaunchState.Running  => "■   STOP SYSTEM",
        LaunchState.Starting => "STARTING…",
        LaunchState.Stopping => "STOPPING…",
        _                    => "▶   START SYSTEM",
    };

    public string LaunchButtonColor => LaunchState switch
    {
        LaunchState.Running  => "#FF5252",
        LaunchState.Starting => "#334466",
        LaunchState.Stopping => "#334466",
        _                    => "#00C853",
    };

    public bool IsLaunchTransitioning =>
        LaunchState is LaunchState.Starting or LaunchState.Stopping;

    /// <summary>
    /// go2rtc is its own systemd unit with Restart=always, so :1984 keeps serving after
    /// the launch stops — the WebView holds the last frame and looks live. This gates the
    /// camera overlay on the launch actually running rather than on the stream responding.
    /// </summary>
    public bool IsCameraLive => LaunchState == LaunchState.Running && RosActive;

    /// <summary>
    /// What MjpegView's IsActive binds to. Separate from IsCameraLive because the two ask
    /// different questions: IsCameraLive is about the robot, this is about whether anyone
    /// is looking. Collapsing the control does not by itself end the multipart response, so
    /// without this the app keeps pulling 30 fps off the robot's Wi-Fi to decode frames
    /// into a hidden element while the operator is reading the scan plot.
    /// </summary>
    public bool IsCameraStreaming => IsCameraLive && !IsLidarView;

    [ObservableProperty] private ObservableCollection<LogEntry> _logs = [];
    [ObservableProperty] private string  _logsSummary         = string.Empty;

    // ── Settings Tab ────────────────────────────────────────────────────────

    [ObservableProperty] private string _robotName            = "—";
    [ObservableProperty] private string _robotSerial          = "—";
    [ObservableProperty] private string _robotModel           = "—";
    [ObservableProperty] private string _robotFirmware        = "—";
    [ObservableProperty] private string _robotMdns            = "—";
    [ObservableProperty] private string _pairedAt             = "—";
    // MAUI's AppInfo.VersionString read the platform manifest. WPF has no manifest, so the
    // number comes off the assembly — the same value <Version> in the csproj produces.
    [ObservableProperty] private string _appVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";

    // ── Lifecycle & Polling ──────────────────────────────────────────────────

    public void StartPolling()
    {
        // Idempotent: the shell can route back to the dashboard repeatedly, and a second
        // poll loop would double the request rate against the robot for no benefit.
        if (_pollCts is not null) return;

        CameraStreamUrl = _api.CameraStreamUrl;
        _pollCts = new CancellationTokenSource();
        _ = Task.Run(() => PollLoopAsync(_pollCts.Token));

        _lidarCts = new CancellationTokenSource();
        _ = Task.Run(() => LidarLoopAsync(_lidarCts.Token));
    }

    public async Task StopPollingAsync()
    {
        if (_pollCts is not null)
        {
            await _pollCts.CancelAsync();
            _pollCts.Dispose();
            _pollCts = null;
        }

        if (_lidarCts is not null)
        {
            await _lidarCts.CancelAsync();
            _lidarCts.Dispose();
            _lidarCts = null;
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RefreshTelemetryAsync(ct);
            try { await Task.Delay(PollingIntervalMs, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// The scan loop. Deliberately silent about failures: /lidar_data is the one endpoint
    /// that can be legitimately absent — no YDLidar, or the driver still coming up — and
    /// putting that in TelemetryError would bury a real /data failure under a permanent
    /// banner. A stale plot with its own "No data" status is the honest signal here.
    ///
    /// The loop keeps its cadence but skips the request while the plot is hidden. A scan is
    /// several hundred float pairs, four times a second, over the robot's own Wi-Fi — by
    /// some way the heaviest thing the app asks for — and none of it is looked at while the
    /// camera tab is up. Reading the flag from this thread is safe: a bool is written
    /// atomically, and being one tick late to notice the switch costs 250 ms of stale plot.
    /// </summary>
    private async Task LidarLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!IsLidarView)
            {
                try { await Task.Delay(LidarIntervalMs, ct); }
                catch (TaskCanceledException) { break; }
                continue;
            }

            var res = await _api.GetLidarDataAsync(ct);
            if (ct.IsCancellationRequested) break;

            if (res.IsSuccess)
                await UiThread.RunAsync(() => LidarScan = res.Data);

            try { await Task.Delay(LidarIntervalMs, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// One poll cycle: fetch off the UI thread, apply on it.
    ///
    /// The MAUI version assigned every observable property straight from this background
    /// task and only marshalled the three LaunchState writes. MAUI's binding engine
    /// forgave that; WPF does not — a bound property set from a non-dispatcher thread
    /// throws, and the same is true of the Logs collection. So the HTTP work stays here
    /// and the whole apply block is handed to the dispatcher as a unit.
    ///
    /// Applying as one unit is also correct rather than merely convenient: marshalling
    /// each assignment separately would let the UI render a frame with the new speed but
    /// the old lap counter.
    /// </summary>
    private async Task RefreshTelemetryAsync(CancellationToken ct)
    {
        // 1. Poll Port 8080 ROS 2 live data feed (/data)
        var rosRes = await _api.GetRosDashboardDataAsync(ct);
        if (ct.IsCancellationRequested) return;

        await UiThread.RunAsync(() => ApplyRosData(rosRes.IsSuccess ? rosRes.Data : null));

        // 2. Secondary poll port 8000 for network info (every ~3s). Network and service
        //    state change far more slowly than telemetry, so it rides a slower cadence.
        var now = DateTime.UtcNow;
        if (now - _lastStatusPoll < StatusPollInterval) return;
        _lastStatusPoll = now;

        var statusRes = await _api.GetStatusAsync(ct);
        if (ct.IsCancellationRequested || !statusRes.IsSuccess) return;

        await UiThread.RunAsync(() => ApplyStatus(statusRes.Data!));
    }

    /// <summary>UI thread only.</summary>
    private void ApplyRosData(Models.RosDashboardData? d)
    {
        if (d is not null)
        {
            RosActive           = true;
            AutoMode            = d.AutoMode;
            StateName           = !string.IsNullOrWhiteSpace(d.State) ? d.State : "RUNNING";
            Lap                 = d.Lap;
            Speed               = d.Speed;
            Distance            = d.Distance;
            OdomX               = d.OdomX;
            OdomY               = d.OdomY;
            OdomYaw             = d.OdomYaw;
            ImuRoll             = d.ImuRoll;
            ImuPitch            = d.ImuPitch;
            ImuYaw              = d.ImuYaw;
            LaneError           = d.LaneError;
            StateTime           = d.StateTime;
            StateDist           = d.StateDist;
            StopReason          = d.StopReason;
            CmdLinX             = d.CmdLinX;
            CmdAngZ             = d.CmdAngZ;
            TrafficLight        = !string.IsNullOrWhiteSpace(d.TrafficLight) ? d.TrafficLight.ToUpper() : "NONE";
            BoomGate            = d.BoomGate;
            TunnelDetected      = d.TunnelDetected;
            ParkingSignDetected = d.ParkingSignDetected;
            LidarObstacle       = d.LidarObstacle;
            CameraObstacle      = d.CameraObstacle;
            FusedObstacle       = d.FusedObstacle;
            ObstructionActive   = d.ObstructionActive;
            ParkingComplete     = d.ParkingComplete;

            HealthOk              = d.HealthOk;
            HealthStaleSummary    = d.HealthStale.Count > 0 ? string.Join(", ", d.HealthStale) : "—";
            CmdSafetyEstop        = d.CmdSafetyEstop;
            CmdSafetyTimeoutCount = d.CmdSafetyTimeoutCount;

            SpeedPct      = d.SpeedPct;
            CtrlStateName = d.CtrlStateName;
            Buttons       = d.Buttons;
            Axes          = d.Axes;

            LoggerStatus = d.LoggerStatus;

            TelemetryError      = null;
        }
        else
        {
            RosActive = false;

            // The logger lives inside the dashboard node, so losing /data means its state is
            // unknown rather than idle. Clearing it lets the Data Logger screen say so instead
            // of showing a stale duration that has stopped advancing.
            LoggerStatus = null;
        }

        // Promote Starting -> Running only once the dashboard node actually answers.
        // systemd reporting the unit active says nothing about the node graph being up.
        if (LaunchState != LaunchState.Starting) return;

        if (RosActive)
        {
            LaunchState = LaunchState.Running;
        }
        else if (DateTime.UtcNow > _readyDeadline)
        {
            LaunchState = LaunchState.Running;
            ActionError = "Service is active but the ROS nodes never responded.";
        }
    }

    /// <summary>UI thread only.</summary>
    private void ApplyStatus(Models.StatusResponse s)
    {
        Wlan0State     = s.Wlan0State;
        ConnectedSsid  = s.ConnectedSsid ?? "—";
        InternetOnline = s.InternetOnline;

        // Never let the poll overwrite an in-flight transition — it would make the toggle
        // flip back and forth while the robot is still coming up.
        if (IsLaunchTransitioning) return;

        LaunchActive = s.LaunchActive;
        ServiceState = s.LaunchActive ? "active" : "inactive";
        LaunchState  = s.LaunchActive ? LaunchState.Running : LaunchState.Stopped;
    }

    // ── Camera Commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SelectCamViewAsync(string view)
    {
        if (string.IsNullOrWhiteSpace(view)) return;

        // Picking any camera tab is also how you leave the scan plot. Setting this first
        // means the feed is already un-collapsed by the time the robot switches overlay,
        // so the first frame that arrives is the one the operator asked for.
        IsLidarView   = false;
        ActiveCamView = view;
        await _api.SetCameraViewAsync(view);
    }

    /// <summary>
    /// The sixth tab. No robot call: /api/set_cam_view has no LiDAR view, and the scan
    /// plot is fed by its own /lidar_data poll that runs regardless. Whatever camera
    /// overlay was last selected stays selected on the robot, so coming back lands on the
    /// same view rather than resetting to raw.
    /// </summary>
    [RelayCommand]
    private void ShowLidarView() => IsLidarView = true;

    // ── Control Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ResetOdometryAsync()
    {
        IsActionBusy = true;
        ActionError  = null;
        var res = await _api.ResetOdometryAsync();
        IsActionBusy = false;
        if (!res.IsSuccess) ActionError = res.ErrorMessage;
    }

    /// <summary>
    /// Zero the IMU. This is the "hold it level and press" calibration, not the axis-scale
    /// one — the axis variant needs the robot tipped to a known angle and a target passed
    /// in, which is a procedure rather than a button.
    /// </summary>
    [RelayCommand]
    private async Task CalibrateImuAsync()
    {
        IsActionBusy = true;
        ActionError  = null;
        var res = await _api.CalibrateImuZeroAsync();
        IsActionBusy = false;
        if (!res.IsSuccess) ActionError = res.ErrorMessage;
    }

    [RelayCommand]
    private async Task SendCompetitionCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        IsActionBusy = true;
        ActionError  = null;
        var res = await _api.SendCompetitionCommandAsync(command);
        IsActionBusy = false;
        if (!res.IsSuccess) ActionError = res.ErrorMessage;
    }

    [RelayCommand(CanExecute = nameof(CanToggleLaunch))]
    private async Task ToggleLaunchAsync()
    {
        if (LaunchState == LaunchState.Running)
        {
            bool confirmed = await _dialogs.ConfirmAsync(
                "Stop System",
                "This stops the ROS 2 launch. The robot will stop driving and the camera "
                + "feed will close. Continue?",
                "Stop System", "Cancel");

            if (!confirmed) return;
            await StopLaunchAsync();
        }
        else
        {
            await StartLaunchAsync();
        }
    }

    private bool CanToggleLaunch() => !IsLaunchTransitioning;

    private async Task StartLaunchAsync()
    {
        LaunchState = LaunchState.Starting;
        ActionError = null;

        var result = await _api.StartLaunchAsync();
        if (!result.IsSuccess)
        {
            ActionError = result.ErrorMessage;
            LaunchState = LaunchState.Stopped;
            return;
        }

        LaunchActive = result.Data!.LaunchActive;
        ServiceState = result.Data.ServiceState;

        if (!result.Data.LaunchActive)
        {
            ActionError = string.IsNullOrWhiteSpace(result.Data.Output)
                ? "Service failed to start."
                : result.Data.Output;
            LaunchState = LaunchState.Stopped;
            return;
        }

        // systemd is up, but Type=simple means the node graph is not. Stay in Starting
        // until the dashboard node answers; RefreshTelemetryAsync promotes to Running.
        _readyDeadline = DateTime.UtcNow + ReadyTimeout;
    }

    private async Task StopLaunchAsync()
    {
        LaunchState = LaunchState.Stopping;
        ActionError = null;

        var result = await _api.StopLaunchAsync();
        if (!result.IsSuccess)
        {
            ActionError = result.ErrorMessage;
            LaunchState = LaunchActive ? LaunchState.Running : LaunchState.Stopped;
            return;
        }

        LaunchActive = result.Data!.LaunchActive;
        ServiceState = result.Data.ServiceState;
        LaunchState  = result.Data.LaunchActive ? LaunchState.Running : LaunchState.Stopped;

        if (result.Data.LaunchActive)
            ActionError = "Service did not stop.";
    }

    [RelayCommand]
    private async Task RestartRosAsync()
    {
        IsActionBusy = true;
        ActionError  = null;
        var result = await _api.RestartRosAsync();
        IsActionBusy = false;
        if (!result.IsSuccess) ActionError = result.ErrorMessage;
    }

    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        var result = await _api.GetLogsAsync();
        if (!result.IsSuccess) { ActionError = result.ErrorMessage; return; }

        var data = result.Data!;
        LogsSummary = data.StatusSummary;
        Logs.Clear();

        foreach (var line in data.Logs)
            Logs.Add(new LogEntry { Text = line, IsError = data.Errors.Contains(line) });
    }

    [RelayCommand]
    private async Task ForgetRobotAsync()
    {
        bool confirmed = await _dialogs.ConfirmAsync(
            "Forget Robot",
            "This will remove the pairing and return to Discovery. Are you sure?",
            "Forget", "Cancel");

        if (!confirmed) return;

        await StopPollingAsync();
        _pairing.ClearPairedRobot();
        _api.UpdateBaseUrl("http://192.168.4.1:8000");

        // Reset the identity fields too. This view-model is a singleton, so without this
        // the sidebar would keep showing the forgotten robot's name behind the discovery
        // screen and again on the next pairing until the first successful poll.
        RobotName = RobotSerial = RobotModel = RobotFirmware = RobotMdns = PairedAt = "—";
        LaunchState = LaunchState.Stopped;
        RosActive   = false;

        _nav.GoTo(Route.Discovery);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-read the stored pairing. Called by the shell after provisioning completes —
    /// this view-model is a singleton constructed at startup, so it was built before any
    /// pairing existed and would otherwise keep showing em-dashes for the whole session.
    /// </summary>
    public void ReloadPairedRobotInfo() => LoadPairedRobotInfo();

    private void LoadPairedRobotInfo()
    {
        var robot = _pairing.GetPairedRobot();
        if (robot is null) return;

        RobotName     = robot.Name;
        RobotSerial   = robot.Serial;
        RobotModel    = robot.Model;
        RobotFirmware = robot.FirmwareVersion;
        RobotMdns     = robot.MdnsHostname;
        PairedAt      = robot.PairedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public async ValueTask DisposeAsync() => await StopPollingAsync();
}
