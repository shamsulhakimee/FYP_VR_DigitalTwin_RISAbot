using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

public partial class DiscoveryView : UserControl
{
    private readonly DiscoveryViewModel _vm;
    private bool _scanned;

    public DiscoveryView(DiscoveryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // ListBoxItem marks the button-down handled as it selects, so the deselect half of
        // the toggle cannot be wired up in XAML like its preview twin — it needs
        // handledEventsToo, and only AddHandler can ask for that.
        DeviceGrid.AddHandler(
            UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(DeviceGrid_MouseLeftButtonDown),
            handledEventsToo: true);

        // MAUI had OnAppearing. WPF's nearest equivalent is Loaded, but Loaded fires again
        // whenever the control is re-attached to the visual tree, and the shell swaps views
        // in and out of a ContentControl — so the scan is guarded to run once per instance.
        // (The view is registered transient, so a fresh navigation still gets a fresh scan.)
        Loaded += async (_, _) =>
        {
            if (_scanned) return;
            _scanned = true;
            await _vm.RunNetworkScanAsync();
        };
    }

    /// <summary>
    /// Set during the tunnelling pass, read during the bubbling one — the two halves of a
    /// single press. Whether the card was already selected can only be known before the
    /// ListBox acts on the click, but undoing the selection can only be done after.
    /// </summary>
    private bool _pressLandedOnSelection;

    /// <summary>
    /// Clicking the selected card again clears the selection.
    ///
    /// A single-selection ListBox will not do this on its own — once something is picked,
    /// clicking it merely re-picks it — and there is no property that turns it on. The
    /// obvious fix is to deselect in the preview handler and swallow the event, but the
    /// preview tunnels from the ListBox *down* to the card, so swallowing it there robs the
    /// card of the press animation its own template hangs off the same event.
    ///
    /// So the work is split: note the state on the way down, act on the way up, once
    /// ListBoxItem has already selected and marked the event handled (which is why the
    /// bubbling handler has to be registered with handledEventsToo).
    ///
    /// This is code-behind rather than view-model because none of it is state — it is a
    /// fact about how a ListBox treats a mouse press, and the view-model has no business
    /// knowing the grid is a ListBox at all.
    /// </summary>
    private void DeviceGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The second click of a double-click comes through here too, and deselecting on it
        // would leave the double-click-to-connect binding with nothing to connect to.
        _pressLandedOnSelection =
            e.ClickCount == 1 &&
            FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { IsSelected: true };
    }

    private void DeviceGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_pressLandedOnSelection) return;
        _pressLandedOnSelection = false;

        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item)
            item.IsSelected = false;
    }

    /// <summary>
    /// Walks up the visual tree. The click almost never lands on the ListBoxItem itself —
    /// it lands on a TextBlock or the glyph Path inside the card's template.
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
    {
        while (from is not null)
        {
            if (from is T match) return match;
            from = VisualTreeHelper.GetParent(from);
        }
        return null;
    }
}
