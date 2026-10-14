using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RisaBotApp.Services;

namespace RisaBotApp.Controls;

/// <summary>
/// Displays an MJPEG stream, with a frame-rate readout and stall detection.
///
/// Bind <see cref="StreamUrl"/> and toggle <see cref="IsActive"/>; the control owns the
/// reader's lifetime from there. It never starts a connection it was not asked for, which
/// matters because go2rtc keeps serving on :1984 after the ROS launch stops — a stream that
/// starts itself would sit there displaying a frozen frame and look live.
/// </summary>
public partial class MjpegView : UserControl
{
    private readonly MjpegStreamReader _reader = new();

    // Decode happens on the reader's thread at whatever rate the robot sends. Rather than
    // marshalling every frame to the dispatcher — which floods the queue on a fast feed and
    // makes the UI sluggish under exactly the conditions you want it responsive — the newest
    // frame is parked here and the render hook picks it up. Late frames are simply dropped,
    // which is the right trade for live video.
    private volatile BitmapSource? _pending;

    private readonly DispatcherTimer _statsTimer;
    private readonly Stopwatch _sinceLastFrame = new();
    private long _framesAtLastTick;

    // Two seconds without a frame is a stall. The feed runs at 15-30 fps, so this is
    // 30+ missed frames — well past anything a hiccup explains.
    private static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(2);

    public MjpegView()
    {
        InitializeComponent();

        _reader.FrameReady += OnFrameReady;
        _reader.Failed     += OnFailed;

        _statsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statsTimer.Tick += OnStatsTick;

        // The render hook is global, so it must be attached and detached with the control.
        // Leaving it wired on an unloaded view keeps this instance alive and burning a
        // callback per frame for a pane nobody is looking at.
        Loaded   += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _statsTimer.Stop();
        };
    }

    // ── Dependency properties ───────────────────────────────────────────────

    public static readonly DependencyProperty StreamUrlProperty =
        DependencyProperty.Register(
            nameof(StreamUrl), typeof(string), typeof(MjpegView),
            new PropertyMetadata(string.Empty, OnStreamSettingsChanged));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(MjpegView),
            new PropertyMetadata(false, OnStreamSettingsChanged));

    public static readonly DependencyProperty InactiveMessageProperty =
        DependencyProperty.Register(
            nameof(InactiveMessage), typeof(string), typeof(MjpegView),
            new PropertyMetadata("Start the system to bring the feed up."));

    /// <summary>The MJPEG endpoint, e.g. <c>http://risabot10.local:1984/api/stream.mjpeg?src=astra_camera</c>.</summary>
    public string StreamUrl
    {
        get => (string)GetValue(StreamUrlProperty);
        set => SetValue(StreamUrlProperty, value);
    }

    /// <summary>Whether the stream should be running. Bound to the launch state.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Text shown on the overlay while the stream is intentionally stopped.</summary>
    public string InactiveMessage
    {
        get => (string)GetValue(InactiveMessageProperty);
        set => SetValue(InactiveMessageProperty, value);
    }

    private static void OnStreamSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MjpegView)d).ApplyStreamState();

    // ── Stream control ──────────────────────────────────────────────────────

    private void ApplyStreamState()
    {
        if (IsActive && !string.IsNullOrWhiteSpace(StreamUrl))
        {
            ShowOverlay("Connecting…", "Opening the camera stream.");
            StatsBadge.Visibility = Visibility.Collapsed;

            _framesAtLastTick = 0;
            _sinceLastFrame.Restart();
            _reader.Start(StreamUrl);
            _statsTimer.Start();
        }
        else
        {
            _reader.Stop();
            _statsTimer.Stop();
            _sinceLastFrame.Reset();
            _pending = null;

            // Drop the last frame rather than leaving it on screen. A paused feed that still
            // shows the road ahead is genuinely dangerous to misread as live.
            FrameImage.Source     = null;
            StatsBadge.Visibility = Visibility.Collapsed;
            ShowOverlay("Camera offline", InactiveMessage);
        }
    }

    // ── Frame flow ──────────────────────────────────────────────────────────

    private void OnFrameReady(object? sender, BitmapSource frame) => _pending = frame;

    private void OnRendering(object? sender, EventArgs e)
    {
        var frame = _pending;
        if (frame is null) return;

        _pending = null;

        // The very first frame of a session fades up from black. Later frames are assigned
        // straight in — a 300 ms fade at 30 fps would just be a permanent 300 ms of ghosting.
        var isFirst = FrameImage.Source is null;
        FrameImage.Source = frame;
        _sinceLastFrame.Restart();

        if (isFirst)
        {
            FrameImage.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, FrameFade) { EasingFunction = FadeCurve });
        }

        if (OverlayPanel.Visibility == Visibility.Visible)
        {
            HideOverlay();
            StatsBadge.Visibility = Visibility.Visible;
        }
    }

    private void OnStatsTick(object? sender, EventArgs e)
    {
        var total = _reader.FrameCount;
        var fps   = total - _framesAtLastTick;
        _framesAtLastTick = total;

        StatsText.Text = $"{fps} fps";

        var stalled = _sinceLastFrame.IsRunning && _sinceLastFrame.Elapsed > StallThreshold;

        Animate.Tint(LiveDot, (Brush)FindResource(stalled ? "AccentOrange" : "AccentGreen"));

        if (stalled && total > 0)
            ShowOverlay("Feed stalled", "No frames received for a few seconds. The stream is still open.");
    }

    private void OnFailed(object? sender, string message)
        => Dispatcher.BeginInvoke(() =>
        {
            // Only surface a failure if the stream was meant to be running — a drop caused
            // by our own Stop() is not something to report.
            if (!IsActive) return;

            StatsBadge.Visibility = Visibility.Collapsed;
            ShowOverlay("Camera unavailable", message);
        });

    // ── Overlay transitions ─────────────────────────────────────────────────
    //
    // The overlay is a scrim over the last good frame, so it cuts hard between "picture"
    // and "no picture" if it is simply collapsed. Fading it means a stall reads as the feed
    // dimming out rather than as the pane being replaced, and the frame underneath stays
    // legible the whole way through.
    //
    // Visibility itself cannot be animated, so it is set around the fade — and because a
    // held animation outranks a local value, Opacity is only ever animated here, never
    // assigned.

    private static readonly Duration OverlayFade = new(TimeSpan.FromMilliseconds(220));
    private static readonly Duration FrameFade   = new(TimeSpan.FromMilliseconds(300));

    private static readonly IEasingFunction FadeCurve =
        new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// Guards the fade-out's completion handler. A feed that recovers and stalls again
    /// inside 220 ms would otherwise have the earlier fade's handler collapse the overlay
    /// the later one had just brought back.
    /// </summary>
    private bool _overlayWanted = true;

    private void ShowOverlay(string title, string detail)
    {
        OverlayTitle.Text  = title;
        OverlayDetail.Text = detail;

        if (_overlayWanted && OverlayPanel.Visibility == Visibility.Visible) return;

        _overlayWanted          = true;
        OverlayPanel.Visibility = Visibility.Visible;
        OverlayPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, OverlayFade) { EasingFunction = FadeCurve });
    }

    private void HideOverlay()
    {
        _overlayWanted = false;

        var fade = new DoubleAnimation(0, OverlayFade) { EasingFunction = FadeCurve };
        fade.Completed += (_, _) =>
        {
            if (!_overlayWanted)
                OverlayPanel.Visibility = Visibility.Collapsed;
        };

        OverlayPanel.BeginAnimation(OpacityProperty, fade);
    }
}
