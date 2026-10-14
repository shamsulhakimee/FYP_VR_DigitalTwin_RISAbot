using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Models;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// One row in the network table. Wraps a <see cref="WifiNetwork"/> and adds the handful of
/// things the row template needs that the wire model has no business carrying.
///
/// The signal strength used to be an emoji here — 📶 for four bars and a run of ▋ glyphs
/// below that, which meant the indicator changed typeface partway down the scale and
/// rendered at whatever size the emoji font felt like. It is drawn geometry now, and the
/// booleans below are what drive it: a DataTrigger can only test equality, so "at least
/// three bars" has to arrive as a bool rather than as a number to compare. There is no
/// Bar1, because one bar is the floor of SignalBars — the shortest bar is lit on every
/// row that exists at all.
/// </summary>
public class WifiNetworkItem
{
    public WifiNetwork Network { get; }

    public WifiNetworkItem(WifiNetwork network, bool isCurrent = false)
    {
        Network   = network;
        IsCurrent = isCurrent;
    }

    public string Ssid   => Network.Ssid;
    public bool   IsOpen => Network.IsOpen;

    /// <summary>
    /// The network wlan0 is already associated with, if it turned up in the scan. Worth a
    /// mark of its own because the most common reason to be on this screen is to move the
    /// robot somewhere else, and the row you are moving it *off* is the one piece of context
    /// the list cannot otherwise give you.
    /// </summary>
    public bool IsCurrent { get; }

    /// <summary>Short form for the badge at the right of the row — one token, no prose.</summary>
    public string SecurityBadge =>
        Network.IsOpen ? "open" : Network.Security.ToLowerInvariant();

    /// <summary>The longer form, for the detail panel where there is room to spell it out.</summary>
    public string SecurityLabel =>
        Network.IsOpen ? "open · no password" : Network.Security.ToLowerInvariant();

    public bool Bar2 => Network.SignalBars >= 2;
    public bool Bar3 => Network.SignalBars >= 3;
    public bool Bar4 => Network.SignalBars >= 4;

    /// <summary>Right-hand column of the row, in the same mono face the rest of the app
    /// uses for machine-reported numbers.</summary>
    public string SignalLabel => $"{Network.Signal}%";
}


public partial class WifiSetupViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient              _api;
    private readonly RobotConnectionService _ws;
    private readonly RobotPairingService    _pairing;
    private readonly NavigationService      _nav;
    private readonly DashboardViewModel     _dashboard;

    public WifiSetupViewModel(
        ApiClient api,
        RobotConnectionService ws,
        RobotPairingService pairing,
        NavigationService nav,
        DashboardViewModel dashboard)
    {
        _api       = api;
        _ws        = ws;
        _pairing   = pairing;
        _nav       = nav;
        _dashboard = dashboard;

        _ws.WsMessageReceived    += OnWsMessage;
        _ws.ConnectionStateChanged += OnWsStateChanged;

        // Pre-populate steps in correct protocol order
        ResetSteps();
    }

    // ── Provisioning-run bookkeeping ─────────────────────────────────────────
    //
    // A run used to end only when the robot said "ready" or "failed" over /ws/log. If the
    // laptop fell off the robot's access point mid-run — which happens routinely, because
    // moving the robot to another network is exactly what this screen does — no such frame
    // ever arrived, IsConnecting stayed true, and every way off the screen (join, skip,
    // rescan, clearing the selection, even hit-testing the list) is gated on !IsConnecting.
    // The window locked and only killing the app cleared it. The two guards below are the
    // ways a run can now end without the robot's cooperation.

    /// <summary>
    /// Identifies the current attempt so a watchdog or a socket event belonging to an
    /// abandoned one cannot tear down its successor.
    /// </summary>
    private int _runId;

    /// <summary>
    /// Whether the log stream has reached Connected during this run. Without it the
    /// Disconnected that <see cref="RobotConnectionService.ConnectLogStreamAsync"/> raises
    /// while tearing down the previous socket would abort the run that is opening it.
    /// </summary>
    private bool _logStreamLive;

    private DateTime _lastStepUtc;

    /// <summary>
    /// How long the run may go without a step frame before it is declared stalled. Measured
    /// from the last step rather than from the start, so a slow-but-progressing robot is
    /// never cut off; the backend's own steps (internet probe, ROS node check) are the thing
    /// being timed, and none of them takes anywhere near this long.
    /// </summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    // ── Observable properties ────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<WifiNetworkItem> _networks = [];

    [ObservableProperty]
    private ObservableCollection<ConnectionStep> _steps = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    private bool _isConnecting;

    /// <summary>
    /// Whether the robot already reaches the internet on wlan0. Read from /api/status rather
    /// than inferred from the address the app happened to reach the robot on — that inference
    /// is what used to let a robot with no uplink at all sail past this screen.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionLabel))]
    [NotifyPropertyChangedFor(nameof(ShowContinue))]
    private bool _hasUplink;

    /// <summary>
    /// What wlan0 is associated with right now, as the robot reported it before the scan.
    /// Kept so the row for that network can mark itself; it is not the same thing as
    /// <see cref="SelectedSsid"/>, which is what the user has asked the robot to join.
    /// </summary>
    [ObservableProperty]
    private string? _connectedSsid;

    /// <summary>
    /// What the robot says about its own network, in its own line above the scan hint. Kept
    /// separate from <see cref="StatusHint"/> because the two answer different questions —
    /// where the robot is now, versus what the scan turned up — and folding them together
    /// meant the first was overwritten by the second the moment results landed.
    /// </summary>
    [ObservableProperty]
    private string _uplinkHint = "checking what the robot is connected to…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// The row the user has clicked, bound two-way to the table's SelectedItem. Selecting a
    /// network no longer joins it — the footer button does that — which is what lets the
    /// chosen row open up and ask for a password in place rather than over a modal.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionLabel))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedIsSecured))]
    [NotifyPropertyChangedFor(nameof(SelectedIsOpen))]
    [NotifyPropertyChangedFor(nameof(ShowContinue))]
    private WifiNetworkItem? _selectedNetwork;

    /// <summary>
    /// Password for the selected row. It lives here rather than on the row because there is
    /// only ever one row asking: hanging it off each item would leave the other rows quietly
    /// holding whatever was typed into them before the user changed their mind.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    private string _password = string.Empty;

    /// <summary>
    /// The network being provisioned. Distinct from <see cref="SelectedNetwork"/> on purpose:
    /// selection is what the user is pointing at and can still change, this is what the robot
    /// was actually told to join, and the steps belong to it.
    /// </summary>
    [ObservableProperty]
    private string? _selectedSsid;

    [ObservableProperty]
    private string _statusHint = "asking the robot what it can see…";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // ── What the detail panel binds to ───────────────────────────────────────
    //
    // Three flags rather than one nullable and a pile of converters. The panel has to answer
    // "is anything picked", "does it want a password" and "is it open" independently — the
    // password block and the "nothing to type" line are not each other's inverse once the
    // no-selection case exists, and a converter chain that got that wrong would show both.

    /// <summary>Whether to draw the panel at all, as opposed to its empty state.</summary>
    public bool HasSelection => SelectedNetwork is not null;

    /// <summary>A network is picked and it will ask for a password.</summary>
    public bool SelectedIsSecured => SelectedNetwork is { IsOpen: false };

    /// <summary>A network is picked and it will not.</summary>
    public bool SelectedIsOpen => SelectedNetwork is { IsOpen: true };

    /// <summary>
    /// Whether the footer still needs its own button. Connecting moved into the panel, so the
    /// footer's only remaining job is the case the panel cannot serve: no row picked and the
    /// robot already online, i.e. "continue". With a row picked the button would be a second
    /// Join sitting a long way from the password it belongs to.
    /// </summary>
    public bool ShowContinue => SelectedNetwork is null && HasUplink;

    /// <summary>
    /// The footer button says what pressing it will do, which is not the same thing on every
    /// visit. With a row picked it joins that row. With nothing picked and the robot already
    /// online it moves on — that is the whole reason this screen can be mandatory without
    /// being a toll gate. With nothing picked and no uplink it still reads "join", because
    /// joining is the thing to do and the button is disabled until a row is chosen.
    /// </summary>
    public string PrimaryActionLabel =>
        SelectedNetwork is null && HasUplink ? "continue  →" : "join  →";

    /// <summary>Shown top-right, matching Discovery and the sidebar once paired.</summary>
    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";

    /// <summary>
    /// Clearing the password on every selection change is the whole reason the two are
    /// separate properties — carrying one network's password to the next one would send it
    /// to the wrong access point on the next click of Join.
    /// </summary>
    partial void OnSelectedNetworkChanged(WifiNetworkItem? value)
    {
        Password = string.Empty;

        // Steps belong to the attempt, not to the list. Picking a different row after a
        // failure should clear the previous attempt's ✕ rather than leave it hanging under
        // a network that never failed.
        if (!IsConnecting && SelectedSsid is not null && value?.Ssid != SelectedSsid)
        {
            SelectedSsid = null;
            ErrorMessage = null;
            ResetSteps();
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ScanAsync()
    {
        // This view-model is a singleton (see App.ConfigureServices), so a scan doubles as
        // the per-visit reset: without it a second provisioning attempt would open on the
        // previous attempt's failed step list.
        IsScanning      = true;
        IsConnecting    = false;
        ErrorMessage    = null;
        SelectedNetwork = null;
        SelectedSsid    = null;
        Password        = string.Empty;
        HasUplink       = false;   // re-answered by the preflight below; a stale true from a
                                   // previous robot would have the button reading "continue"
                                   // before anything has been asked of this one
        ConnectedSsid   = null;
        StatusHint      = "asking the robot what it can see…";
        ResetSteps();
        Networks.Clear();

        // Ask before looking. /api/status is a cheap read and it answers the question this
        // screen exists to settle — whether the robot has an uplink — whereas the scan below
        // takes a couple of seconds because the backend sleeps through an nmcli rescan. Doing
        // it first means the "already online, continue" case is offered while the list is
        // still filling rather than after it.
        await CheckUplinkAsync();

        var result = await _api.ScanNetworksAsync();
        IsScanning = false;

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            // The robot is plainly reachable — the preflight above just got an answer out of
            // it — so "is the robot still there" was the wrong thing to ask. A scan is the one
            // request that can fail while everything else works: it needs the radio to leave
            // the channel the hotspot is beaconing on, and the driver will not allow that.
            StatusHint = "the scan did not come back · the robot cannot look for networks while its own hotspot is up";
            return;
        }

        // Ordinal, not the current culture: SSIDs are byte strings off the air, and a
        // culture-aware compare would call two differently-cased names the same network on
        // some machines and not others.
        foreach (var n in result.Data!.Networks)
            Networks.Add(new WifiNetworkItem(
                n,
                isCurrent: !string.IsNullOrEmpty(ConnectedSsid) &&
                           string.Equals(n.Ssid, ConnectedSsid, StringComparison.Ordinal)));

        if (Networks.Count == 0)
        {
            StatusHint = "no networks in range · move the robot closer to the router and scan again";
        }
        else
        {
            var noun = Networks.Count == 1 ? "network" : "networks";
            StatusHint = $"{Networks.Count} {noun} in range · select the one the robot should join";
        }

        // The MAUI build auto-selected an SSID captured by the QR scanner here. That flow
        // depended on the phone camera and has no desktop equivalent, so it is gone along
        // with the qr_ssid / qr_password keys it wrote.
    }

    /// <summary>
    /// Reads the robot's own account of its network. Two fields decide it: connected_ssid is
    /// what wlan0 associated with, internet_online is the robot's own reachability probe
    /// through it. Both are required, because an association that leads nowhere is exactly the
    /// state this screen is here to fix — the laptop is NAT'd out through the robot, so a robot
    /// that joined a captive portal and stopped there takes this machine's internet with it.
    ///
    /// A failed read counts as no uplink. The pessimistic reading costs a screen the user can
    /// press past in one click; the optimistic one drops them on a dashboard that cannot talk
    /// to anything, which is the bug this whole change is about.
    ///
    /// When there is no uplink the hint names which failure it is, because the three have
    /// nothing in common. A channel conflict is the robot's own hotspot sabotaging its uplink
    /// and no amount of rescanning or retyping a password will touch it; a captive portal can
    /// never be cleared by a headless robot at all; only the plain case is worth another try.
    /// Saying "no internet" to all three sends people to re-enter a password that was correct.
    /// </summary>
    private async Task CheckUplinkAsync()
    {
        UplinkHint = "checking what the robot is connected to…";

        var result = await _api.GetStatusAsync();
        var status = result.IsSuccess ? result.Data : null;

        if (status is null)
        {
            HasUplink  = false;
            UplinkHint = "could not read the robot's network state · treating it as offline";
            return;
        }

        var ssid = status.ConnectedSsid;

        // Recorded whether or not the association is any good: the row deserves its mark
        // even — especially — when the network it names is the one that is not working.
        ConnectedSsid = string.IsNullOrWhiteSpace(ssid) ? null : ssid;

        HasUplink = status.InternetOnline && !string.IsNullOrWhiteSpace(ssid);

        if (HasUplink)
        {
            UplinkHint = $"the robot is on “{ssid}” with internet · continue, or move it to another network below";
            return;
        }

        if (string.IsNullOrWhiteSpace(ssid))
        {
            UplinkHint = "the robot has no uplink · it is running on its own hotspot alone";
            return;
        }

        UplinkHint =
            status.ChannelConflict
                ? $"the robot joined “{ssid}” on channel {status.StaChannel}, but its hotspot is stuck on channel {status.ApChannel} — one radio cannot serve both, so nothing gets through"
                : status.CaptivePortal
                    ? $"“{ssid}” wants a sign-in page · the robot has no browser to fill one in, so it will need its MAC whitelisted"
                    : $"the robot joined “{ssid}” but gets no internet through it";
    }

    /// <summary>
    /// Commits to the selected row. Bound to the footer button and to Enter on the table,
    /// which is the fast path a desktop user expects once a password is typed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanJoinSelected))]
    private Task JoinSelectedAsync()
    {
        if (SelectedNetwork is null) return Task.CompletedTask;
        return ConnectToNetworkAsync(SelectedNetwork.Ssid, SelectedNetwork.IsOpen ? string.Empty : Password);
    }

    /// <summary>
    /// An empty password on a secured network is a user error the robot would reject after a
    /// round trip and a fifteen-second timeout, so it is caught here instead. Open networks
    /// have nothing to type and are joinable the moment they are selected.
    /// </summary>
    private bool CanJoinSelected() =>
        SelectedNetwork is not null &&
        !IsScanning &&
        !IsConnecting &&
        (SelectedNetwork.IsOpen || !string.IsNullOrEmpty(Password));

    /// <summary>
    /// Closes the detail panel. Setting the table's SelectedItem to null is what actually
    /// does it, and OnSelectedNetworkChanged then clears the password and any steps left over
    /// from an attempt on that network — so the ✕ is a real "never mind", not just a hide.
    ///
    /// Refused mid-join: the panel is the only place the steps are drawn, and letting it be
    /// dismissed while the robot is still working would leave a provisioning run in flight
    /// with nothing on screen reporting it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClearSelection))]
    private void ClearSelection() => SelectedNetwork = null;

    private bool CanClearSelection() => !IsConnecting;

    /// <summary>
    /// What the footer button runs. It is a separate command from
    /// <see cref="JoinSelectedCommand"/> rather than a smarter version of it, because Join is
    /// also bound to Enter on the table and to Enter in the password field, and neither of
    /// those should ever mean "continue past this screen" — pressing Return in a text box is
    /// not a decision to skip provisioning.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPrimaryAction))]
    private Task PrimaryActionAsync()
    {
        if (SelectedNetwork is not null) return JoinSelectedAsync();

        GoToDashboard();
        return Task.CompletedTask;
    }

    private bool CanPrimaryAction() =>
        SelectedNetwork is not null
            ? CanJoinSelected()
            : HasUplink && !IsScanning && !IsConnecting;

    /// <summary>
    /// Leaves without an uplink. Nothing about the robot's local behaviour depends on the
    /// internet — control, camera and telemetry all run over the robot's own access point —
    /// so a venue with dead Wi-Fi should not be able to lock the user out of driving. The
    /// footer says as much next to the link, before it is pressed.
    ///
    /// Not offered when the robot is already online: the primary button reads "continue"
    /// there, and two ways past the same screen doing the same thing is one too many.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSkip))]
    private void Skip() => GoToDashboard();

    private bool CanSkip() => !IsConnecting;

    /// <summary>
    /// The one way onward, shared by all three routes out of this screen: a successful join,
    /// continuing on an uplink the robot already had, and skipping without one. The dashboard
    /// view-model is a singleton built at startup, before any of this pairing existed, so it
    /// has to be told to re-read it — and polling starts after the route so the first tick
    /// lands on a view that is already up.
    /// </summary>
    private void GoToDashboard()
    {
        _dashboard.ReloadPairedRobotInfo();
        _nav.GoTo(Route.Dashboard);
        _dashboard.StartPolling();
    }

    /// <summary>
    /// Connect to the given SSID. Does NOT clear the network list on failure,
    /// so the user can retry without rescanning.
    /// </summary>
    private async Task ConnectToNetworkAsync(string ssid, string password)
    {
        var runId = ++_runId;

        SelectedSsid   = ssid;
        IsConnecting   = true;
        ErrorMessage   = null;
        StatusHint     = $"handing “{ssid}” to the robot…";
        _logStreamLive = false;
        _lastStepUtc   = DateTime.UtcNow;
        ResetSteps();

        // 1. POST /api/connect — fire-and-forget on the backend side
        var connectResult = await _api.ConnectAsync(ssid, password);
        if (!connectResult.IsSuccess)
        {
            IsConnecting = false;
            ErrorMessage = connectResult.ErrorMessage;
            StatusHint   = "the robot did not take the request";
            return;
        }

        // 2. Open WebSocket to /ws/log for live step progress
        StatusHint = $"the robot is joining “{ssid}” · this app will follow it over";
        await _ws.ConnectLogStreamAsync(_api.CurrentBaseUrl);
        // Receive loop fires OnWsMessage events — progress tracked there

        _ = WatchForStallAsync(runId);
    }

    /// <summary>
    /// Ends a run the robot has gone quiet on. Belt to the socket event's braces: a dropped
    /// link is usually visible as a socket failure, but not always — and a backend that
    /// wedges partway through its checks holds a perfectly healthy socket open while sending
    /// nothing, which no transport-level signal will ever report.
    ///
    /// Started after the socket is up and keyed to the run, so the first thing it does on a
    /// superseded attempt is return.
    /// </summary>
    private async Task WatchForStallAsync(int runId)
    {
        while (runId == _runId && IsConnecting)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));

            if (runId != _runId || !IsConnecting) return;

            if (DateTime.UtcNow - _lastStepUtc > StallTimeout)
            {
                await AbortRunAsync(
                    $"nothing has arrived from it for {(int)StallTimeout.TotalSeconds} seconds");
                return;
            }
        }
    }

    /// <summary>
    /// Marshalled to the UI thread by <see cref="RobotConnectionService"/> before it is
    /// raised, so it is safe to touch bound state from here.
    /// </summary>
    private async void OnWsStateChanged(object? sender, WsConnectionState state)
    {
        if (state == WsConnectionState.Connected)
        {
            _logStreamLive = true;
            return;
        }

        // A drop only means anything once the stream has actually been up in this run, and
        // only while a run is in flight — the ordinary teardown after "ready" or "failed"
        // reaches here too, and has already set IsConnecting false.
        if (!_logStreamLive || !IsConnecting) return;

        if (state is WsConnectionState.Failed or WsConnectionState.Disconnected)
            await AbortRunAsync("the connection to it dropped");
    }

    /// <summary>
    /// Unsticks the screen. Deliberately does not call the outcome a failure: the most common
    /// way to arrive here is that the robot joined the new network and this laptop, sitting on
    /// the robot's hotspot, lost sight of it in the process. Reporting that as "the join
    /// failed" would send the user to re-enter a password that was correct — the same mistake
    /// <see cref="CheckUplinkAsync"/> takes care to avoid.
    /// </summary>
    private async Task AbortRunAsync(string reason)
    {
        if (!IsConnecting) return;

        _runId++;                 // orphan the watchdog and any late socket event
        IsConnecting   = false;
        _logStreamLive = false;

        foreach (var s in Steps)
            if (s.State == StepState.Active)
                s.State = StepState.Failed;

        var ssid = SelectedSsid ?? "that network";
        ErrorMessage =
            $"Lost contact with the robot while it was joining “{ssid}” — {reason}. " +
            "It may well have joined successfully; this laptop just stopped being able to see it. " +
            "Reconnect to the robot's hotspot and rescan to find out.";
        StatusHint = "contact lost mid-join · the robot may have joined anyway";

        await _ws.DisconnectAsync();
    }

    // The old RetryConnection command is gone. It reset the step list and cleared the error
    // so the user could press Join again — but a failure already leaves IsConnecting false
    // and the password in the box, so Join is live on its own and the extra button only
    // added a step between seeing the failure and re-trying it. Picking a different row
    // clears the failed steps via OnSelectedNetworkChanged.

    // ── WebSocket event handler ────────────────────────────────────────────

    /// <summary>
    /// Arrives on the UI thread — RobotConnectionService marshals the event before raising
    /// it, because this handler mutates Steps (a bound ObservableCollection) and WPF throws
    /// on a cross-thread collection change rather than merely misbehaving.
    /// </summary>
    private async void OnWsMessage(object? sender, WsLogMessage msg)
    {
        // The stream could not be opened at all. This used to fall through the "not a step"
        // filter below and vanish, leaving the run wedged on a socket that was never up.
        if (msg.Type == "error")
        {
            await AbortRunAsync(msg.Message);
            return;
        }

        if (msg.Type != "step") return; // ignore pong / plain log frames

        // Only step frames refresh the stall deadline. Pongs prove the socket is alive, which
        // is not the question — a backend stuck between steps answers them happily.
        _lastStepUtc = DateTime.UtcNow;

        var step = Steps.FirstOrDefault(s => s.Status == msg.Status);
        if (step is not null)
        {
            // Mark previous steps as done
            foreach (var s in Steps)
            {
                if (s == step) break;
                s.State = StepState.Done;
            }

            step.State = msg.Status is "ready" or "failed"
                ? (msg.Status == "ready" ? StepState.Done : StepState.Failed)
                : StepState.Active;
        }

        if (msg.Status == "ready")
        {
            _runId++;
            IsConnecting   = false;
            _logStreamLive = false;
            await _ws.DisconnectAsync();

            // The robot is about to drop its hotspot, so the app must follow it to the
            // address it just acquired. Identity was persisted by DiscoveryViewModel
            // before it navigated here, so read it back from the pairing service.
            var paired   = _pairing.GetPairedRobot();
            var wlan0Ip  = msg.Wlan0Ip ?? string.Empty;

            // Prefer the IP the robot just reported. Avahi also advertises AAAA records
            // once the network hands out IPv6, but the robot's services bind 0.0.0.0, so
            // anything resolving the mDNS name stalls on a dead v6 address first — and
            // the camera stream inherits this host, which makes the feed hang rather than
            // fail. The mDNS name stays the stored identity, and the next discovery scan
            // re-resolves it to IPv4 if this lease changes.
            var host = wlan0Ip;
            if (string.IsNullOrWhiteSpace(host)) host = paired?.MdnsHostname;

            if (!string.IsNullOrWhiteSpace(host))
            {
                var dashboardUrl = msg.DashboardUrl ?? $"http://{host}:8080";
                _pairing.UpdateAfterProvisioning(SelectedSsid ?? string.Empty, wlan0Ip, dashboardUrl);

                // Re-point every call. ApiClient derives :8080 and :1984 from this base,
                // so telemetry and the camera follow automatically.
                _api.UpdateBaseUrl($"http://{host}:8000");
            }

            // Nothing is shown for the successful case — no confirmation card, no dwell.
            // The robot keeps its own AP up while it joins the network, so this laptop's
            // link never drops and the dashboard simply arrives.
            HasUplink = true;
            GoToDashboard();
        }
        else if (msg.Status == "failed")
        {
            _runId++;
            IsConnecting   = false;
            _logStreamLive = false;
            ErrorMessage   = msg.Message;
            StatusHint     = $"“{SelectedSsid}” was not joined";
            await _ws.DisconnectAsync();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ResetSteps()
    {
        Steps = new ObservableCollection<ConnectionStep>(
            ConnectionSteps.Ordered
                .Select(s => new ConnectionStep
                {
                    Status  = s,
                    Label   = ConnectionSteps.ToDisplayLabel(s),
                    State   = StepState.Pending
                })
        );
    }

    public void Dispose()
    {
        _ws.WsMessageReceived      -= OnWsMessage;
        _ws.ConnectionStateChanged -= OnWsStateChanged;
    }
}

/// <summary>One step in the WiFi connection progress list.</summary>
public partial class ConnectionStep : ObservableObject
{
    public string Status { get; init; } = string.Empty;
    public string Label  { get; init; } = string.Empty;

    // The four Is* helpers are what the step template actually binds to, and they are
    // computed rather than stored — so a State change has to announce them explicitly.
    // MAUI's list re-rendered often enough to paper over the missing notifications;
    // in WPF the step rows would simply never update.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    private StepState _state = StepState.Pending;

    public bool IsPending  => State == StepState.Pending;
    public bool IsActive   => State == StepState.Active;
    public bool IsDone     => State == StepState.Done;
    public bool IsFailed   => State == StepState.Failed;
}

public enum StepState { Pending, Active, Done, Failed }
