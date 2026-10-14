using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using RisaBotApp.Views;

namespace RisaBotApp.Services;

/// <summary>The screens the shell can display. Replaces MAUI's Shell route strings.</summary>
public enum Route
{
    Splash,
    Discovery,
    WifiSetup,
    Dashboard,
    Controls,
    Logs,
    DataLogger,
    Slam,
    Settings,
}

/// <summary>
/// Replacement for MAUI Shell routing (<c>Shell.Current.GoToAsync("//DashboardPage")</c>).
///
/// The desktop build is a single window whose body is a ContentControl, so navigation is
/// just swapping which UserControl that control holds. Routes stay as an enum rather than
/// the magic strings MAUI used — a typo in "//DashbaordPage" failed silently at runtime,
/// whereas this fails at compile time.
///
/// Views resolve through the DI container. Whether an instance is reused is decided by the
/// container registration, not here: Discovery and WifiSetup are transient so each visit is
/// a clean provisioning attempt, while the four post-pairing screens are singletons sharing
/// one DashboardViewModel and its running telemetry loop.
/// </summary>
public sealed class NavigationService(IServiceProvider services) : INotifyPropertyChanged
{
    private readonly IServiceProvider _services = services;

    private Route _current = Route.Splash;
    private object? _currentView;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The active route.</summary>
    public Route Current
    {
        get => _current;
        private set
        {
            if (_current == value) return;
            _current = value;
            Raise(nameof(Current));
            Raise(nameof(IsShellVisible));
        }
    }

    /// <summary>The view instance bound into the main window body.</summary>
    public object? CurrentView
    {
        get => _currentView;
        private set
        {
            if (ReferenceEquals(_currentView, value)) return;
            _currentView = value;
            Raise(nameof(CurrentView));
        }
    }

    /// <summary>
    /// Whether the sidebar should be drawn. Splash, Discovery and WifiSetup are pre-pairing
    /// screens with nothing to navigate to, so they get the full window.
    /// </summary>
    public bool IsShellVisible =>
        Current is Route.Dashboard or Route.Controls or Route.Logs
                 or Route.DataLogger or Route.Slam or Route.Settings;

    public void GoTo(Route route)
    {
        // Callers reach this from async service work that has already left the dispatcher.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => GoTo(route));
            return;
        }

        CurrentView = Resolve(route);
        Current     = route;
    }

    private ContentControl Resolve(Route route) => route switch
    {
        Route.Splash    => _services.GetRequiredService<SplashView>(),
        Route.Discovery => _services.GetRequiredService<DiscoveryView>(),
        Route.WifiSetup => _services.GetRequiredService<WifiSetupView>(),
        Route.Dashboard => _services.GetRequiredService<DashboardView>(),
        Route.Controls  => _services.GetRequiredService<ControlsView>(),
        Route.Logs      => _services.GetRequiredService<LogsView>(),
        Route.DataLogger => _services.GetRequiredService<DataLoggerView>(),
        Route.Slam      => _services.GetRequiredService<SlamView>(),
        Route.Settings  => _services.GetRequiredService<SettingsView>(),
        _               => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
