using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RisaBotApp.Models;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

public partial class LogsView : UserControl
{
    private readonly DashboardViewModel _vm;
    private bool _loaded;

    public LogsView(DashboardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // Logs are pulled on demand rather than polled — the endpoint shells out to
        // journalctl on the robot, which is far too expensive for the 500ms loop. So they
        // load once when the section is first opened, and thereafter only on Refresh.
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await _vm.LoadLogsCommand.ExecuteAsync(null);
        };
    }

    /// <summary>
    /// Errors-only filter. Done with a CollectionView filter rather than a second
    /// collection in the view-model: it is a view concern, and filtering here leaves the
    /// underlying Logs collection intact so toggling back off costs nothing.
    /// </summary>
    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        var view = CollectionViewSource.GetDefaultView(_vm.Logs);
        if (view is null) return;

        view.Filter = ErrorsOnlyToggle.IsChecked == true
            ? o => o is LogEntry entry && entry.IsError
            : null;
    }
}
