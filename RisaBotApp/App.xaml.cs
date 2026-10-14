using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RisaBotApp.Services;
using RisaBotApp.ViewModels;
using RisaBotApp.Views;

namespace RisaBotApp;

/// <summary>
/// Composition root. Replaces <c>MauiProgram.CreateMauiApp()</c> — same service graph,
/// minus the MAUI-specific builder and the Microcharts registration the app never used.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>
    /// Ambient access to the container, for the few places XAML-constructed objects
    /// need a service and cannot be given one through a constructor.
    /// </summary>
    public static IServiceProvider Services =>
        ((App)Current)._services ?? throw new InvalidOperationException("Container not built yet.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        // Any unhandled exception on the dispatcher would otherwise kill the process with
        // the Windows crash dialog. Operators run this unattended next to the robot, so
        // show something readable and keep the window alive where we can.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Infrastructure ──────────────────────────────────────────────────
        //
        // One long-lived HttpClient for the whole app. The robot is a single host and
        // requests are frequent, so socket reuse matters; the 10 s timeout is short on
        // purpose because every call is on a LAN and a stalled request should surface as
        // "robot unreachable" quickly rather than hanging the UI for the default 100 s.
        services.AddSingleton(_ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        });

        services.AddSingleton<SettingsStore>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<NavigationService>();

        // ── Robot services ──────────────────────────────────────────────────
        services.AddSingleton<ApiClient>();
        services.AddSingleton<RobotConnectionService>();
        services.AddSingleton<RobotPairingService>();
        services.AddSingleton<RobotDiscoveryService>();

        // ── View-models ─────────────────────────────────────────────────────
        //
        // Discovery is transient: each visit is a fresh scan and must not inherit the
        // previous run's results. The dashboard is a singleton because it owns the
        // telemetry poll loop and the camera stream, which survive navigating away to
        // Settings and back.
        //
        // Wi-Fi setup is a singleton despite being a one-shot flow, because its
        // constructor subscribes to RobotConnectionService.WsMessageReceived. A transient
        // instance is dropped without anyone calling Dispose, so the subscription — and
        // therefore the instance — would survive; a second provisioning attempt would then
        // have two live handlers racing to navigate on the same "ready" frame. Its state is
        // reset by ScanAsync on each visit instead.
        services.AddSingleton<DashboardViewModel>();
        services.AddTransient<DiscoveryViewModel>();
        services.AddSingleton<WifiSetupViewModel>();

        // The parameters drawer is a singleton for the same reason the dashboard is: values
        // are read one HTTP round trip at a time, so a fresh instance would throw away every
        // group the operator had already expanded and make them pay for it again.
        services.AddSingleton<ParametersViewModel>();

        // Singleton because it subscribes to DashboardViewModel.PropertyChanged in its
        // constructor and never unsubscribes — a transient one would leave a live handler
        // behind on every visit, and each stale copy would keep answering the telemetry poll.
        // It also caches the session list, which is only invalidated by this screen itself.
        services.AddSingleton<DataLoggerViewModel>();

        // Singleton so a picked file, the last upload's outcome and the "restart still needed"
        // flag survive navigating away from Settings — that flag in particular has to outlive
        // the screen, since the whole point is that the operator has to come back and act on it.
        services.AddSingleton<ModelViewModel>();

        // Singleton so the decoded map and the saved-map list survive a trip to Settings.
        // Its poll loop is started and stopped by SlamView's Loaded/Unloaded rather than
        // running for the app's lifetime — a map fetch is kilobytes and nobody is looking
        // at it from the Controls page.
        services.AddSingleton<SlamViewModel>();

        // ── Views ───────────────────────────────────────────────────────────
        //
        // Lifetimes mirror the view-models they bind to. The four post-pairing screens are
        // singletons: they share one DashboardViewModel, and rebuilding DashboardView on
        // every sidebar click would tear down and restart the MJPEG stream each time.
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ParametersDrawer>();
        services.AddTransient<SplashView>();
        services.AddTransient<DiscoveryView>();
        services.AddTransient<WifiSetupView>();
        services.AddSingleton<DashboardView>();
        services.AddSingleton<ControlsView>();
        services.AddSingleton<LogsView>();
        services.AddSingleton<DataLoggerView>();
        services.AddSingleton<SlamView>();
        services.AddSingleton<SettingsView>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "RISA-Studio — unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Disposes the singletons — the websocket connection, the camera stream reader
        // and the telemetry poll loop all hang off these and would otherwise keep
        // background threads alive after the window closes.
        //
        // DisposeAsync, not Dispose: DashboardViewModel and RobotConnectionService are
        // IAsyncDisposable *only*, and the synchronous Dispose() throws on those rather
        // than skipping them. Task.Run keeps the wait off the dispatcher — the poll loop
        // marshals its continuations back to the UI thread, so blocking that thread on a
        // task that needs it would deadlock the shutdown instead.
        if (_services is not null)
        {
            var provider = _services;
            _services = null;
            Task.Run(async () => await provider.DisposeAsync()).GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}
