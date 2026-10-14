using System.Windows;
using System.Windows.Controls;

namespace RisaBotApp.Controls;

/// <summary>
/// Three-lamp traffic-light indicator. Bind <see cref="Lamp"/> to the robot's
/// <c>traffic_light</c> telemetry string; the XAML does the rest.
/// </summary>
public partial class TrafficLightView : UserControl
{
    /// <summary>
    /// Which lamp is lit: "red", "yellow", "green", or anything else for none.
    ///
    /// Normalised to lowercase on the way in so the DataTriggers in the XAML can compare
    /// against literals. They match by string equality with no culture or case handling of
    /// their own, so an upper-case value from a future detector would silently light
    /// nothing at all — a failure that looks exactly like a working detector seeing no
    /// light, which is the worst way for it to break.
    /// </summary>
    public static readonly DependencyProperty LampProperty =
        DependencyProperty.Register(
            nameof(Lamp), typeof(string), typeof(TrafficLightView),
            new PropertyMetadata("unknown", null, CoerceLamp));

    private static object CoerceLamp(DependencyObject d, object baseValue) =>
        (baseValue as string)?.Trim().ToLowerInvariant() ?? "unknown";

    public string Lamp
    {
        get => (string)GetValue(LampProperty);
        set => SetValue(LampProperty, value);
    }

    public TrafficLightView()
    {
        InitializeComponent();
    }
}
