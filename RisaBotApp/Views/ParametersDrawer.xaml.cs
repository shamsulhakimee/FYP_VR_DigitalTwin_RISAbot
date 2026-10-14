using System.Windows.Controls;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

/// <summary>
/// The parameters drawer's view. All behaviour is in <see cref="ParametersViewModel"/> and the
/// templates; this exists only to bind the two together.
///
/// Unlike the routed views it is not resolved through <c>NavigationService</c> — it is a panel
/// inside MainWindow, not a screen — but it still takes its view-model by constructor injection
/// so the DI container owns the lifetime. MainWindow resolves it and hands it to the drawer
/// host.
/// </summary>
public partial class ParametersDrawer : UserControl
{
    public ParametersDrawer(ParametersViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
