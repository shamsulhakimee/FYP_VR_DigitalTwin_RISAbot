using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RisaBotApp.Converters;

/*
    WPF equivalents of the MAUI converter set.

    Two systematic differences from the MAUI originals:

      • MAUI bound visibility to a bool (IsVisible). WPF binds to the three-state
        Visibility enum, so every "is this shown?" converter returns Visibility
        and collapses (not Hidden) when false — Hidden would still reserve layout
        space, which is never what the old IsVisible=false meant.

      • MAUI's Color.FromArgb(string) parsed hex directly and MAUI let you bind a
        Color to a background. WPF wants a Brush, so the colour converters go all
        the way to a frozen SolidColorBrush. Freezing matters: these run on every
        telemetry tick and an unfrozen brush would allocate a new dependency
        object each time.
*/

/// <summary>Inverts a boolean.</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>True → Visible, False → Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>True → Collapsed, False → Visible.</summary>
public sealed class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}

/// <summary>Non-empty string → Visible, null/blank → Collapsed. Used for error banners.</summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns true if a string is neither null nor empty.</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Picks one of two brushes from a comma-separated ConverterParameter
/// "#true_hex,#false_hex", e.g. ConverterParameter="#00C853,#FF5252".
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split(',') ?? [];
        var hex = value is true
            ? (parts.Length > 0 ? parts[0] : "#FFFFFF")
            : (parts.Length > 1 ? parts[1] : "#888888");
        return BrushCache.Get(hex.Trim());
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Parses a hex colour string ("#00E676") into a brush, for view-models and models
/// that expose their colour as a string. Falls back to transparent rather than throwing.
/// </summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BrushCache.Get(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Tri-state text for the perception badges, which are bool? — the dashboard node reports
/// null when a detector has not published yet, and "no gate detected" and "detector silent"
/// are genuinely different things to a driver watching the run.
///
/// ConverterParameter is "true,false,null", e.g. ConverterParameter="CLOSED,OPEN,—".
/// </summary>
public sealed class NullableBoolTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split(',') ?? [];
        string Part(int i, string fallback) => parts.Length > i ? parts[i].Trim() : fallback;

        return value switch
        {
            true  => Part(0, "YES"),
            false => Part(1, "NO"),
            _     => Part(2, "—"),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Brush counterpart to <see cref="NullableBoolTextConverter"/>. Parameter is
/// "#true,#false,#null"; the null slot defaults to the muted text colour so an
/// unreported detector recedes instead of shouting.
/// </summary>
public sealed class NullableBoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split(',') ?? [];
        string Part(int i, string fallback) => parts.Length > i ? parts[i].Trim() : fallback;

        return BrushCache.Get(value switch
        {
            true  => Part(0, "#00C853"),
            false => Part(1, "#FF5252"),
            _     => Part(2, "#667799"),
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True → "Online", False → "Offline".</summary>
public sealed class BoolToOnlineConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Online" : "Offline";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// True when the bound enum equals the ConverterParameter, e.g.
/// <c>IsChecked="{Binding ActiveRoute, Converter={StaticResource EnumToBool}, ConverterParameter=Logs}"</c>.
///
/// ConvertBack returns Binding.DoNothing for the unchecked case on purpose. A RadioButton
/// group raises IsChecked=false on the outgoing button before IsChecked=true on the incoming
/// one; converting that false into a value would write garbage back over the route.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null
           && parameter is string name
           && string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string name && Enum.TryParse(targetType, name, out var parsed)
            ? parsed!
            : Binding.DoNothing;
}

/// <summary>
/// Multiplies a bound double by the ConverterParameter. Used to size the camera
/// surface against its container without a code-behind SizeChanged handler.
/// </summary>
public sealed class MultiplyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double d) return 0d;
        var factor = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1d;
        var result = d * factor;
        return double.IsNaN(result) || result < 0 ? 0d : result;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Like <see cref="MultiplyConverter"/> but keeps the sign, and clamps the magnitude to the
/// factor so the result stays inside the track it is offsetting within.
///
/// MultiplyConverter floors at zero because it feeds Width, where a negative is meaningless.
/// The lane-error indicator feeds TranslateTransform.X, where the sign *is* the information —
/// it says which side of the line the robot has drifted to.
/// </summary>
public sealed class SignedMultiplyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double d || double.IsNaN(d)) return 0d;
        var factor = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1d;
        return Math.Clamp(d * factor, -Math.Abs(factor), Math.Abs(factor));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The colour pair for a <c>ChallengeState</c> name, as the web dashboard's
/// <c>.state-*</c> rules define them.
///
/// A dictionary rather than seventeen DataTriggers: the trigger version would put the same
/// two hex values in the markup once per state and per usage, and the badge appears in more
/// than one place. Use it as two converters over one table —
/// <see cref="StateBadgeBackgroundConverter"/> and <see cref="StateBadgeForegroundConverter"/>.
/// </summary>
internal static class StateBadgeColors
{
    /// <summary>
    /// Shown for any state not in the table. The web dashboard has no such fallback: a state
    /// with no matching rule gets <c>.state-badge</c> alone, which sets no colours at all, so
    /// the pill vanishes into the card. Several states reach that path — see below — so this
    /// port gives them a visible neutral instead.
    /// </summary>
    public static readonly (string Bg, string Fg) Fallback = ("#EED5DC", "#6B0E22");

    private static readonly Dictionary<string, (string Bg, string Fg)> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Straight from dashboard_templates.py's stylesheet.
        ["LANE_FOLLOW"]        = ("#1b5e20", "#a5d6a7"),
        ["OBSTRUCTION"]        = ("#e65100", "#ffcc80"),
        ["ROUNDABOUT"]         = ("#4a148c", "#ce93d8"),
        ["BOOM_GATE_1"]        = ("#b71c1c", "#ef9a9a"),
        ["BOOM_GATE_2"]        = ("#b71c1c", "#ef9a9a"),
        ["TUNNEL"]             = ("#263238", "#90a4ae"),
        ["HILL"]               = ("#33691e", "#aed581"),
        ["BUMPER"]             = ("#795548", "#d7ccc8"),
        ["TRAFFIC_LIGHT"]      = ("#f57f17", "#fff9c4"),
        ["PARALLEL_PARK"]      = ("#0d47a1", "#90caf9"),
        ["PERPENDICULAR_PARK"] = ("#0d47a1", "#90caf9"),
        ["DRIVE_TO_PERP"]      = ("#1565c0", "#90caf9"),
        ["EMERGENCY_STOP"]     = ("#b71c1c", "#ffcdd2"),
        ["FINISHED"]           = ("#ffd600", "#333333"),

        // The stylesheet and the enum have drifted apart. ChallengeState (auto_driver.py:63)
        // has no BOOM_GATE_1/BOOM_GATE_2 or DRIVE_TO_PERP — those three rules are left over
        // from an older split — and seven states it *does* publish have no rule at all. The
        // entries below fill that gap, each borrowing the colour of its family so the badge
        // still reads as "same kind of thing" at a glance.
        ["BOOM_GATE"]        = ("#b71c1c", "#ef9a9a"),  // as BOOM_GATE_1/2
        ["HILL_DESCENT"]     = ("#33691e", "#aed581"),  // as HILL
        ["PARKING_IDLE"]     = ("#0d47a1", "#90caf9"),  // as the park states
        ["PARKING_PLAYBACK"] = ("#0d47a1", "#90caf9"),
        ["REVERSE_ADJUST"]   = ("#e65100", "#ffcc80"),  // as OBSTRUCTION — it is the recovery from one
        ["LANE_RECOVERY"]    = ("#e65100", "#ffcc80"),
        ["MANUAL"]           = ("#37474f", "#cfd8dc"),  // deliberately colourless: not a challenge
    };

    public static (string Bg, string Fg) For(string? state) =>
        state is not null && _map.TryGetValue(state.Trim(), out var pair) ? pair : Fallback;
}

/// <summary>State name → badge background brush.</summary>
public sealed class StateBadgeBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BrushCache.Get(StateBadgeColors.For(value as string).Bg);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>State name → badge text brush.</summary>
public sealed class StateBadgeForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BrushCache.Get(StateBadgeColors.For(value as string).Fg);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Hex string → frozen SolidColorBrush, memoised.
///
/// The colour converters are hit on every telemetry refresh across a dozen bindings,
/// and the set of distinct hex values in the app is tiny (a handful of palette
/// entries). Caching keeps that from allocating a fresh brush per tick.
/// </summary>
internal static class BrushCache
{
    private static readonly Dictionary<string, SolidColorBrush> _cache = [];
    private static readonly Lock _gate = new();

    public static SolidColorBrush Get(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;

        lock (_gate)
        {
            if (_cache.TryGetValue(hex, out var cached)) return cached;

            SolidColorBrush brush;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex)!;
                brush = new SolidColorBrush(color);
                brush.Freeze();
            }
            catch
            {
                brush = Brushes.Transparent;
            }

            _cache[hex] = brush;
            return brush;
        }
    }
}
