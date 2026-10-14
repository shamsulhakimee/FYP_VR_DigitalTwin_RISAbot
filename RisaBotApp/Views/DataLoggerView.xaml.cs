using System.Windows.Controls;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

public partial class DataLoggerView : UserControl
{
    private readonly DataLoggerViewModel _vm;

    public DataLoggerView(DataLoggerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // The session list is fetched on first open, not on a timer: it only changes when
        // this screen stops a recording, and it already refreshes itself then. The guard
        // lives in the view-model because this handler fires on every visit — the view is
        // a singleton, so re-entering the tab raises Loaded again on the same instance.
        Loaded += async (_, _) => await _vm.EnsureSessionsLoadedAsync();
    }
}
