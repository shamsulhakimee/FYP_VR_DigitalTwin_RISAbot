using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Models;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// ViewModel for the Map page — the live SLAM occupancy grid, plus Save Map,
/// Pause and Restart Mapping.
///
/// Polling is split in two on purpose. <c>/api/slam/status</c> is a few dozen bytes and
/// rides a 1 s loop; the PNG is kilobytes and is fetched only when the robot's map
/// sequence number moves. On a parked robot at a 1 s <c>map_update_interval</c> that
/// number often does not move at all, so the steady-state cost of leaving this page open
/// is one small GET per second.
///
/// Threading follows <see cref="DashboardViewModel"/>: HTTP happens on a background task,
/// every bound-property write is marshalled through <see cref="UiThread"/>, and the whole
/// apply block goes over as a unit so a frame can never render the new pose against the
/// old map.
/// </summary>
public partial class SlamViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ApiClient     _api;
    private readonly DialogService _dialogs;

    /// <summary>Status cadence. Matches slam_toolbox's 1 Hz map_update_interval — polling
    /// faster only re-reads a number that cannot have changed.</summary>
    private const int StatusIntervalMs = 1_000;

    private CancellationTokenSource? _pollCts;

    /// <summary>Sequence of the map currently decoded into <see cref="MapImage"/>. -1 means
    /// "nothing decoded yet", which is distinct from sequence 0.</summary>
    private int _renderedSeq = -1;

    public SlamViewModel(ApiClient api, DialogService dialogs)
    {
        _api     = api;
        _dialogs = dialogs;
    }

    // ── Map state ─────────────────────────────────────────────────────────────

    [ObservableProperty] private ImageSource? _mapImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private double _resolution = 0.05;

    [ObservableProperty] private double _originX;
    [ObservableProperty] private double _originY;

    [ObservableProperty] private double _robotX;
    [ObservableProperty] private double _robotY;
    [ObservableProperty] private double _robotYaw;
    [ObservableProperty] private bool   _robotValid;

    // ── SLAM liveness ─────────────────────────────────────────────────────────

    /// <summary>True once the robot has confirmed a recent /map. Everything actionable on
    /// the page keys off this, because a stack launched with <c>slam:=false</c> looks
    /// identical to one whose mapper died — and neither can be saved from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(PlaceholderText))]
    [NotifyCanExecuteChangedFor(nameof(SaveMapCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartMappingCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    private bool _slamRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private bool _paused;

    /// <summary>Set while the robot is unreachable, as distinct from reachable-but-not-mapping.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(PlaceholderText))]
    private bool _offline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private int _mapWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private int _mapHeight;

    /// <summary>Blocks the three commands against each other while one is in flight. They
    /// all reach the same slam_toolbox service queue, and overlapping a save with a restart
    /// is a way to write a map that no longer exists.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveMapCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartMappingCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    private bool _busy;

    [ObservableProperty] private string _busyText = string.Empty;

    /// <summary>
    /// Sticky once set: a slam_toolbox build with no reset service is a permanent property
    /// of the robot, not a transient failure, so the button stays disabled for the session
    /// rather than inviting a retry that will fail the same way.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestartTooltip))]
    [NotifyCanExecuteChangedFor(nameof(RestartMappingCommand))]
    private bool _restartUnsupported;

    [ObservableProperty] private string _lastActionMessage = string.Empty;
    [ObservableProperty] private bool   _lastActionIsError;

    // ── Saved maps ────────────────────────────────────────────────────────────

    public ObservableCollection<SavedMap> SavedMaps { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedMaps))]
    private int _savedMapCount;

    [ObservableProperty] private string _mapsDirectory = "~/risabot_maps";

    public bool HasSavedMaps => SavedMapCount > 0;

    // ── Derived text ──────────────────────────────────────────────────────────

    public string StatusText => Offline     ? "Robot unreachable"
                              : !SlamRunning ? "SLAM not running"
                              : Paused       ? "Paused"
                                             : "Mapping";

    public string PlaceholderText => Offline     ? "Robot unreachable"
                                   : !SlamRunning ? "SLAM not running\nLaunch with slam:=true"
                                                  : "Waiting for the first map…";

    public string PauseButtonText => Paused ? "Resume Mapping" : "Pause Mapping";

    public string RestartTooltip => RestartUnsupported
        ? "This robot's slam_toolbox cannot clear its map on request — restart the ROS stack instead."
        : "Discard the current map and start again from the robot's present position. "
        + "Mapping stops for a couple of seconds while slam_toolbox restarts; "
        + "driving and obstacle avoidance are unaffected.";

    /// <summary>Arena extent implied by the grid. Blank rather than "0.0 × 0.0 m" before the
    /// first map, because zero is a measurement and no-map-yet is not.</summary>
    public string SizeText => MapWidth > 0 && MapHeight > 0
        ? $"{MapWidth * Resolution:0.0} × {MapHeight * Resolution:0.0} m  ({MapWidth}×{MapHeight} cells)"
        : string.Empty;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartPolling()
    {
        // Idempotent — the shell routes back here repeatedly and a second loop would just
        // double the request rate.
        if (_pollCts is not null) return;

        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;
        _ = Task.Run(() => PollLoopAsync(ct), ct);
        _ = Task.Run(() => RefreshSavedMapsAsync(ct), ct);
    }

    public async Task StopPollingAsync()
    {
        if (_pollCts is null) return;

        await _pollCts.CancelAsync();
        _pollCts.Dispose();
        _pollCts = null;
    }

    public async ValueTask DisposeAsync() => await StopPollingAsync();

    // ── Polling ───────────────────────────────────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RefreshStatusAsync(ct);

            try { await Task.Delay(StatusIntervalMs, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RefreshStatusAsync(CancellationToken ct)
    {
        var res = await _api.GetSlamStatusAsync(ct);
        if (ct.IsCancellationRequested) return;

        if (!res.IsSuccess || res.Data is null)
        {
            await UiThread.RunAsync(() =>
            {
                Offline     = true;
                SlamRunning = false;
                RobotValid  = false;
            });
            return;
        }

        var s = res.Data;

        // Whether the PNG is worth fetching is decided here, off the UI thread, against the
        // sequence of what is currently on screen. The fetch itself follows the apply so the
        // geometry the map is drawn against is already in place when the bitmap lands.
        var needsMap = s.Running && s.HasMap && s.MapSeq != _renderedSeq;

        await UiThread.RunAsync(() =>
        {
            Offline     = false;
            SlamRunning = s.Running;
            Paused      = s.Paused;
            MapWidth    = s.Width;
            MapHeight   = s.Height;

            // Latches one-way rather than assigning, which looks over-cautious but is not:
            // RestartSupported defaults to true when the field is absent, so a plain
            // assignment would let an older dashboard silently clear the flag the error path
            // had just set — re-enabling a button already proven to fail. One-way means the
            // button only ever comes back on a fresh app run, which is the cheaper mistake.
            if (!s.RestartSupported) RestartUnsupported = true;

            // Guard the divisor: a zero resolution would put the robot marker at infinity.
            if (s.Resolution > 0) Resolution = s.Resolution;
            OriginX = s.OriginX;
            OriginY = s.OriginY;

            RobotValid = s.RobotValid;
            if (s.RobotValid)
            {
                RobotX   = s.RobotX;
                RobotY   = s.RobotY;
                RobotYaw = s.RobotYaw;
            }

            // A map that went away — slam_toolbox stopped, or was restarted — must drop the
            // bitmap too, or the page would keep showing a map the robot no longer has.
            if (!s.Running)
            {
                MapImage     = null;
                _renderedSeq = -1;
            }
        });

        if (needsMap) await RefreshMapImageAsync(ct);
    }

    private async Task RefreshMapImageAsync(CancellationToken ct)
    {
        var res = await _api.GetSlamMapImageAsync(ct);
        if (ct.IsCancellationRequested) return;

        // A 404 is normal — the robot has nothing to render yet. Leave whatever is on screen
        // and let the next tick try again; there is nothing for the operator to act on.
        if (!res.IsSuccess || res.Data is null || res.Data.Png.Length == 0) return;

        var bitmap = DecodePng(res.Data.Png);
        if (bitmap is null) return;

        var seq = res.Data.Seq;

        await UiThread.RunAsync(() =>
        {
            MapImage     = bitmap;
            _renderedSeq = seq;
        });
    }

    /// <summary>
    /// Decode on the background thread and freeze, so the bitmap can cross to the UI thread.
    /// An un-frozen BitmapImage belongs to the thread that created it and throws on use
    /// elsewhere; OnLoad caching is what makes freezing possible, since it forces the whole
    /// stream to be read before the constructor returns.
    /// </summary>
    private static BitmapImage? DecodePng(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // A truncated or half-written PNG is not worth a banner; the next map replaces it.
            return null;
        }
    }

    private async Task RefreshSavedMapsAsync(CancellationToken ct)
    {
        var res = await _api.GetSavedMapsAsync(ct);
        if (ct.IsCancellationRequested || !res.IsSuccess || res.Data is null) return;

        var maps = res.Data.Maps.OrderByDescending(m => m.Mtime).ToList();
        var dir  = res.Data.Dir;

        await UiThread.RunAsync(() =>
        {
            SavedMaps.Clear();
            foreach (var m in maps) SavedMaps.Add(m);
            SavedMapCount = maps.Count;
            if (!string.IsNullOrWhiteSpace(dir)) MapsDirectory = dir;
        });
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private bool CanAct => SlamRunning && !Busy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SaveMapAsync()
    {
        var suggested = $"map_{DateTime.Now:yyyyMMdd_HHmm}";
        var name = await _dialogs.PromptAsync(
            "Save Map",
            "Name for this map. The robot writes a .pgm and a .yaml under " +
            $"{MapsDirectory} and will sanitise anything unusable.",
            placeholder: suggested,
            initialValue: suggested);

        if (name is null) return;                       // cancelled
        if (string.IsNullOrWhiteSpace(name)) name = suggested;

        await RunActionAsync("Saving map…", async ct =>
        {
            var res = await _api.SaveSlamMapAsync(name.Trim(), ct);

            if (!res.IsSuccess)
                return (false, res.ErrorMessage ?? "Could not save the map.");

            // The robot reports the name it actually used, which need not be what was asked
            // for — it sanitises, and falls back to a timestamp.
            var saved = res.Data?.Name ?? name;
            await RefreshSavedMapsAsync(ct);
            return (true, $"Saved as {saved}.pgm");
        });
    }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartMappingAsync()
    {
        // Destructive and adjacent to Save Map, so it asks. Everything mapped since the
        // stack came up is discarded, and there is no undo.
        var ok = await _dialogs.ConfirmAsync(
            "Restart Mapping",
            "Discard the current map and start again from the robot's present position?\n\n" +
            "Anything not already saved is lost. Mapping pauses for a couple of seconds " +
            "while slam_toolbox restarts; the robot keeps driving normally.",
            accept: "Restart", cancel: "Keep mapping");

        if (!ok) return;

        await RunActionAsync("Restarting mapping…", async ct =>
        {
            var res = await _api.RestartSlamAsync(ct);

            if (!res.IsSuccess)
            {
                // "no reset service" is a property of the build, not a transient error, so
                // the button is retired for the session instead of failing identically again.
                var msg = res.ErrorMessage ?? "Could not restart mapping.";
                if (msg.Contains("reset", StringComparison.OrdinalIgnoreCase))
                    await UiThread.RunAsync(() => RestartUnsupported = true);
                return (false, msg);
            }

            // Force the next status tick to re-fetch: the robot may well reuse the sequence
            // number, and a cleared map that still shows the old one is worse than a blank.
            await UiThread.RunAsync(() =>
            {
                MapImage     = null;
                _renderedSeq = -1;
            });

            return (true, "Mapping restarted.");
        });
    }

    private bool CanRestart => SlamRunning && !Busy && !RestartUnsupported;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task TogglePauseAsync()
    {
        var target = !Paused;

        await RunActionAsync(target ? "Pausing…" : "Resuming…", async ct =>
        {
            var res = await _api.SetSlamPausedAsync(target, ct);

            if (!res.IsSuccess)
                return (false, res.ErrorMessage ?? "Could not change the pause state.");

            // Optimistic, but the 1 s status poll corrects it within a tick if the robot
            // disagrees — and slam_toolbox's pause is a toggle, so the robot's own view is
            // the only authority.
            await UiThread.RunAsync(() => Paused = target);
            return (true, target ? "Scan processing paused." : "Scan processing resumed.");
        });
    }

    /// <summary>Manual re-read of ~/risabot_maps. Named <c>Reload…</c> rather than
    /// <c>Refresh…</c> only to keep the generated <c>ReloadSavedMapsCommand</c> from
    /// colliding with the private helper it calls.</summary>
    [RelayCommand]
    private async Task ReloadSavedMapsAsync()
        => await RefreshSavedMapsAsync(_pollCts?.Token ?? CancellationToken.None);

    /// <summary>
    /// Shared shell for the three service-backed buttons: raise Busy, run, report.
    ///
    /// The token is the page's own, so navigating away mid-save cancels the wait but not the
    /// robot's work — which is correct, because the map is written either way.
    /// </summary>
    private async Task RunActionAsync(string busyText, Func<CancellationToken, Task<(bool ok, string message)>> action)
    {
        var ct = _pollCts?.Token ?? CancellationToken.None;

        await UiThread.RunAsync(() =>
        {
            Busy              = true;
            BusyText          = busyText;
            LastActionMessage = string.Empty;
        });

        bool ok; string message;
        try
        {
            (ok, message) = await action(ct);
        }
        catch (OperationCanceledException)
        {
            ok = false; message = "Cancelled.";
        }
        catch (Exception ex)
        {
            ok = false; message = ex.Message;
        }

        await UiThread.RunAsync(() =>
        {
            Busy              = false;
            BusyText          = string.Empty;
            LastActionIsError = !ok;
            LastActionMessage = message;
        });
    }
}
