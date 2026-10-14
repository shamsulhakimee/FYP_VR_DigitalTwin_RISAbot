using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RisaBotApp.Controls;

/// <summary>
/// The dashboard's recurring "filled track" primitive: IMU roll/pitch/yaw, lane error, the
/// manual-control speed cap. One control rather than three because the only real difference
/// between them is whether zero sits at the left edge or in the middle.
///
/// Drawn rather than templated. A ProgressBar could be made to do the unsigned case, but
/// not the signed one — a bar that grows leftward from the centre is not a fill fraction,
/// and getting there means fighting the template with a margin converter. Twenty lines of
/// OnRender is less code and behaves the same at every size.
///
/// The fill is eased rather than snapped. Telemetry arrives as a 2 Hz sample of something
/// continuous, and redrawing the bar at its new length the instant a sample lands makes a
/// smoothly accelerating robot look like it is lurching between five speeds. Easing between
/// samples reads as the motion that actually happened.
/// </summary>
public class MeterBar : FrameworkElement
{
    /// <summary>
    /// How long the fill takes to reach a newly-set value.
    ///
    /// Deliberately well under the dashboard's 500 ms telemetry poll: an animation longer
    /// than the gap between samples never finishes before the next one restarts it, so the
    /// bar would sit permanently short of the truth and lag a full sample behind. 280 ms
    /// leaves ~220 ms of settled time at every sample.
    /// </summary>
    private static readonly Duration Travel = new(TimeSpan.FromMilliseconds(280));

    /// <summary>
    /// Fast off the mark, gentle into the stop — the same curve the drawers use. Anything
    /// with an ease-in would add perceived latency to a value that is already 500 ms stale.
    /// </summary>
    private static readonly IEasingFunction Curve = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Current value, in whatever unit <see cref="Maximum"/> is in.</summary>
    /// <remarks>
    /// Not <c>AffectsRender</c>: rendering reads <see cref="DisplayValue"/>, which chases
    /// this one. Setting this starts the animation rather than redrawing.
    /// </remarks>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(0.0, OnValueChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// The length actually drawn — <see cref="Value"/> as it is part-way through easing
    /// toward its target.
    ///
    /// Two properties rather than one because an animation with <c>FillBehavior.HoldEnd</c>
    /// occupies the animation layer of whatever it targets, which sits *above* bindings in
    /// WPF's value precedence. Animating <see cref="ValueProperty"/> directly would work
    /// exactly once and then swallow every later telemetry update in silence — the bar
    /// would freeze at the first sample it ever received.
    ///
    /// Public only because <c>BeginAnimation</c> refuses read-only properties. Nothing
    /// outside this class should bind or write it.
    /// </summary>
    public static readonly DependencyProperty DisplayValueProperty =
        DependencyProperty.Register(
            nameof(DisplayValue), typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double DisplayValue
    {
        get => (double)GetValue(DisplayValueProperty);
        set => SetValue(DisplayValueProperty, value);
    }

    /// <summary>
    /// Set false to have the fill jump straight to each value. Nothing does yet, but a
    /// meter that ever carries a safety reading should: 280 ms of easing is 280 ms of the
    /// bar showing a number the robot is no longer at.
    /// </summary>
    public static readonly DependencyProperty IsAnimatedProperty =
        DependencyProperty.Register(
            nameof(IsAnimated), typeof(bool), typeof(MeterBar),
            new PropertyMetadata(true));

    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (MeterBar)d;
        var to  = (double)e.NewValue;

        // A NaN target would poison the animation clock and leave the bar stuck for the rest
        // of the session, so it snaps to zero instead. Telemetry gaps arrive as nulls that
        // the view-model coalesces, but a divide-by-zero on the robot would not.
        if (double.IsNaN(to) || double.IsInfinity(to))
        {
            bar.BeginAnimation(DisplayValueProperty, null);
            bar.DisplayValue = 0;
            return;
        }

        if (!bar.IsAnimated)
        {
            bar.BeginAnimation(DisplayValueProperty, null);
            bar.DisplayValue = to;
            return;
        }

        // No From: the animation picks up wherever the previous one had got to, so a value
        // that changes again mid-travel redirects smoothly instead of jumping back.
        var animation = new DoubleAnimation(to, Travel)
        {
            EasingFunction = Curve,
            FillBehavior   = FillBehavior.HoldEnd
        };

        bar.BeginAnimation(DisplayValueProperty, animation);
    }

    /// <summary>
    /// Value at which the bar is full. For a signed meter this is the magnitude at either
    /// end, so the track spans -Maximum..+Maximum. Values past it are clamped rather than
    /// allowed to overflow the track — the bar is for reading trend and sign at a glance,
    /// and the exact number is always printed beside it anyway.
    /// </summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Whether zero sits in the middle. Signed meters (lane error, angular velocity, IMU
    /// axes) need it there so that the sign is visible as a direction rather than only as
    /// a minus in the text beside the bar.
    /// </summary>
    public static readonly DependencyProperty IsSignedProperty =
        DependencyProperty.Register(
            nameof(IsSigned), typeof(bool), typeof(MeterBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsSigned
    {
        get => (bool)GetValue(IsSignedProperty);
        set => SetValue(IsSignedProperty, value);
    }

    /// <summary>Fill colour. Defaults to the app accent.</summary>
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(MeterBar),
            new FrameworkPropertyMetadata(
                (Brush)new SolidColorBrush(Color.FromRgb(0x29, 0x79, 0xFF)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Unfilled track colour.</summary>
    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(MeterBar),
            new FrameworkPropertyMetadata(
                (Brush)new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x46)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    private static readonly Pen ZeroPen = CreateZeroPen();

    private static Pen CreateZeroPen()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        var pen = new Pen(brush, 1);
        pen.Freeze();
        return pen;
    }

    static MeterBar()
    {
        // A bare FrameworkElement has no natural height, so in anything but a fixed-size
        // cell it would measure to nothing and never appear.
        HeightProperty.OverrideMetadata(typeof(MeterBar), new FrameworkPropertyMetadata(6.0));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var radius = h / 2;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), radius, radius);

        var max = Math.Abs(Maximum);
        if (max <= 0) return;

        var fraction = Math.Clamp(DisplayValue / max, -1.0, 1.0);

        Rect fill;
        if (IsSigned)
        {
            var mid  = w / 2;
            var span = Math.Abs(fraction) * mid;
            // A zero-width rounded rectangle still paints its two end caps, which shows a
            // dot at dead centre where there should be nothing. Below a pixel there is
            // nothing worth drawing anyway.
            if (span < 1)
            {
                dc.DrawLine(ZeroPen, new Point(mid, 0), new Point(mid, h));
                return;
            }
            fill = fraction >= 0
                ? new Rect(mid, 0, span, h)
                : new Rect(mid - span, 0, span, h);
        }
        else
        {
            var span = Math.Max(fraction, 0) * w;
            if (span < 1) return;
            fill = new Rect(0, 0, span, h);
        }

        dc.DrawRoundedRectangle(Fill, null, fill, radius, radius);

        if (IsSigned)
            dc.DrawLine(ZeroPen, new Point(w / 2, 0), new Point(w / 2, h));
    }
}
