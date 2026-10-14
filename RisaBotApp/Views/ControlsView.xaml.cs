using System.Windows.Controls;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

public partial class ControlsView : UserControl
{
    public ControlsView(DashboardViewModel vm)
    {
        InitializeComponent();

        // Shares the dashboard view-model rather than owning one. Launch state, action
        // errors and telemetry are one thing conceptually — splitting them across two
        // view-models would mean keeping the launch pill in the sidebar in sync by hand.
        DataContext = vm;
    }
}
