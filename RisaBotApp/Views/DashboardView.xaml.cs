using System.Windows.Controls;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

public partial class DashboardView : UserControl
{
    public DashboardView(DashboardViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // No Loaded hook: polling is started by whoever routes here (MainViewModel on a
        // warm start, Discovery or WifiSetup after pairing) because those callers know
        // whether a robot is actually addressable yet. StartPolling is idempotent anyway.
    }
}
