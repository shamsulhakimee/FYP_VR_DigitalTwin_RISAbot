using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RisaBotApp.ViewModels;

namespace RisaBotApp.Views;

public partial class WifiSetupView : UserControl
{
    private readonly WifiSetupViewModel _vm;
    private bool _scanned;

    /// <summary>
    /// Whether the detail panel was on screen the last time the selection changed. Moving
    /// from one network to another must not replay the slide — the panel is already there,
    /// and re-animating it would read as a twitch rather than as an arrival.
    /// </summary>
    private bool _panelWasOpen;

    public WifiSetupView(WifiSetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // The password field is a single control in the detail panel now, so nothing about
        // it is torn down and rebuilt when the pick changes — which means the view-model's
        // selection is the only event that can tell it to start over.
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Same once-per-instance guard as DiscoveryView: Loaded re-fires on every
        // re-attach to the visual tree, and a second scan mid-provisioning would wipe
        // the list the user is watching.
        Loaded += async (_, _) =>
        {
            if (_scanned) return;
            _scanned = true;
            await _vm.ScanCommand.ExecuteAsync(null);
        };
    }

    /// <summary>
    /// PasswordBox deliberately exposes no bindable Password property — a DependencyProperty
    /// would leave the plaintext sitting in the property store and in any binding trace — so
    /// the value crosses to the view-model by hand. This is the whole reason the screen has
    /// code-behind at all.
    /// </summary>
    private void NetworkPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
            _vm.Password = box.Password;
    }

    /// <summary>
    /// One box serves every network, so it outlives the selection that filled it. The
    /// view-model clears its own copy of the password when the pick changes; without this the
    /// box would keep showing the dots typed for the previous network while the value behind
    /// them was already gone — the worst of both, since the user would then delete characters
    /// that no longer exist anywhere but on screen.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WifiSetupViewModel.SelectedNetwork)) return;

        NetworkPassword.Clear();

        // The panel is collapsed while nothing is picked, so it cannot fade in — it simply
        // exists on the next layout pass, taking 330px off the table as it goes. 18px of
        // travel over 140ms is what turns that into the panel sliding in from the edge
        // rather than the list appearing to jump left. Only on the way in; see _panelWasOpen.
        var open = _vm.HasSelection;
        if (open && !_panelWasOpen)
            PanelSlide.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(18, 0, new Duration(TimeSpan.FromMilliseconds(140))));
        _panelWasOpen = open;

        // Only chase focus into the field when there is actually something to type. Focus has
        // to wait for the panel to lay out — the box is not yet arranged at the moment its
        // visibility flips, and Focus() on an unarranged element is silently dropped. Input
        // priority is the first pass after layout.
        if (!_vm.SelectedIsSecured) return;

        Dispatcher.BeginInvoke(new Action(() => NetworkPassword.Focus()),
                               DispatcherPriority.Input);
    }

    /// <summary>
    /// The password field swallows keys the table would otherwise act on, so the three that
    /// still mean something out there are put back by hand.
    ///
    /// Enter joins — the table's own KeyBinding cannot serve, because PasswordBox handles
    /// Enter itself and the key never bubbles up to the ListBox. Up and Down keep moving the
    /// selection, since auto-focusing this field on selection would otherwise trap the
    /// keyboard in it after one arrow press: they do nothing in a single-line box, so
    /// nothing is being taken away by spending them on the list. Escape steps back out to
    /// the table without changing what is picked.
    /// </summary>
    private void NetworkPassword_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // PasswordChanged has already run for every keystroke, so the view-model is
                // current and there is nothing to flush first.
                if (_vm.JoinSelectedCommand.CanExecute(null))
                    _vm.JoinSelectedCommand.Execute(null);
                break;

            case Key.Up:
                MoveSelection(-1);
                break;

            case Key.Down:
                MoveSelection(+1);
                break;

            case Key.Escape:
                FocusSelectedRow();
                break;

            default:
                return; // everything else belongs to the field
        }

        e.Handled = true;
    }

    /// <summary>
    /// Steps the table's selection by one, clamped rather than wrapping — a list that jumps
    /// from the bottom back to the top reads as having lost the user's place.
    /// </summary>
    private void MoveSelection(int delta)
    {
        var count = NetworkTable.Items.Count;
        if (count == 0) return;

        var next = Math.Clamp(NetworkTable.SelectedIndex + delta, 0, count - 1);
        if (next == NetworkTable.SelectedIndex) return;

        NetworkTable.SelectedIndex = next;
        NetworkTable.ScrollIntoView(NetworkTable.SelectedItem);

        // Setting SelectedIndex above has already fired the view-model's PropertyChanged,
        // which queues a focus call for the password field at Input priority when the new
        // network is secured. Focus is put on the row first so that the two land in that
        // order: the queued call runs after this one and wins, and if the new network is open
        // there is no field to take it, leaving the row focused so the next arrow press still
        // moves the selection.
        FocusSelectedRow();
    }

    /// <summary>
    /// Focus lands on the container, not the ListBox: FlatRowListBox sets Focusable=False
    /// on the list itself, so that the tab order steps through rows rather than stopping on
    /// the list as a whole.
    /// </summary>
    private void FocusSelectedRow()
    {
        if (NetworkTable.SelectedIndex < 0) return;

        var row = NetworkTable.ItemContainerGenerator
                              .ContainerFromIndex(NetworkTable.SelectedIndex) as ListBoxItem;
        row?.Focus();
    }
}
