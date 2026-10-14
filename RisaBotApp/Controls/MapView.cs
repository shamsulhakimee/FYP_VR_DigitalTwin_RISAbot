using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RisaBotApp.Controls;

/// <summary>
/// The SLAM occupancy grid, with the robot on it.
///
/// Built the same way as <see cref="LidarView"/> — a <see cref="FrameworkElement"/> that
/// overrides <see cref="OnRender"/> — and for the same reason: the picture is one bitmap
/// plus a handful of primitives, and assembling that from Image/Path elements would mean
/// rebuilding a visual tree every time the map ticks.
///
/// The map is letterboxed into whatever box the layout gives it rather than stretched. A
/// non-uniform scale would make the scale bar lie in one axis, and a corridor would read as
/// wider than it is — the same choice DashboardView documents for the LiDAR plot.
///
/// Coordinates. The PNG's rows already run top-down (the dashboard reverses the
/// OccupancyGrid's bottom-up rows when encoding), so pixel (0,0) is the top-left of the
/// grid and world y therefore still increases *upward* on screen. Everything in
/// <see cref="WorldToScreen"/> follows from that plus <c>origin</c>, the world position of
/// the grid's bottom-left corner.
/// </summary>
public class MapView : FrameworkElement
{
    // ── Appearance ─────────────────────────────────────────────────────────────

    private static readonly Brush Backdrop      = Frozen(Color.FromRgb(0x0A, 0x0A, 0x0F));
    private static readonly Brush RobotFill     = Frozen(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly Pen   RobotStroke   = FrozenPen(Colors.White, 1.5);
    private static readonly Brush TrailBrush    = Frozen(Color.FromArgb(0x66, 0x00, 0xE5, 0xFF));
    private static readonly Brush ScaleBrush    = Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
    private static readonly Pen   ScalePen      = FrozenPen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF), 2);
    private static readonly Brush PlaceholderBrush = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    private static readonly Pen   BorderPen     = FrozenPen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 1);

    private static readonly Typeface LabelFace =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Lengths a scale bar is allowed to be, metres. The largest that fits in a
    /// third of the width wins, so the bar stays a round number at every zoom.</summary>
    private static readonly double[] ScaleSteps = { 0.25, 0.5, 1, 2, 5, 10, 20 };

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double thickness)
    {
        var p = new Pen(Frozen(c), thickness);
        p.Freeze();
        return p;
    }

    public MapView()
    {
        // Cells are 5 cm squares blown up to whatever the window allows, so the default
        // smoothing turns crisp walls into grey smears and makes unknown space look mapped.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    // ── Inputs ─────────────────────────────────────────────────────────────────

    /// <summary>The decoded occupancy grid. Null draws the placeholder.</summary>
    public static readonly DependencyProperty MapImageProperty =
        DependencyProperty.Register(
            nameof(MapImage), typeof(ImageSource), typeof(MapView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ImageSource? MapImage
    {
        get => (ImageSource?)GetValue(MapImageProperty);
        set => SetValue(MapImageProperty, value);
    }

    /// <summary>Metres per cell.</summary>
    public static readonly DependencyProperty ResolutionProperty =
        DependencyProperty.Register(
            nameof(Resolution), typeof(double), typeof(MapView),
            new FrameworkPropertyMetadata(0.05, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Resolution
    {
        get => (double)GetValue(ResolutionProperty);
        set => SetValue(ResolutionProperty, value);
    }

    /// <summary>World x of the grid's bottom-left corner, metres.</summary>
    public static readonly DependencyProperty OriginXProperty =
        DependencyProperty.Register(
            nameof(OriginX), typeof(double), typeof(MapView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double OriginX
    {
        get => (double)GetValue(OriginXProperty);
        set => SetValue(OriginXProperty, value);
    }

    /// <summary>World y of the grid's bottom-left corner, metres.</summary>
    public static readonly DependencyProperty OriginYProperty =
        DependencyProperty.Register(
            nameof(OriginY), typeof(double), typeof(MapView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double OriginY
    {
        get => (double)GetValue(OriginYProperty);
        set => SetValue(OriginYProperty, value);
    }

    public static readonly DependencyProperty RobotXProperty =
        DependencyProperty.Register(
            nameof(RobotX), typeof(double), typeof(MapView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double RobotX
    {
        get => (double)GetValue(RobotXProperty);
        set => SetValue(RobotXProperty, value);
    }

    public static readonly DependencyProperty RobotYProperty =
        DependencyProperty.Register(
            nameof(RobotY), typeof(double), typeof(MapView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double RobotY
    {
        get => (double)GetValue(RobotYProperty);
        set => SetValue(RobotYProperty, value);
    }

    /// <summary>Heading in radians, CCW from world +x.</summary>
    public static readonly DependencyProperty RobotYawProperty =
        DependencyProperty.Register(
            nameof(RobotYaw), typeof(double), typeof(MapView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double RobotYaw
    {
        get => (double)GetValue(RobotYawProperty);
        set => SetValue(RobotYawProperty, value);
    }

    /// <summary>
    /// Whether the pose is real. False hides the marker rather than parking it at the
    /// origin — a marker sitting confidently in the corner of the map is worse than none,
    /// because it looks like an answer.
    /// </summary>
    public static readonly DependencyProperty RobotValidProperty =
        DependencyProperty.Register(
            nameof(RobotValid), typeof(bool), typeof(MapView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool RobotValid
    {
        get => (bool)GetValue(RobotValidProperty);
        set => SetValue(RobotValidProperty, value);
    }

    /// <summary>Text shown in place of the map when there is nothing to draw.</summary>
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(
            nameof(PlaceholderText), typeof(string), typeof(MapView),
            new FrameworkPropertyMetadata("Waiting for map…",
                                          FrameworkPropertyMetadataOptions.AffectsRender));

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    // ── Render ─────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var bounds = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(Backdrop, BorderPen, bounds, 12, 12);

        var geo = new RectangleGeometry(bounds, 12, 12);
        geo.Freeze();
        dc.PushClip(geo);

        var image = MapImage;
        var (pixelW, pixelH) = PixelSize(image);

        if (image is null || pixelW <= 0 || pixelH <= 0)
        {
            DrawPlaceholder(dc, bounds);
            dc.Pop();
            return;
        }

        // Letterbox: one scale for both axes, centred in whatever is left over.
        var scale = Math.Min(w / pixelW, h / pixelH);
        var drawW = pixelW * scale;
        var drawH = pixelH * scale;
        var dest  = new Rect((w - drawW) / 2, (h - drawH) / 2, drawW, drawH);

        dc.DrawImage(image, dest);

        var res = Resolution > 0 ? Resolution : 0.05;

        if (RobotValid)
            DrawRobot(dc, WorldToScreen(RobotX, RobotY, dest, pixelH, scale, res), RobotYaw);

        DrawScaleBar(dc, bounds, scale, res);

        dc.Pop();
    }

    /// <summary>
    /// World metres to a point inside the drawn image.
    ///
    /// The PNG is emitted top-row-first, so its row 0 is the *top* of the grid: a cell's
    /// distance from the bottom (<c>(y - originY) / resolution</c>) has to be measured down
    /// from the image's bottom edge, which is what the subtraction from
    /// <paramref name="pixelH"/> does. Getting this backwards produces a map that looks
    /// right until the robot drives, and then has it moving the wrong way vertically.
    /// </summary>
    private Point WorldToScreen(double worldX, double worldY, Rect dest,
                                double pixelH, double scale, double res)
    {
        var col      = (worldX - OriginX) / res;
        var rowFromB = (worldY - OriginY) / res;

        return new Point(dest.X + col * scale,
                         dest.Y + (pixelH - rowFromB) * scale);
    }

    /// <summary>
    /// A blunt triangle at the robot's pose, nose along its heading. Fixed size in pixels,
    /// not to scale: at 5 cm a cell a to-scale car would be a few pixels across and would
    /// vanish under the walls it is supposed to be navigating between.
    /// </summary>
    private static void DrawRobot(DrawingContext dc, Point at, double yaw)
    {
        // Robot frame (forward, left) to screen. Screen y grows downward, so the whole
        // second component is negated — hence one sign flip, not two.
        Point P(double fwd, double lat) => new(
            at.X + fwd * Math.Cos(yaw) - lat * Math.Sin(yaw),
            at.Y - (fwd * Math.Sin(yaw) + lat * Math.Cos(yaw)));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P(11, 0), true, true);
            ctx.LineTo(P(-7, 6), true, false);
            ctx.LineTo(P(-7, -6), true, false);
        }
        geo.Freeze();

        dc.DrawEllipse(TrailBrush, null, at, 14, 14);
        dc.DrawGeometry(RobotFill, RobotStroke, geo);
    }

    /// <summary>
    /// A labelled bar in the bottom-left corner. The only thing on screen that says how big
    /// the arena is — the map fills its box at every size, so nothing else conveys scale.
    /// </summary>
    private void DrawScaleBar(DrawingContext dc, Rect bounds, double scale, double res)
    {
        var pixelsPerMetre = scale / res;
        if (pixelsPerMetre <= 0 || double.IsInfinity(pixelsPerMetre)) return;

        var maxPixels = bounds.Width / 3;

        var metres = ScaleSteps[0];
        foreach (var step in ScaleSteps)
        {
            if (step * pixelsPerMetre > maxPixels) break;
            metres = step;
        }

        var barPixels = metres * pixelsPerMetre;
        if (barPixels < 12) return;   // too cramped to read; better to show nothing

        var y  = bounds.Bottom - 18;
        var x0 = bounds.X + 14;
        var x1 = x0 + barPixels;

        dc.DrawLine(ScalePen, new Point(x0, y), new Point(x1, y));
        dc.DrawLine(ScalePen, new Point(x0, y - 4), new Point(x0, y + 4));
        dc.DrawLine(ScalePen, new Point(x1, y - 4), new Point(x1, y + 4));

        var label = Label(metres < 1 ? $"{metres * 100:0} cm" : $"{metres:0.#} m", 11, ScaleBrush);
        dc.DrawText(label, new Point(x0, y - 4 - label.Height));
    }

    private void DrawPlaceholder(DrawingContext dc, Rect bounds)
    {
        var text = Label(PlaceholderText, 13, PlaceholderBrush);
        dc.DrawText(text, new Point(bounds.X + (bounds.Width - text.Width) / 2,
                                    bounds.Y + (bounds.Height - text.Height) / 2));
    }

    /// <summary>
    /// Pixel dimensions of the grid. Read off <see cref="BitmapSource"/> rather than
    /// <see cref="ImageSource.Width"/>, which is in device-independent units and would be
    /// wrong for any bitmap not tagged 96 dpi.
    /// </summary>
    private static (double W, double H) PixelSize(ImageSource? image) => image switch
    {
        BitmapSource bs => (bs.PixelWidth, bs.PixelHeight),
        not null        => (image.Width, image.Height),
        _               => (0, 0),
    };

    private FormattedText Label(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, size,
            brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
