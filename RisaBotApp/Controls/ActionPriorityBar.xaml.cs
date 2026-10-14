using System.Windows;
using System.Windows.Controls;

namespace RisaBotApp.Controls;

/// <summary>
/// The hybrid action priority chain. Bind <see cref="State"/> to the robot's
/// <c>state</c> telemetry string; the control marks the matching node active, everything
/// below it overridden, and lights the arrows in between.
/// </summary>
public partial class ActionPriorityBar : UserControl
{
    /// <summary>
    /// Behaviours in ascending priority — the same list, in the same order, as
    /// <c>PRIORITY_ORDER</c> in the web dashboard's script.
    ///
    /// This is a subset of <c>ChallengeState</c>, not all of it. The state machine has
    /// seventeen states; only these six are arbitrated against each other, and the rest
    /// (parking, hill, roundabout, the recovery states) are phases of a behaviour rather
    /// than competitors for control. When one of those is current, nothing here lights up
    /// — which is correct, and matches the web version.
    /// </summary>
    private static readonly string[] PriorityOrder =
    [
        "LANE_FOLLOW",
        "TUNNEL",
        "OBSTRUCTION",
        "BOOM_GATE",
        "TRAFFIC_LIGHT",
        "MANUAL",
    ];

    /// <summary>Node cells in the same order as <see cref="PriorityOrder"/>.</summary>
    private readonly Grid[] _nodes;

    /// <summary>Arrows, where arrow <c>i</c> sits between node <c>i</c> and node <c>i+1</c>.</summary>
    private readonly TextBlock[] _arrows;

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State), typeof(string), typeof(ActionPriorityBar),
            new PropertyMetadata(null, OnStateChanged));

    /// <summary>Current <c>ChallengeState</c> name. Unknown or unlisted values leave every
    /// node dark rather than falling back to one.</summary>
    public string? State
    {
        get => (string?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ActionPriorityBar)d).Refresh();

    public ActionPriorityBar()
    {
        InitializeComponent();

        _nodes  = [NodeLaneFollow, NodeTunnel, NodeObstruction, NodeBoomGate, NodeTrafficLight, NodeManual];
        _arrows = [ArrowLaneTunnel, ArrowTunnelObstruction, ArrowObstructionGate,
                   ArrowGateTrafficLight, ArrowTrafficLightManual];

        Refresh();
    }

    private void Refresh()
    {
        var active = IndexOf(State);

        for (var i = 0; i < _nodes.Length; i++)
        {
            _nodes[i].Tag = i == active ? "active"
                          : i <  active ? "done"
                          : null;
        }

        for (var i = 0; i < _arrows.Length; i++)
            _arrows[i].Tag = i < active ? "passed" : null;
    }

    private static int IndexOf(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return -1;

        var name = state.Trim();
        for (var i = 0; i < PriorityOrder.Length; i++)
        {
            if (string.Equals(PriorityOrder[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
