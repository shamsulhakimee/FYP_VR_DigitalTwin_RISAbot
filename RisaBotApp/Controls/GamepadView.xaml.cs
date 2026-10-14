using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RisaBotApp.Controls;

/// <summary>
/// Live mirror of the joystick driving the robot.
///
/// The whole reason this has code-behind is the mapping below. The telemetry carries a
/// bare list of button states with no names, and the D-pad is not in that list at all —
/// it arrives as two analogue axes. Turning that into thirteen lit pads is index
/// arithmetic, and it belongs in one place with the indices named.
/// </summary>
public partial class GamepadView : UserControl
{
    // ── Controller mapping ─────────────────────────────────────────────────────
    //
    // Taken from the web dashboard, which notes these as "the user's specific mapping" —
    // they are this competition's controller, not a standard. Indices 2, 5 and 10 are
    // deliberately absent: that controller reports them but nothing is bound to them.
    // A different pad will light the wrong squares, which is what the debug line under
    // the grid is for.

    private const int BtnA     = 0;
    private const int BtnB     = 1;
    private const int BtnX     = 3;
    private const int BtnY     = 4;
    private const int BtnLB    = 6;
    private const int BtnRB    = 7;
    private const int BtnLT    = 8;
    private const int BtnRT    = 9;
    private const int BtnStart = 11;

    /// <summary>Left stick, and the D-pad's horizontal axis.</summary>
    private const int AxisLeftX  = 0;
    private const int AxisLeftY  = 1;
    private const int AxisRightX = 2;
    private const int AxisRightY = 3;
    private const int AxisDpadX  = 6;
    private const int AxisDpadY  = 7;

    /// <summary>
    /// How far a D-pad axis has to travel to count as pressed. It is analogue in the wire
    /// format but digital in the hand, so anything past halfway is a press.
    /// </summary>
    private const double DpadThreshold = 0.5;

    /// <summary>Below this the stick is at rest; the dot stays grey rather than lighting
    /// up for a controller's resting drift.</summary>
    private const double StickDeadzone = 0.05;

    /// <summary>Half the well's width, in pixels — how far the dot can travel from centre
    /// at full deflection.</summary>
    private const double StickTravel = 24;

    /// <summary>How long the dot takes to reach a new stick position, in milliseconds.</summary>
    private const double StickTravelMs = 140;

    // ── Inputs ─────────────────────────────────────────────────────────────────

    public static readonly DependencyProperty ButtonsProperty =
        DependencyProperty.Register(
            nameof(Buttons), typeof(IReadOnlyList<int>), typeof(GamepadView),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>Button states in controller index order. Empty or short lists are fine —
    /// every read is bounds-checked, because the list is empty until a joystick publishes
    /// and there is no guarantee about its length after that.</summary>
    public IReadOnlyList<int>? Buttons
    {
        get => (IReadOnlyList<int>?)GetValue(ButtonsProperty);
        set => SetValue(ButtonsProperty, value);
    }

    public static readonly DependencyProperty AxesProperty =
        DependencyProperty.Register(
            nameof(Axes), typeof(IReadOnlyList<double>), typeof(GamepadView),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>Axis values in controller index order, each roughly -1..1.</summary>
    public IReadOnlyList<double>? Axes
    {
        get => (IReadOnlyList<double>?)GetValue(AxesProperty);
        set => SetValue(AxesProperty, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((GamepadView)d).Refresh();

    public GamepadView()
    {
        InitializeComponent();
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var buttons = Buttons;
        var axes    = Axes;

        SetPad(PadA,     Pressed(buttons, BtnA));
        SetPad(PadB,     Pressed(buttons, BtnB));
        SetPad(PadX,     Pressed(buttons, BtnX));
        SetPad(PadY,     Pressed(buttons, BtnY));
        SetPad(PadLB,    Pressed(buttons, BtnLB));
        SetPad(PadRB,    Pressed(buttons, BtnRB));
        SetPad(PadLT,    Pressed(buttons, BtnLT));
        SetPad(PadRT,    Pressed(buttons, BtnRT));
        SetPad(PadStart, Pressed(buttons, BtnStart));

        // The D-pad's signs are inverted relative to screen directions: positive X is left
        // and positive Y is up on this controller, which is why left reads > and right
        // reads < rather than the other way round.
        SetPad(PadLeft,  Axis(axes, AxisDpadX) >  DpadThreshold);
        SetPad(PadRight, Axis(axes, AxisDpadX) < -DpadThreshold);
        SetPad(PadUp,    Axis(axes, AxisDpadY) >  DpadThreshold);
        SetPad(PadDown,  Axis(axes, AxisDpadY) < -DpadThreshold);

        UpdateStick(StickOffsetL, StickDotL, StickLabelL, "L",
                    Axis(axes, AxisLeftX), Axis(axes, AxisLeftY));
        UpdateStick(StickOffsetR, StickDotR, StickLabelR, "R",
                    Axis(axes, AxisRightX), Axis(axes, AxisRightY));

        UpdateDebugLine(buttons);
    }

    private static void SetPad(System.Windows.Controls.Border pad, bool on) =>
        pad.Tag = on ? "on" : null;

    private static bool Pressed(IReadOnlyList<int>? buttons, int index) =>
        buttons is not null && index < buttons.Count && buttons[index] != 0;

    private static double Axis(IReadOnlyList<double>? axes, int index) =>
        axes is not null && index < axes.Count ? axes[index] : 0.0;

    private void UpdateStick(
        System.Windows.Media.TranslateTransform offset,
        System.Windows.Shapes.Ellipse dot,
        TextBlock label,
        string name,
        double x,
        double y)
    {
        // Both axes are inverted, matching the web version's `50 + (axis * -50)`: on this
        // controller pushing the stick left reports positive X.
        //
        // Eased rather than assigned, and faster than the meters. The dot is standing in for
        // a thumb, so it should look like it is being pushed — but this is a 2 Hz sample of a
        // continuous input, and anything slower than ~140 ms starts to feel like the operator
        // is dragging the stick through treacle rather than watching their own hand.
        Animate.To(offset, System.Windows.Media.TranslateTransform.XProperty, -x * StickTravel, StickTravelMs);
        Animate.To(offset, System.Windows.Media.TranslateTransform.YProperty, -y * StickTravel, StickTravelMs);

        var live = Math.Abs(x) > StickDeadzone || Math.Abs(y) > StickDeadzone;
        Animate.Tint(dot, (System.Windows.Media.Brush)FindResource(live ? "AccentBlue" : "TextMuted"));

        label.Text = $"{name}: {x:0.0}, {y:0.0}";
    }

    private void UpdateDebugLine(IReadOnlyList<int>? buttons)
    {
        if (buttons is null || buttons.Count == 0)
        {
            ButtonDebug.Text = "No controller data";
            ButtonDebug.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");
            return;
        }

        var down = buttons
            .Select((v, i) => (v, i))
            .Where(t => t.v != 0)
            .Select(t => $"btn[{t.i}]")
            .ToList();

        if (down.Count == 0)
        {
            ButtonDebug.Text = "No buttons pressed";
            ButtonDebug.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");
        }
        else
        {
            ButtonDebug.Text = "Active: " + string.Join(", ", down);
            ButtonDebug.Foreground = (System.Windows.Media.Brush)FindResource("AccentGreen");
        }
    }
}
