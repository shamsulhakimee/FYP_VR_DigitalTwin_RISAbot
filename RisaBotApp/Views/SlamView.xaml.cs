using System.Windows.Controls;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

public partial class SlamView : UserControl
{
    public SlamView(SlamViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Polling is bound to visibility rather than started by the router, unlike the
        // dashboard. The map loop is only worth paying for while it is being looked at, and
        // the view is a singleton — so Loaded fires again on every visit and StartPolling is
        // idempotent for exactly that reason.
        Loaded   += (_, _) => vm.StartPolling();
        Unloaded += async (_, _) => await vm.StopPollingAsync();
    }
}
