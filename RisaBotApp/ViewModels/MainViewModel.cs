using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// View-model for the application shell — the window chrome, the sidebar and the
/// splash-to-first-screen handoff. MAUI had no equivalent: AppShell.xaml owned the tab
/// bar declaratively and the flyout header was wired up in code-behind.
///
/// It deliberately holds no robot state of its own. The sidebar footer shows launch
/// status and the identity header shows the robot name, but both come from
/// <see cref="Dashboard"/> so there is exactly one copy of that state in the app.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly NavigationService  _nav;
    private readonly RobotPairingService _pairing;
    private readonly ApiClient           _api;

    /// <summary>
    /// The dashboard view-model, exposed so the sidebar can bind the launch pill and the
    /// START/STOP button. Pinning those in the shell is the main desktop win over the phone
    /// layout, where they were buried inside the Controls tab.
    /// </summary>
    public DashboardViewModel Dashboard { get; }

    public NavigationService Nav => _nav;

    public MainViewModel(
        NavigationService nav,
        RobotPairingService pairing,
        ApiClient api,
        DashboardViewModel dashboard)
    {
        _nav      = nav;
        _pairing  = pairing;
        _api      = api;
        Dashboard = dashboard;

        _nav.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(NavigationService.Current)) return;

            OnPropertyChanged(nameof(ActiveRoute));

            // Leaving the post-pairing screens takes the top bar with it, and with it the
            // toggle that closes the parameters drawer. Forgetting a robot navigates straight
            // to Discovery from inside DashboardViewModel, bypassing the commands below, so
            // this is the only place that catches it.
            if (!_nav.IsShellVisible)
                IsParametersOpen = false;
        };
    }

    /// <summary>Backs the sidebar RadioButton group, which compares against a route name.</summary>
    public Route ActiveRoute => _nav.Current;

    [ObservableProperty] private string _appVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";

    /// <summary>
    /// Whether the sidebar overlay drawer is open. Bound by MainWindow to drive the
    /// slide-in animation and the backdrop. Closed automatically on navigation so the
    /// drawer does not sit over the page the operator just chose.
    /// </summary>
    [ObservableProperty] private bool _isDrawerOpen;

    /// <summary>
    /// Whether the parameters drawer is open on the right. Independent of
    /// <see cref="IsDrawerOpen"/> and, unlike it, not closed on navigation: tuning a gain and
    /// then switching from the camera view to the Controls page to feel the result is a normal
    /// thing to do, and a drawer that shut every time would make it tedious. It also has no
    /// backdrop — see ParametersDrawer.xaml for why that is load-bearing rather than an
    /// oversight.
    /// </summary>
    [ObservableProperty] private bool _isParametersOpen;

    // ── Startup ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Hands off from the splash. Called by MainWindow once loaded, after a short dwell so
    /// the window does not flicker straight past the splash on a fast machine.
    ///
    /// Discovery is always the first screen, even when a robot is remembered. The phone app
    /// skipped straight to the dashboard on a saved pairing, which is the right call when
    /// the device travels with one robot — but on a desk with several on the bench, a
    /// remembered pairing is a default, not a decision. Discovery marks the remembered robot
    /// so it is easy to find again, without picking it, and choosing a different one no
    /// longer means clearing the pairing to get the chooser back.
    /// </summary>
    public async Task StartAsync()
    {
        _nav.GoTo(Route.Splash);

        // 2.4 s, not the 1.2 s this started at: the wordmark's fade-and-rise alone runs
        // 0.9 s, so the shorter dwell swapped the screen out while it was still arriving.
        await Task.Delay(TimeSpan.FromSeconds(2.4));

        // Point the client somewhere sane before the scan runs. A remembered robot's mDNS
        // hostname is preferred over its last IP — the hostname survives DHCP handing out a
        // new lease. With nothing remembered, assume the robot is still serving its own
        // provisioning AP on 192.168.4.1.
        var paired  = _pairing.GetPairedRobot();
        var baseUrl = paired is null
            ? "http://192.168.4.1:8000"
            : _pairing.GetPairedBaseUrl() ?? "http://192.168.4.1:8000";

        _api.UpdateBaseUrl(baseUrl);
        _nav.GoTo(Route.Discovery);
    }

    // ── Navigation commands ─────────────────────────────────────────────────

    [RelayCommand] private void ShowDashboard() { _nav.GoTo(Route.Dashboard); IsDrawerOpen = false; }
    [RelayCommand] private void ShowControls()  { _nav.GoTo(Route.Controls);  IsDrawerOpen = false; }
    [RelayCommand] private void ShowLogs()      { _nav.GoTo(Route.Logs);      IsDrawerOpen = false; }
    [RelayCommand] private void ShowSettings()  { _nav.GoTo(Route.Settings);  IsDrawerOpen = false; }

    /// <summary>
    /// The CSV data logger. Its own route rather than a card on the dashboard: a recording is
    /// a session with a start, a label and files to collect afterwards, none of which belongs
    /// next to the live camera feed.
    /// </summary>
    [RelayCommand] private void ShowDataLogger() { _nav.GoTo(Route.DataLogger); IsDrawerOpen = false; }

    /// <summary>
    /// The SLAM map. Its own route for the same reason: mapping is a mode the robot is put
    /// into and driven around in, watched continuously — it wants the window, not a card,
    /// and it replaces having RViz open on a second machine.
    /// </summary>
    [RelayCommand] private void ShowSlam() { _nav.GoTo(Route.Slam); IsDrawerOpen = false; }

    [RelayCommand] private void ToggleDrawer()  => IsDrawerOpen = !IsDrawerOpen;

    [RelayCommand] private void ToggleParameters() => IsParametersOpen = !IsParametersOpen;
}
