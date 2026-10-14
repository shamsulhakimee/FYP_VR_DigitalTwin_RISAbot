using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
// Named explicitly: the WPF SDK's implicit usings do not include System.IO, so Path,
// Directory and File all have to be brought in by hand here.
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Models;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// The Data Logger screen — the subsystem CSV recorder, ported from the web dashboard's
/// <c>#loggerStatusBadge</c> card (dashboard_templates.py:1360).
///
/// It is its own route rather than a card on the dashboard for the same reason it is a
/// separate section in the web version: recording is a *session*, not a reading. Starting one
/// means committing to a labelled run and then coming back for the files afterwards, and none
/// of that competes for attention with the camera and the state machine mid-lap.
///
/// Two things about the design are worth stating outright.
///
/// <b>There is no poll loop here.</b> The robot folds <c>logger_status</c> into the /data
/// payload (dashboard.py:780), which <see cref="DashboardViewModel"/> already fetches every
/// 500 ms, so this watches that property instead of opening a second stream of requests for a
/// value that is on the wire either way. <c>/api/logger/status</c> exists and is
/// <see cref="ApiClient.GetLoggerStatusAsync"/>, but nothing needs it.
///
/// <b>Start and Stop are not symmetric.</b> Start only opens files; Stop is what writes
/// session_meta.json and closes them. A session killed by a power cut or a service restart
/// leaves its CSVs on disk with no metadata, which is why <see cref="LoggerSession.Time"/>
/// falls back to the directory name and why a listed session can have fewer than five files.
/// </summary>
public partial class DataLoggerViewModel : ObservableObject
{
    private readonly ApiClient          _api;
    private readonly DialogService      _dialogs;
    private readonly DashboardViewModel _dashboard;

    /// <summary>
    /// The CSVs one session writes, in the order <c>SubsystemDataLogger.start</c> creates them
    /// (dashboard.py:141). Hardcoded because the robot does not advertise the list anywhere —
    /// <c>session_meta.json</c> names the subsystems but is absent from interrupted sessions,
    /// which are exactly the ones worth recovering.
    /// </summary>
    public static readonly string[] SubsystemFiles =
    [
        "lane_follower.csv",
        "auto_driver.csv",
        "signage_detector.csv",
        "imu_telemetry.csv",
        "servo_encoder.csv",
    ];

    /// <summary>Where downloaded sessions land. One folder per session underneath.</summary>
    private static string DownloadRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "RISA data logs");

    public DataLoggerViewModel(ApiClient api, DialogService dialogs, DashboardViewModel dashboard)
    {
        _api       = api;
        _dialogs   = dialogs;
        _dashboard = dashboard;

        // Singleton watching a singleton for the life of the process, so there is nothing to
        // unsubscribe from — both die with the container.
        _dashboard.PropertyChanged += OnDashboardPropertyChanged;
        ApplyStatus(_dashboard.LoggerStatus);
    }

    // ── Live status, mirrored off the telemetry poll ─────────────────────────

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.LoggerStatus))
            ApplyStatus(_dashboard.LoggerStatus);
    }

    private void ApplyStatus(LoggerStatus? status)
    {
        IsStatusKnown = status is not null;

        if (status is null)
        {
            IsLogging         = false;
            ActiveSessionName = "—";
            DurationText      = "0.0 s";
            SampleCount       = 0;
            return;
        }

        IsLogging   = status.IsLogging;
        SampleCount = status.SampleCount;
        LogDir      = status.LogDir;

        // The robot reports the live session name even after it stops, so it is only shown
        // while a run is open — otherwise the card claims an active session that has ended.
        ActiveSessionName = status.IsLogging && !string.IsNullOrWhiteSpace(status.SessionName)
            ? status.SessionName
            : "—";

        DurationText = FormatDuration(status.DurationSec);
    }

    /// <summary>Seconds as the operator thinks of them: tenths under a minute, m:ss over it.</summary>
    private static string FormatDuration(double seconds)
    {
        if (seconds < 60) return $"{seconds:0.0} s";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes:00}m {span.Seconds:00}s"
            : $"{span.Minutes}m {span.Seconds:00}s";
    }

    /// <summary>False when /data is not answering, so the screen can say "unknown" rather
    /// than "idle" — the difference matters when a run is supposed to be in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    [NotifyPropertyChangedFor(nameof(BadgeColor))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isStatusKnown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    [NotifyPropertyChangedFor(nameof(BadgeColor))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isLogging;

    [ObservableProperty] private string _activeSessionName = "—";
    [ObservableProperty] private string _durationText      = "0.0 s";
    [ObservableProperty] private int    _sampleCount;

    /// <summary>The robot-side directory sessions are written to. Display only.</summary>
    [ObservableProperty] private string _logDir = "~/risabotcar_ws/data_logs";

    public string BadgeText  => !IsStatusKnown ? "OFFLINE" : IsLogging ? "RECORDING" : "IDLE";
    public string BadgeColor => !IsStatusKnown ? "#667799" : IsLogging ? "#FF5252" : "#00C853";

    /// <summary>Where <see cref="DownloadSessionCommand"/> writes. Shown so a download is
    /// not a surprise about where files went.</summary>
    public string DownloadRootDisplay => DownloadRoot;

    // ── Session label ───────────────────────────────────────────────────────

    /// <summary>
    /// Prefix for the next session, not its name — the robot builds
    /// <c>session_&lt;label&gt;_&lt;timestamp&gt;</c> and returns what it used. Left as typed
    /// rather than sanitised here: it becomes a directory name on the robot, so the robot is
    /// the right place to be strict about it.
    /// </summary>
    [ObservableProperty] private string _label = string.Empty;

    // ── Action state ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSessionsCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _actionStatus      = string.Empty;
    [ObservableProperty] private string _actionStatusColor = "#8899AA";

    private void Report(string message, string color)
    {
        ActionStatus      = message;
        ActionStatusColor = color;
    }

    // ── Start / stop ────────────────────────────────────────────────────────

    private bool CanStart() => IsStatusKnown && !IsLogging && !IsBusy;
    private bool CanStop()  => IsStatusKnown && IsLogging  && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsBusy = true;
        Report("Opening session…", "#8899AA");

        try
        {
            var result = await _api.StartLoggerAsync(Label.Trim());

            if (!result.IsSuccess || result.Data is null)
            {
                Report(result.ErrorMessage ?? "Could not start the logger.", "#FF5252");
                return;
            }

            if (!result.Data.Ok)
            {
                Report(result.Data.Error ?? result.Data.Msg ?? "Robot refused to start logging.",
                       "#FF5252");
                return;
            }

            // Optimistic, so the badge flips on the click rather than on the next poll up to
            // 500 ms later. The poll then owns it — if the start silently failed, the very
            // next payload corrects this back to IDLE.
            IsLogging         = true;
            ActiveSessionName = result.Data.SessionName ?? "—";
            Report($"Recording into {ActiveSessionName}.", "#00C853");

            // The label has been consumed; leaving it in the box invites a second run named
            // after the first one's purpose.
            Label = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        IsBusy = true;
        Report("Flushing CSVs…", "#8899AA");

        try
        {
            var result = await _api.StopLoggerAsync();

            if (!result.IsSuccess)
            {
                Report(result.ErrorMessage ?? "Could not stop the logger.", "#FF5252");
                return;
            }

            IsLogging = false;
            Report("Session saved.", "#00C853");

            // The session only becomes listable once stop has written its metadata, so this
            // is the moment the list is wrong and refreshing it is free.
            await LoadSessionsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Saved sessions ──────────────────────────────────────────────────────

    public ObservableCollection<LoggerSession> Sessions { get; } = [];

    [ObservableProperty] private string _sessionsMessage = "Not loaded yet.";
    [ObservableProperty] private bool   _isLoadingSessions;

    private bool _hasLoadedOnce;

    /// <summary>
    /// First-visit load, called from the view's Loaded handler. The view is a singleton, so
    /// this fires once per process — revisiting the tab does not re-fetch, since sessions only
    /// change when this screen stops one.
    /// </summary>
    public async Task EnsureSessionsLoadedAsync()
    {
        if (_hasLoadedOnce) return;
        _hasLoadedOnce = true;
        await LoadSessionsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshSessions))]
    private async Task RefreshSessionsAsync() => await LoadSessionsAsync();

    private bool CanRefreshSessions() => !IsBusy && !IsLoadingSessions;

    private async Task LoadSessionsAsync()
    {
        IsLoadingSessions = true;
        RefreshSessionsCommand.NotifyCanExecuteChanged();

        try
        {
            var result = await _api.GetLoggerSessionsAsync();

            if (!result.IsSuccess || result.Data is null)
            {
                SessionsMessage = result.ErrorMessage ?? "Could not list sessions.";
                return;
            }

            Sessions.Clear();
            foreach (var session in result.Data.Sessions)
                Sessions.Add(session);

            SessionsMessage = Sessions.Count == 0
                ? "No sessions recorded yet."
                : string.Empty;
        }
        finally
        {
            IsLoadingSessions = false;
            RefreshSessionsCommand.NotifyCanExecuteChanged();
        }
    }

    // ── Download ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pulls a session's CSVs down to <see cref="DownloadRoot"/> and opens the folder.
    ///
    /// All five subsystems in one go rather than a link each, which is what the web version
    /// offers. On a desktop the useful unit is the session — the files get opened together in
    /// whatever is doing the PID maths — and five separate browser downloads into a shared
    /// Downloads folder loses which run they came from.
    ///
    /// A missing file is not a failure of the whole download; interrupted sessions genuinely
    /// have fewer than five, so the count of what arrived is reported instead.
    /// </summary>
    [RelayCommand]
    private async Task DownloadSessionAsync(LoggerSession? session)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.Name)) return;

        IsBusy = true;
        Report($"Saving {session.Name}…", "#8899AA");

        try
        {
            var folder = Path.Combine(DownloadRoot, session.Name);
            Directory.CreateDirectory(folder);

            var saved  = 0;
            var errors = new List<string>();

            foreach (var file in SubsystemFiles)
            {
                var result = await _api.DownloadLoggerFileAsync(
                    session.Name, file, Path.Combine(folder, file));

                if (result.IsSuccess) saved++;
                else if (result.ErrorMessage is { } message) errors.Add(message);
            }

            if (saved == 0)
            {
                Report($"Nothing saved — {string.Join("; ", errors)}", "#FF5252");
                return;
            }

            Report(
                saved == SubsystemFiles.Length
                    ? $"Saved {saved} CSVs to {folder}."
                    : $"Saved {saved} of {SubsystemFiles.Length} CSVs to {folder}. " +
                      "The rest are missing on the robot — the session was probably interrupted.",
                saved == SubsystemFiles.Length ? "#00C853" : "#FFB300");

            Reveal(folder);
        }
        catch (Exception ex)
        {
            Report($"Could not save: {ex.Message}", "#FF5252");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens Explorer on the downloaded folder. Best-effort: the files are already on disk
    /// and their path is in the status line, so failing to open a window is not worth
    /// reporting as an error over the successful save.
    /// </summary>
    private static void Reveal(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Ignored deliberately — see remarks.
        }
    }

    /// <summary>
    /// Deletes every downloaded copy on this PC. The robot's own <c>data_logs</c> is left
    /// alone: the dashboard offers no delete endpoint, and inventing one by other means is
    /// not this screen's job.
    /// </summary>
    [RelayCommand]
    private async Task ClearDownloadsAsync()
    {
        if (!Directory.Exists(DownloadRoot))
        {
            Report("Nothing downloaded yet.", "#8899AA");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Delete downloaded logs?",
            $"This deletes every CSV downloaded to:\n\n{DownloadRoot}\n\n" +
            "Sessions on the robot are not touched and can be downloaded again.",
            "Delete",
            "Cancel");

        if (!confirmed) return;

        try
        {
            Directory.Delete(DownloadRoot, recursive: true);
            Report("Downloaded copies deleted.", "#00C853");
        }
        catch (Exception ex)
        {
            Report($"Could not delete: {ex.Message}", "#FF5252");
        }
    }
}
