using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RisaBotApp.Services;
using RisaBotApp.ViewModels;
using RisaBotApp.Views;

namespace RisaBotApp;

/// <summary>
/// The single application window. Replaces AppShell — a slim top bar at the top, the
/// active view filling the rest, and a sidebar drawer that slides in from the left as
/// an overlay.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel     _vm;
    private readonly NavigationService _nav;

    /// <summary>Matches the Width property on the DrawerPanel Border in XAML.</summary>
    private const double DrawerWidth = 280;

    /// <summary>Matches the Width property on the ParametersPanel Border in XAML.</summary>
    private const double ParametersWidth = 420;

    private static readonly Duration SlideIn  = new(TimeSpan.FromMilliseconds(250));
    private static readonly Duration SlideOut = new(TimeSpan.FromMilliseconds(200));
    private static readonly Duration Fade     = new(TimeSpan.FromMilliseconds(200));

    /// <summary>How long a newly-navigated view takes to settle. Short on purpose: this is a
    /// transition between two screens the operator asked for, not an introduction to either,
    /// and anything longer starts to feel like the app is thinking.</summary>
    private static readonly Duration RouteIn = new(TimeSpan.FromMilliseconds(220));

    /// <summary>How far the incoming view rises, in pixels. Enough to read as arriving from
    /// somewhere, small enough that no text is legibly in motion while it does.</summary>
    private const double RouteRise = 10;

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn  = new CubicEase { EasingMode = EasingMode.EaseIn };

    public MainWindow(MainViewModel vm, NavigationService nav, ParametersDrawer parameters)
    {
        InitializeComponent();

        _vm  = vm;
        _nav = nav;
        DataContext = vm;

        // The parameters drawer is a panel, not a route, so it is not resolved through
        // NavigationService — but it still comes from the container so its view-model is
        // injected rather than new'd up by the XAML parser.
        ParametersHost.Content = parameters;

        // React to the drawer open/close flag on the view-model.
        _vm.PropertyChanged += OnVmPropertyChanged;

        // Every route change replays the entrance on the host. Subscribing to the navigation
        // service rather than to ContentControl's content change because the binding and this
        // handler both fire off the same notification, and the animation does not care which
        // of them goes first — it targets the host, not the view inside it.
        _nav.PropertyChanged += OnNavPropertyChanged;

        // Clicking the backdrop closes the drawer.
        DrawerBackdrop.PreviewMouseLeftButtonDown += OnBackdropClick;

        Loaded += async (_, _) => await _vm.StartAsync();
    }

    // ── Route transition ──────────────────────────────────────────────────

    private void OnNavPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationService.Current))
            AnimateRouteIn();
    }

    /// <summary>
    /// Fades and lifts the view host after the route changes.
    ///
    /// Only the incoming half is animated. Cross-fading would mean holding both views alive
    /// and composited at once, and the two heaviest routes own an MJPEG stream and a LiDAR
    /// plot between them — paying for both to render for a fifth of a second, every time
    /// someone checks the logs, buys very little.
    /// </summary>
    private void AnimateRouteIn()
    {
        var fade = new DoubleAnimation(0, 1, RouteIn) { EasingFunction = EaseOut };
        ViewHost.BeginAnimation(OpacityProperty, fade);

        var rise = new DoubleAnimation(RouteRise, 0, RouteIn) { EasingFunction = EaseOut };
        ViewHostTranslate.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    // ── Drawer animation ──────────────────────────────────────────────────

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDrawerOpen))
            AnimateDrawer(_vm.IsDrawerOpen);
        else if (e.PropertyName == nameof(MainViewModel.IsParametersOpen))
            AnimateParameters(_vm.IsParametersOpen);
    }

    /// <summary>
    /// Slides the parameters drawer in from the right. Same easing and durations as the
    /// navigation drawer, but with no backdrop to fade — it is non-modal by design, so the
    /// telemetry behind it stays live and clickable while values are being tuned.
    /// </summary>
    private void AnimateParameters(bool open)
    {
        if (open)
        {
            ParametersPanel.Visibility = Visibility.Visible;

            var slide = new DoubleAnimation(ParametersWidth, 0, SlideIn) { EasingFunction = EaseOut };
            ParametersTranslate.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        else
        {
            var slide = new DoubleAnimation(0, ParametersWidth, SlideOut) { EasingFunction = EaseIn };
            slide.Completed += (_, _) => ParametersPanel.Visibility = Visibility.Collapsed;
            ParametersTranslate.BeginAnimation(TranslateTransform.XProperty, slide);
        }
    }

    private void AnimateDrawer(bool open)
    {
        if (open)
        {
            // Show elements before animating so the first frame is visible.
            DrawerPanel.Visibility    = Visibility.Visible;
            DrawerBackdrop.Visibility = Visibility.Visible;

            // Slide the drawer in.
            var slide = new DoubleAnimation(-DrawerWidth, 0, SlideIn) { EasingFunction = EaseOut };
            DrawerTranslate.BeginAnimation(TranslateTransform.XProperty, slide);

            // Fade the backdrop in.
            var fade = new DoubleAnimation(0, 1, Fade);
            DrawerBackdrop.BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            // Slide the drawer out.
            var slide = new DoubleAnimation(0, -DrawerWidth, SlideOut) { EasingFunction = EaseIn };
            slide.Completed += (_, _) => DrawerPanel.Visibility = Visibility.Collapsed;
            DrawerTranslate.BeginAnimation(TranslateTransform.XProperty, slide);

            // Fade the backdrop out.
            var fade = new DoubleAnimation(1, 0, Fade);
            fade.Completed += (_, _) => DrawerBackdrop.Visibility = Visibility.Collapsed;
            DrawerBackdrop.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        _vm.IsDrawerOpen = false;
        e.Handled = true;
    }

    /// <summary>
    /// Escape closes the drawer. It behaves as a modal overlay — the backdrop swallows every
    /// click behind it — so leaving the keyboard without a way out is a trap.
    ///
    /// The key is only consumed while the drawer is actually open, so Escape still reaches
    /// whatever the active view does with it the rest of the time.
    ///
    /// The parameters drawer is deliberately left out. It is non-modal, so it traps nothing,
    /// and it is full of text boxes mid-edit — closing the whole drawer on the keystroke an
    /// operator pressed to abandon one field would throw away the rest of their work.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.IsDrawerOpen)
        {
            _vm.IsDrawerOpen = false;
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _nav.PropertyChanged -= OnNavPropertyChanged;
        DrawerBackdrop.PreviewMouseLeftButtonDown -= OnBackdropClick;
        base.OnClosed(e);
    }
}
