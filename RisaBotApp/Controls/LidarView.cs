using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RisaBotApp.Models;

namespace RisaBotApp.Controls;

/// <summary>
/// Top-down plot of one LiDAR scan, in the robot's own frame: forward is up, left is left.
///
/// A port of the <c>drawLidar</c> canvas routine in dashboard_templates.py (line 2440),
/// including its quirks — the same three range rings, the same two tunnel-detection
/// wedges, the same three distance bands for point colour. Anyone comparing this against
/// the browser should see the same picture, which is the point.
///
/// Drawn with <see cref="OnRender"/> rather than assembled from Shape elements. A scan is
/// several hundred points arriving four times a second; as Shapes that would mean building
/// and discarding hundreds of visuals per second, each with its own layout and hit-test
/// participation, to draw circles two pixels across. OnRender issues them straight into a
/// drawing instruction list instead.
///
/// The furniture — rings, labels, wedges, forward line — does not depend on the scan, so it
/// is rendered once into a cached <see cref="DrawingGroup"/> and invalidated only when the
/// control is resized.
/// </summary>
public class LidarView : FrameworkElement
{
    // ── Geometry constants, matching the web canvas ────────────────────────────

    /// <summary>
    /// Half-width of the plot in metres. The web canvas is 320px at 200px/m, so its edge
    /// sits at 0.8 m; expressing it this way instead of hardcoding the scale is what lets
    /// this control fill whatever box the layout gives it and still agree with the browser
    /// about where 0.4 m is.
    /// </summary>
    private const double MetresToEdge = 0.8;

    private static readonly double[] RangeRings = { 0.2, 0.4, 0.6 };

    /// <summary>Distance bands for point colour, metres.</summary>
    private const double NearBand = 0.3;
    private const double MidBand  = 0.5;

    /// <summary>
    /// The two sectors the tunnel follower watches, in the same screen-angle convention the
    /// canvas uses: zero points right, angles increase clockwise because y grows downward.
    /// Left wall is -180°..-120°, right wall -60°..0°, each 60° wide and centred 60° off
    /// straight ahead.
    /// </summary>
    private const double LeftWedgeStart  = -180;
    private const double LeftWedgeEnd    = -120;
    private const double RightWedgeStart = -60;
    private const double RightWedgeEnd   = 0;
    private const double WedgeRadiusM    = 0.6;

    // ── Brushes and pens ───────────────────────────────────────────────────────
    //
    // All frozen and static. A Freezable that is still mutable takes a lock and raises
    // change notifications on every access from the render thread; these never change, so
    // freezing them once removes that entirely.

    private static readonly Brush NearBrush  = Frozen(Color.FromRgb(0xFF, 0x52, 0x52));
    private static readonly Brush MidBrush   = Frozen(Color.FromRgb(0xFF, 0xD7, 0x40));
    private static readonly Brush FarBrush   = Frozen(Color.FromRgb(0x69, 0xF0, 0xAE));
    private static readonly Brush CenterBrush = Frozen(Color.FromRgb(0x00, 0xE5, 0xFF));

    private static readonly Brush RobotFill   = Frozen(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly Pen   RobotStroke = FrozenPen(Colors.White, 1.5);

    private static readonly Pen GridPen =
        FrozenPen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1);

    private static readonly Pen ForwardPen = FrozenDashedPen(
        Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 1, new DoubleCollection { 4, 4 });

    private static readonly Pen CenterPen = FrozenDashedPen(
        Color.FromRgb(0x00, 0xE5, 0xFF), 2, new DoubleCollection { 3, 3 });

    private static readonly Brush LeftWedgeBrush =
        Frozen(Color.FromArgb(0x14, 0x4F, 0xC3, 0xF7));
    private static readonly Brush RightWedgeBrush =
        Frozen(Color.FromArgb(0x14, 0xEF, 0x53, 0x50));

    private static readonly Brush LabelBrush = Frozen(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
    private static readonly Brush RingLabelBrush = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    /// <summary>The canvas's own CSS background (#0a0a0f), carried across unchanged.</summary>
    private static readonly Brush PlotBackground = Frozen(Color.FromRgb(0x0A, 0x0A, 0x0F));

    private static readonly Brush OverlayBackdrop = Frozen(Color.FromArgb(0xB3, 0, 0, 0));
    private static readonly Brush OverlayText = Frozen(Colors.White);
    /// <summary>Turn rate past which the follower is fighting the walls rather than
    /// trimming — the web version switches the readout to red at the same threshold.</summary>
    private const double HardTurnRate = 0.3;
    private static readonly Brush OverlayHot = NearBrush;
    private static readonly Brush OverlayCalm = FarBrush;

    private static readonly Typeface LabelFace =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface MonoFace =
        new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

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

    private static Pen FrozenDashedPen(Color c, double thickness, DoubleCollection dashes)
    {
        var p = new Pen(Frozen(c), thickness) { DashStyle = new DashStyle(dashes, 0) };
        p.Freeze();
        return p;
    }

    // ── Scan ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The scan to draw. Null renders the empty plot — furniture only — which is what the
    /// control should show before the first poll returns and after the robot goes away,
    /// rather than a blank rectangle that looks like a broken control.
    /// </summary>
    public static readonly DependencyProperty ScanProperty =
        DependencyProperty.Register(
            nameof(Scan), typeof(LidarData), typeof(LidarView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public LidarData? Scan
    {
        get => (LidarData?)GetValue(ScanProperty);
        set => SetValue(ScanProperty, value);
    }

    // ── Status, derived here so the mapping lives in one place ─────────────────

    private static readonly DependencyPropertyKey StatusTextKey =
        DependencyProperty.RegisterReadOnly(
            nameof(StatusText), typeof(string), typeof(LidarView),
            new PropertyMetadata("No data"));

    /// <summary>Short summary for the pill beside the card heading: the tunnel state if
    /// there is one, otherwise the point count.</summary>
    public static readonly DependencyProperty StatusTextProperty = StatusTextKey.DependencyProperty;

    public string StatusText => (string)GetValue(StatusTextProperty);

    private static readonly DependencyPropertyKey StatusBrushKey =
        DependencyProperty.RegisterReadOnly(
            nameof(StatusBrush), typeof(Brush), typeof(LidarView),
            new PropertyMetadata((Brush)Brushes.Gray));

    public static readonly DependencyProperty StatusBrushProperty = StatusBrushKey.DependencyProperty;

    public Brush StatusBrush => (Brush)GetValue(StatusBrushProperty);

    // ── Cached furniture ───────────────────────────────────────────────────────

    private DrawingGroup? _furniture;
    private Size _furnitureSize;

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        _furniture = null;   // rebuilt lazily on the next render
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Square plot, centred, so a non-square host box letterboxes rather than
        // distorting — an oval range ring would misreport distance in one axis.
        var side  = Math.Min(w, h);
        var plot  = new Rect((w - side) / 2, (h - side) / 2, side, side);
        var cx    = plot.X + side / 2;
        var cy    = plot.Y + side / 2;
        var scale = side / 2 / MetresToEdge;

        // Transparent fill over the whole element so it is hit-testable. It does *not*
        // clip anything: OnRender output is not bounded by the element unless it is
        // explicitly clipped, which is the next line's job.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        // Everything below is clipped to the square. The canvas this ports gets that for
        // free — a 320px canvas simply discards pixels outside itself, which is why the
        // browser shows a tidy 0.8 m disc even when the scan reaches three metres down a
        // corridor. Without this, those far points land wherever the arithmetic puts
        // them: over the neighbouring cards, off the bottom of the window, everywhere.
        var geo = new RectangleGeometry(plot, 12, 12);
        geo.Freeze();
        dc.PushClip(geo);

        // Same near-black as the canvas's CSS background. It is not decoration: it is what
        // makes the clip legible, so a point leaving the plot reads as out of range rather
        // than as the control losing it.
        dc.DrawRoundedRectangle(PlotBackground, null, plot, 12, 12);

        if (_furniture is null || _furnitureSize != new Size(w, h))
        {
            _furniture = BuildFurniture(cx, cy, scale);
            _furnitureSize = new Size(w, h);
        }
        dc.DrawDrawing(_furniture);

        var scan = Scan;
        DrawPoints(dc, scan, cx, cy, scale);
        DrawCenterline(dc, scan, cx, cy, scale);
        DrawRobot(dc, cx, cy);
        DrawEdgeLabels(dc, plot, cx, cy);
        DrawTunnelOverlay(dc, scan, plot);

        dc.Pop();

        UpdateStatus(scan);
    }

    /// <summary>
    /// Everything that depends only on the size: range rings and their labels, the two
    /// tunnel wedges, and the dashed forward line.
    /// </summary>
    private DrawingGroup BuildFurniture(double cx, double cy, double scale)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            foreach (var r in RangeRings)
                dc.DrawEllipse(null, GridPen, new Point(cx, cy), r * scale, r * scale);

            foreach (var r in RangeRings)
            {
                var text = Label($"{r:0.0}m", 10, RingLabelBrush, LabelFace);
                dc.DrawText(text, new Point(cx + r * scale + 2, cy - text.Height));
            }

            dc.DrawGeometry(LeftWedgeBrush, null,
                Wedge(cx, cy, WedgeRadiusM * scale, LeftWedgeStart, LeftWedgeEnd));
            dc.DrawGeometry(RightWedgeBrush, null,
                Wedge(cx, cy, WedgeRadiusM * scale, RightWedgeStart, RightWedgeEnd));

            dc.DrawLine(ForwardPen, new Point(cx, cy), new Point(cx, cy - 0.7 * scale));
        }
        group.Freeze();
        return group;
    }

    private void DrawPoints(DrawingContext dc, LidarData? scan, double cx, double cy, double scale)
    {
        if (scan is null) return;

        foreach (var p in scan.Points)
        {
            // Robot frame to screen: +x forward becomes up, +y left becomes left. Both
            // axes therefore invert, which is why this is a subtraction twice over and not
            // the sign flip it looks like a typo for.
            var px = cx - p.Y * scale;
            var py = cy - p.X * scale;

            var dist = Math.Sqrt(p.X * p.X + p.Y * p.Y);
            var brush = dist < NearBand ? NearBrush
                      : dist < MidBand  ? MidBrush
                                        : FarBrush;

            dc.DrawEllipse(brush, null, new Point(px, py), 2.5, 2.5);
        }
    }

    private void DrawCenterline(DrawingContext dc, LidarData? scan, double cx, double cy, double scale)
    {
        var cl = scan?.Centerline;
        if (cl is null || cl.Count < 2) return;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var first = new Point(cx - cl[0].Y * scale, cy - cl[0].X * scale);
            ctx.BeginFigure(first, false, false);
            for (var i = 1; i < cl.Count; i++)
                ctx.LineTo(new Point(cx - cl[i].Y * scale, cy - cl[i].X * scale), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, CenterPen, geo);

        foreach (var p in cl)
            dc.DrawEllipse(CenterBrush, null,
                new Point(cx - p.Y * scale, cy - p.X * scale), 3.5, 3.5);
    }

    /// <summary>A blunt triangle at the origin, nose up. Deliberately not to scale — it
    /// marks where the robot is, not how big it is.</summary>
    private void DrawRobot(DrawingContext dc, double cx, double cy)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(cx, cy - 10), true, true);
            ctx.LineTo(new Point(cx - 7, cy + 6), true, false);
            ctx.LineTo(new Point(cx + 7, cy + 6), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(RobotFill, RobotStroke, geo);
    }

    /// <summary>
    /// FRONT / L / R, hung on the edges of the plot square rather than of the element. In a
    /// letterboxed box those are different rectangles, and against the element's edges the
    /// labels drift away from the picture they annotate.
    /// </summary>
    private void DrawEdgeLabels(DrawingContext dc, Rect plot, double cx, double cy)
    {
        var front = Label("FRONT", 9, LabelBrush, LabelFace);
        dc.DrawText(front, new Point(cx - front.Width / 2, plot.Y + 4));

        var l = Label("L", 9, LabelBrush, LabelFace);
        dc.DrawText(l, new Point(plot.X + 8, cy - l.Height / 2));

        var r = Label("R", 9, LabelBrush, LabelFace);
        dc.DrawText(r, new Point(plot.Right - 8 - r.Width, cy - r.Height / 2));
    }

    /// <summary>
    /// The wall-follower readout, pinned to the bottom of the plot. Only drawn while the
    /// tunnel behaviour is actually steering — outside a tunnel the numbers are stale
    /// rather than wrong, which is the more misleading of the two.
    /// </summary>
    private void DrawTunnelOverlay(DrawingContext dc, LidarData? scan, Rect plot)
    {
        if (scan is null || !scan.Tunnel || !scan.HasTunnelDebug) return;

        var wz = scan.AngularZ ?? 0;
        var dir = wz > 0.01 ? "< LEFT" : wz < -0.01 ? "RIGHT >" : "^ STRAIGHT";
        var hot = Math.Abs(wz) > HardTurnRate;

        var left  = Label($"L:{scan.LeftDist ?? 0:0.00}m  R:{scan.RightDist ?? 0:0.00}m  " +
                          $"lat:{scan.DistError ?? 0:0.000}  ", 11, OverlayText, MonoFace);
        var right = Label($"w:{wz:0.00} {dir}", 11, hot ? OverlayHot : OverlayCalm, MonoFace);

        var boxH = Math.Max(left.Height, right.Height) + 12;
        var box  = new Rect(plot.X + 8, plot.Bottom - boxH - 8, plot.Width - 16, boxH);
        dc.DrawRoundedRectangle(OverlayBackdrop, null, box, 8, 8);

        var y = box.Y + (boxH - left.Height) / 2;
        dc.DrawText(left,  new Point(box.X + 8, y));
        dc.DrawText(right, new Point(box.X + 8 + left.Width, y));
    }

    private void UpdateStatus(LidarData? scan)
    {
        string text;
        Brush brush;

        if (scan is null || scan.Points.Count == 0)
        {
            text = "No data";
            brush = LabelBrush;
        }
        else if (scan.Tunnel)
        {
            text = "TUNNEL";
            brush = FarBrush;
        }
        else
        {
            text = $"{scan.Points.Count} pts";
            brush = CenterBrush;
        }

        // Guarded because OnRender runs on every invalidation and a redundant DP write
        // would raise a change notification that could invalidate the render again.
        if (StatusText != text) SetValue(StatusTextKey, text);
        if (!ReferenceEquals(StatusBrush, brush)) SetValue(StatusBrushKey, brush);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A pie slice from the centre, in the canvas angle convention: zero points right and
    /// angles increase clockwise on screen, because y grows downward.
    /// </summary>
    private static Geometry Wedge(double cx, double cy, double radius,
                                  double startDeg, double endDeg)
    {
        Point OnCircle(double deg)
        {
            var a = deg * Math.PI / 180;
            return new Point(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a));
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(cx, cy), true, true);
            ctx.LineTo(OnCircle(startDeg), false, false);
            ctx.ArcTo(OnCircle(endDeg), new Size(radius, radius), 0,
                      Math.Abs(endDeg - startDeg) > 180,
                      SweepDirection.Clockwise, false, false);
        }
        geo.Freeze();
        return geo;
    }

    private FormattedText Label(string text, double size, Brush brush, Typeface face) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
