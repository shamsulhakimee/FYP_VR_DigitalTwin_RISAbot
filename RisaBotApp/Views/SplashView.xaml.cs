using System.Windows.Controls;

namespace RisaBotApp.Views;

/// <summary>
/// Shown while the shell decides whether to route to Discovery or the Dashboard.
/// Purely decorative — MainViewModel.StartAsync owns the decision.
/// </summary>
public partial class SplashView : UserControl
{
    public SplashView() => InitializeComponent();
}
