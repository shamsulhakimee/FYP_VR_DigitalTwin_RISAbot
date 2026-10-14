using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace RisaBotApp.Controls;

/// <summary>
/// Attached properties that turn an assignment into a transition.
///
/// The problem this solves is the same one <see cref="MeterBar"/> solves internally, and it
/// comes up wherever telemetry drives a visual directly: a binding writes the new value, the
/// visual snaps to it, and a robot that is accelerating smoothly looks like it is teleporting
/// between 2 Hz samples. Animating the bound property itself is not an option — a held
/// animation sits above bindings in WPF's value precedence, so the first transition would be
/// the last thing that property ever did.
///
/// So each helper here takes the target as a *separate* attached property. Bind to
/// <c>Animate.X</c> instead of <c>X</c>, and the real property is driven by an animation
/// toward whatever the binding last delivered:
///
/// <code>
/// &lt;TranslateTransform controls:Animate.X="{Binding LaneError, Converter=…}" /&gt;
/// </code>
///
/// Durations are per-helper rather than configurable. Position and colour want different
/// timings, and a knob nobody turns is worse than a number with a comment next to it.
/// </summary>
public static class Animate
{
    /// <summary>
    /// Movement. Long enough to read as travel, short enough to settle inside the 500 ms
    /// telemetry poll — the same reasoning as <see cref="MeterBar"/>'s.
    /// </summary>
    private static readonly Duration MoveTime = new(TimeSpan.FromMilliseconds(260));

    /// <summary>
    /// Colour. Faster than movement on purpose: a colour change is nearly always a state
    /// change (a sensor dropping out, a lamp going red), and state should look decisive.
    /// The fade exists to stop the eye being startled, not to slow the news down.
    /// </summary>
    private static readonly Duration TintTime = new(TimeSpan.FromMilliseconds(160));

    private static readonly IEasingFunction Curve = new CubicEase { EasingMode = EasingMode.EaseOut };

    // ── Position ───────────────────────────────────────────────────────────────

    /// <summary>Target X for a <see cref="TranslateTransform"/>; the real X eases toward it.</summary>
    public static readonly DependencyProperty XProperty =
        DependencyProperty.RegisterAttached(
            "X", typeof(double), typeof(Animate),
            new PropertyMetadata(0.0, OnXChanged));

    public static void SetX(DependencyObject element, double value) => element.SetValue(XProperty, value);
    public static double GetX(DependencyObject element) => (double)element.GetValue(XProperty);

    private static void OnXChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TranslateTransform t)
            To(t, TranslateTransform.XProperty, (double)e.NewValue);
    }

    /// <summary>Target Y for a <see cref="TranslateTransform"/>; the real Y eases toward it.</summary>
    public static readonly DependencyProperty YProperty =
        DependencyProperty.RegisterAttached(
            "Y", typeof(double), typeof(Animate),
            new PropertyMetadata(0.0, OnYChanged));

    public static void SetY(DependencyObject element, double value) => element.SetValue(YProperty, value);
    public static double GetY(DependencyObject element) => (double)element.GetValue(YProperty);

    private static void OnYChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TranslateTransform t)
            To(t, TranslateTransform.YProperty, (double)e.NewValue);
    }

    /// <summary>
    /// Eases one double property of an animatable object toward <paramref name="value"/>.
    /// Public so code-behind that already holds a named transform can use the same curve
    /// and duration as everything else rather than inventing its own.
    /// </summary>
    public static void To(Animatable target, DependencyProperty property, double value, double? milliseconds = null)
    {
        // A frozen transform cannot be animated, and a garbage value would leave the clock
        // stuck there permanently. Both fall back to an assignment.
        if (double.IsNaN(value) || double.IsInfinity(value) || target.IsFrozen)
        {
            target.BeginAnimation(property, null);
            if (!target.IsFrozen)
                target.SetValue(property, double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value);
            return;
        }

        var duration = milliseconds is null ? MoveTime : new Duration(TimeSpan.FromMilliseconds(milliseconds.Value));

        // No From, so a value that changes mid-flight redirects from where it currently is
        // rather than restarting from the origin.
        target.BeginAnimation(property, new DoubleAnimation(value, duration)
        {
            EasingFunction = Curve,
            FillBehavior   = FillBehavior.HoldEnd
        });
    }

    // ── Colour ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Target fill for a <see cref="Shape"/>; the shape's own brush cross-fades to it.
    ///
    /// Bind this instead of <c>Fill</c> on the status dots. The dots are bound through
    /// converters to brushes held in the theme dictionary, and those are shared — animating
    /// one directly would recolour every dot using the same resource, which is all of them.
    /// <see cref="Tint"/> sidesteps that by giving the shape a private brush to animate.
    /// </summary>
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.RegisterAttached(
            "Fill", typeof(Brush), typeof(Animate),
            new PropertyMetadata(null, OnFillChanged));

    public static void SetFill(DependencyObject element, Brush? value) => element.SetValue(FillProperty, value);
    public static Brush? GetFill(DependencyObject element) => (Brush?)element.GetValue(FillProperty);

    private static void OnFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Shape shape)
            Tint(shape, e.NewValue as Brush);
    }

    /// <summary>
    /// Cross-fades a shape's fill toward <paramref name="target"/>.
    ///
    /// Only solid colours are faded. A gradient or an image brush is assigned outright —
    /// there is no meaningful interpolation between two arbitrary brush types, and the two
    /// places that use gradients here are static anyway.
    /// </summary>
    public static void Tint(Shape shape, Brush? target)
    {
        if (target is not SolidColorBrush wanted)
        {
            shape.Fill = target;
            return;
        }

        // The brush currently on the shape is very likely a frozen theme resource, and it is
        // certainly shared with every other element bound to the same one. Either way it has
        // to be replaced by a private, unfrozen copy before anything can be animated on it.
        var current = shape.Fill as SolidColorBrush;
        if (current is null || current.IsFrozen)
        {
            // First paint has nothing to fade from, so it starts at the answer.
            current    = new SolidColorBrush(current?.Color ?? wanted.Color);
            shape.Fill = current;
        }

        current.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(wanted.Color, TintTime)
        {
            EasingFunction = Curve,
            FillBehavior   = FillBehavior.HoldEnd
        });
    }
}
