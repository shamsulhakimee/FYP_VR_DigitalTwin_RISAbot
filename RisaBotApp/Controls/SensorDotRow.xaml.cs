using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RisaBotApp.Controls;

/// <summary>
/// A labelled sensor reading: a dot for the state and a word for the value.
///
/// The reason this is a control rather than a Grid repeated ten times is
/// <see cref="TrueIsGood"/>. Most of these rows report a hazard, where true is the bad news
/// — <c>lidar_obstacle</c> true means BLOCKED, and the web dashboard's shared helper paints
/// true red for exactly that reason. But two rows invert it: <c>boom_gate</c> true means OPEN
/// and <c>health_ok</c> true means OK, and both are painted green. Leaving that polarity
/// implicit is how a red dot ends up meaning "fine" on one line and "stop" on the next.
/// </summary>
public partial class SensorDotRow : UserControl
{
    // ── Content ────────────────────────────────────────────────────────────────

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(SensorDotRow),
            new PropertyMetadata(string.Empty));

    /// <summary>Row caption, e.g. "Boom Gate".</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status), typeof(bool?), typeof(SensorDotRow),
            new PropertyMetadata(null, OnStateChanged));

    /// <summary>
    /// The reading. Null is a third state, not a synonym for false: the dashboard node seeds
    /// these entries as null and replaces them only once the topic publishes, so null means
    /// "nothing has reported yet" and shows a grey dot and an em dash.
    /// </summary>
    public bool? Status
    {
        get => (bool?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public static readonly DependencyProperty TrueIsGoodProperty =
        DependencyProperty.Register(
            nameof(TrueIsGood), typeof(bool), typeof(SensorDotRow),
            new PropertyMetadata(false, OnStateChanged));

    /// <summary>
    /// Whether true should be painted green. Defaults to false, matching the majority of rows,
    /// which report hazards. Set it true for the rows that report health.
    /// </summary>
    public bool TrueIsGood
    {
        get => (bool)GetValue(TrueIsGoodProperty);
        set => SetValue(TrueIsGoodProperty, value);
    }

    public static readonly DependencyProperty TrueTextProperty =
        DependencyProperty.Register(
            nameof(TrueText), typeof(string), typeof(SensorDotRow),
            new PropertyMetadata("YES", OnStateChanged));

    /// <summary>Word shown when the reading is true, e.g. "BLOCKED".</summary>
    public string TrueText
    {
        get => (string)GetValue(TrueTextProperty);
        set => SetValue(TrueTextProperty, value);
    }

    public static readonly DependencyProperty FalseTextProperty =
        DependencyProperty.Register(
            nameof(FalseText), typeof(string), typeof(SensorDotRow),
            new PropertyMetadata("NO", OnStateChanged));

    /// <summary>Word shown when the reading is false, e.g. "CLEAR".</summary>
    public string FalseText
    {
        get => (string)GetValue(FalseTextProperty);
        set => SetValue(FalseTextProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SensorDotRow)d).Refresh();

    public SensorDotRow()
    {
        InitializeComponent();
        Refresh();
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var status = Status;

        // Animate.Tint rather than an assignment to Dot.Fill: ten of these share three theme
        // brushes between them, so recolouring the brush would recolour the whole card. Tint
        // gives each dot a private copy and cross-fades that. The fade is short (160 ms) —
        // long enough not to startle, short enough that a sensor dropping out still reads as
        // an event rather than a mood change.
        if (status is null)
        {
            Animate.Tint(Dot, Palette("Disabled", Brushes.Gray));
            ValueText.Text = "—";
            return;
        }

        var good = status.Value == TrueIsGood;
        Animate.Tint(Dot, good ? Palette("AccentGreen", Brushes.LimeGreen)
                               : Palette("ErrorText",   Brushes.OrangeRed));
        ValueText.Text = status.Value ? TrueText : FalseText;
    }

    /// <summary>
    /// Theme brush by key. TryFindResource rather than FindResource because Refresh runs from
    /// the constructor, before the control is in a tree — at run time that still resolves
    /// against Application.Resources, but the XAML designer has no Application and would throw.
    /// </summary>
    private Brush Palette(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;
}
