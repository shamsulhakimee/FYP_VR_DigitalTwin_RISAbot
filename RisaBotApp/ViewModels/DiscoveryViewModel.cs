using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RisaBotApp.Models;
using RisaBotApp.Services;

namespace RisaBotApp.ViewModels;

/// <summary>
/// ViewModel for DiscoveryPage implementing NoMachine-style choice-based device selection grid.
/// </summary>
public partial class DiscoveryViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly RobotPairingService _pairing;
    private readonly RobotDiscoveryService _discovery;
    private readonly NavigationService _nav;

    // No DashboardViewModel here any more. This screen used to hand off to the dashboard
    // directly for robots it judged already configured; that judgement has moved to the
    // Wi-Fi screen, which asks the robot rather than guessing, and owns the handoff now.
    public DiscoveryViewModel(
        ApiClient api,
        RobotPairingService pairing,
        RobotDiscoveryService discovery,
        NavigationService nav)
    {
        _api       = api;
        _pairing   = pairing;
        _discovery = discovery;
        _nav       = nav;
    }

    // ── Observable properties ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private string _statusHint = "scanning the local network and the hotspot…";

    [ObservableProperty]
    private string _manualHostInput = string.Empty;

    [ObservableProperty]
    private bool _showManualEntry;

    /// <summary>
    /// The card the user has clicked, bound two-way to the grid's SelectedItem. Picking a
    /// card no longer connects — the footer button does that — so this is what stands
    /// between the two halves of the old SelectRobot command.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedCommand))]
    private DiscoveredRobotDevice? _selectedRobot;

    public ObservableCollection<DiscoveredRobotDevice> DiscoveredRobots { get; } = new();

    public bool HasRobots => DiscoveredRobots.Count > 0;
    public bool HasError  => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Shown top-right, matching the version in the sidebar once paired.</summary>
    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";

    // ── Lifecycle & Scanning ────────────────────────────────────────────────

    /// <summary>
    /// Executes parallel network discovery scan to find all online RISA-Bots.
    /// Populates the NoMachine-style choice collection.
    /// </summary>
    public async Task RunNetworkScanAsync()
    {
        if (IsScanning) return;

        IsScanning      = true;
        ErrorMessage    = null;
        StatusHint      = "scanning the local network and the hotspot…";
        SelectedRobot   = null;
        DiscoveredRobots.Clear();
        OnPropertyChanged(nameof(HasRobots));

        try
        {
            var paired = _pairing.GetPairedRobot();
            var devices = await _discovery.ScanAllRobotsAsync(paired);

            IsScanning = false;

            foreach (var dev in devices)
            {
                DiscoveredRobots.Add(dev);
            }
            OnPropertyChanged(nameof(HasRobots));

            if (DiscoveredRobots.Count > 0)
            {
                var noun      = DiscoveredRobots.Count == 1 ? "device" : "devices";
                var remembered = DiscoveredRobots.FirstOrDefault(d => d.IsPaired);

                // Nothing is pre-selected, deliberately. Selection is what lights a card,
                // and a card that is lit before the user has touched anything reads as
                // decoration rather than as state — which drains the one signal this screen
                // has for answering "which one did I pick". The remembered robot is called
                // out in the hint below and wears "known" on its own card; that is enough
                // to point at it without claiming it has been chosen.
                StatusHint = remembered is not null
                    ? $"{DiscoveredRobots.Count} {noun} found · {remembered.DisplayName} is remembered"
                    : $"{DiscoveredRobots.Count} {noun} found · select one to continue";
            }
            else
            {
                StatusHint      = "no devices found · check the robot is powered on and on this network";
                ShowManualEntry = true;
            }
        }
        catch (Exception ex)
        {
            IsScanning      = false;
            ErrorMessage    = $"Discovery error: {ex.Message}";
            ShowManualEntry = true;
        }
    }

    // ── Device Selection ────────────────────────────────────────────────────

    /// <summary>
    /// Commits to the selected card. Bound to the footer button and to double-click on a
    /// card, which is the fast path a desktop user expects from a chooser like this.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnectSelected))]
    private void ConnectSelected()
    {
        if (SelectedRobot is not null)
            ConnectTo(SelectedRobot);
    }

    private bool CanConnectSelected() => SelectedRobot is not null && !IsScanning;

    /// <summary>
    /// Points the API client at the target, remembers it, and routes onward.
    ///
    /// Always onward to Wi-Fi setup, never straight to the dashboard. The old branch asked
    /// <c>device.IsHotspot</c>, which reads like a property of the robot and is not one: it
    /// records which candidate slot in the discovery scan happened to answer first. A laptop
    /// sitting on the robot's own access point still resolves risabot10.local — avahi is up on
    /// that link too — and the remembered-hostname candidate is probed first, so it won the
    /// dedupe with IsHotspot false and the app skipped provisioning on a robot that had no
    /// uplink at all. Which address answered says nothing about whether the robot has internet.
    ///
    /// The Wi-Fi screen asks /api/status instead, which is where the robot reports that for
    /// itself, and continues straight through when the answer is yes.
    ///
    /// Synchronous now that navigation is a local ContentControl swap rather than
    /// MAUI's awaitable Shell.GoToAsync — nothing in here is actually asynchronous.
    /// </summary>
    private void ConnectTo(DiscoveredRobotDevice device)
    {
        if (device is null) return;

        try
        {
            // Set base URL for ApiClient
            _api.UpdateBaseUrl(device.BaseUrl);

            // Remember/Pair this robot. The Wi-Fi screen reads this back rather than being
            // passed the device, so it has to be persisted before navigating.
            var hostOrIp = device.Info.MdnsHostname ?? device.Info.Hostname;
            _pairing.SaveManualPairing(device.Info, hostOrIp);

            _nav.GoTo(Route.WifiSetup);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to connect to {device.DisplayName}: {ex.Message}";
        }
    }

    // ── Commands ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RescanNetworkAsync()
    {
        await RunNetworkScanAsync();
    }

    [RelayCommand]
    private void ToggleManualEntry()
    {
        ShowManualEntry = !ShowManualEntry;
    }

    [RelayCommand]
    private async Task ManualConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualHostInput))
        {
            ErrorMessage = "Enter the robot's hostname, e.g. risabot10.local";
            return;
        }

        IsScanning   = true;
        ErrorMessage = null;
        StatusHint   = $"connecting to {ManualHostInput}…";

        try
        {
            var paired = _pairing.GetPairedRobot();
            var device = await _discovery.TryManualConnectAsync(ManualHostInput, paired);
            IsScanning = false;

            if (device is not null)
            {
                ConnectTo(device);
            }
            else
            {
                ErrorMessage = $"Could not reach a robot at '{ManualHostInput}'. Check the name and that the robot is powered on.";
            }
        }
        catch (Exception ex)
        {
            IsScanning   = false;
            ErrorMessage = $"Connection error: {ex.Message}";
        }
    }
}
