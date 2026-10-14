using System.Windows.Controls;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

public partial class SettingsView : UserControl
{
    private readonly ModelViewModel _model;

    public SettingsView(DashboardViewModel vm, ModelViewModel model)
    {
        InitializeComponent();

        // Shares the dashboard view-model: robot identity and network state are already
        // there, kept current by the same poll loop, and Forget has to be able to stop it.
        DataContext = vm;

        // The AI model card is the one part of this screen that is not robot identity, so it
        // gets its own view-model rather than swelling DashboardViewModel with upload state.
        // Assigned here because the container builds it — XAML cannot ask DI for an instance.
        _model = model;
        ModelCard.DataContext = model;

        // Deferred to Loaded rather than done in the constructor: /api/model/info hashes 7.5 MB
        // on the robot, which is not something to spend on a screen the operator may never open.
        // Settings is a singleton, so this fires once per app run in practice.
        Loaded += async (_, _) => await _model.EnsureLoadedAsync();
    }
}
