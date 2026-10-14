using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace RisaBotApp.Services;

/// <summary>
/// Reads a <c>multipart/x-mixed-replace</c> MJPEG stream and raises one event per frame.
///
/// Replaces the MAUI WebView, which loaded a wrapper HTML document pointing an &lt;img&gt;
/// at go2rtc. That worked but gave the app no visibility into the stream: no frame rate,
/// no way to tell a stalled feed from a static scene, and a WebView2 runtime dependency
/// on the desktop.
///
/// Parsing notes:
///
///   • go2rtc declares the boundary in the Content-Type header, but not every producer
///     does, and some prefix it with "--" while others do not. Rather than trust it, the
///     reader scans for the JPEG SOI/EOI markers (FFD8 … FFD9) directly. That is what
///     actually delimits a frame and it survives boundary quirks entirely.
///
///   • Bytes arrive in arbitrary chunks that rarely align with frame boundaries, so a
///     rolling buffer accumulates until a complete SOI…EOI pair is present.
///
///   • Each decoded BitmapSource is frozen. Decoding happens on a background thread, and
///     an unfrozen BitmapSource belongs to the thread that created it — handing one to the
///     UI thread throws. Freezing also lets WPF skip per-render copies.
/// </summary>
public sealed class MjpegStreamReader : IDisposable
{
    private const byte Marker = 0xFF;
    private const byte Soi    = 0xD8;   // start of image
    private const byte Eoi    = 0xD9;   // end of image

    // A 4K JPEG frame runs well under 2 MB. Anything past this means the SOI scan latched
    // onto noise, so the buffer is dropped rather than grown without bound.
    private const int MaxFrameBytes = 8 * 1024 * 1024;

    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;
    private Task? _pump;

    /// <summary>Raised on a background thread for every decoded frame. The image is frozen.</summary>
    public event EventHandler<BitmapSource>? FrameReady;

    /// <summary>Raised when the stream drops or cannot be opened. Carries a display message.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Raised once the first frame of a session arrives.</summary>
    public event EventHandler? Started;

    public bool IsRunning => _pump is { IsCompleted: false };

    /// <summary>Frames decoded since the current session started.</summary>
    public long FrameCount { get; private set; }

    public MjpegStreamReader(HttpClient http)
    {
        // Deliberately not the DI-registered HttpClient. That one carries a 10 s timeout,
        // which is correct for REST calls and fatal here — an MJPEG response never
        // completes, so any finite timeout kills the stream on a fixed schedule.
        _http = http;
    }

    public MjpegStreamReader() : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
    {
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>Open <paramref name="url"/> and begin raising <see cref="FrameReady"/>.</summary>
    public void Start(string url)
    {
        Stop();

        FrameCount = 0;
        _cts  = new CancellationTokenSource();
        _pump = Task.Run(() => PumpAsync(url, _cts.Token));
    }

    /// <summary>Stop reading and release the connection. Safe to call when not running.</summary>
    public void Stop()
    {
        if (_cts is null) return;

        _cts.Cancel();
        _cts.Dispose();
        _cts  = null;
        _pump = null;
    }

    // ── Pump ────────────────────────────────────────────────────────────────

    private async Task PumpAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // ResponseHeadersRead is mandatory. The default buffers the whole response
            // before returning, and this response has no end — it would simply never return.
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                Failed?.Invoke(this, $"Camera stream returned HTTP {(int)response.StatusCode}.");
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await ReadFramesAsync(stream, ct);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via Stop().
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex.Message);
        }
    }

    private async Task ReadFramesAsync(Stream stream, CancellationToken ct)
    {
        var chunk  = new byte[16 * 1024];
        var buffer = new List<byte>(64 * 1024);

        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0)
            {
                Failed?.Invoke(this, "Camera stream closed by the robot.");
                return;
            }

            buffer.AddRange(new ArraySegment<byte>(chunk, 0, read));

            // A single read can straddle more than one frame when the UI thread has been
            // busy, so drain everything complete in the buffer before reading again.
            while (TryExtractFrame(buffer, out var jpeg))
            {
                var image = Decode(jpeg);
                if (image is null) continue;

                FrameCount++;
                if (FrameCount == 1) Started?.Invoke(this, EventArgs.Empty);

                FrameReady?.Invoke(this, image);
            }

            if (buffer.Count > MaxFrameBytes) buffer.Clear();
        }
    }

    /// <summary>
    /// Pulls the first complete JPEG out of <paramref name="buffer"/> and removes it,
    /// along with any multipart preamble that preceded it.
    /// </summary>
    private static bool TryExtractFrame(List<byte> buffer, out byte[] jpeg)
    {
        jpeg = [];

        var start = IndexOfMarker(buffer, Soi, 0);
        if (start < 0)
        {
            // No SOI at all: everything so far is header noise. Keep the last byte in case
            // it is the 0xFF half of a marker split across two reads.
            if (buffer.Count > 1) buffer.RemoveRange(0, buffer.Count - 1);
            return false;
        }

        var end = IndexOfMarker(buffer, Eoi, start + 2);
        if (end < 0) return false;

        var length = end + 2 - start;
        jpeg = new byte[length];
        buffer.CopyTo(start, jpeg, 0, length);
        buffer.RemoveRange(0, end + 2);
        return true;
    }

    private static int IndexOfMarker(List<byte> buffer, byte second, int from)
    {
        for (var i = Math.Max(from, 0); i < buffer.Count - 1; i++)
            if (buffer[i] == Marker && buffer[i + 1] == second)
                return i;

        return -1;
    }

    private static BitmapSource? Decode(byte[] jpeg)
    {
        try
        {
            using var ms = new MemoryStream(jpeg, writable: false);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            // OnLoad + a stream we then dispose: the decoder must not hold a reference to
            // the MemoryStream past EndInit, or every frame leaks its backing array.
            bitmap.CacheOption   = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.StreamSource  = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            // A torn frame is expected occasionally on a lossy link. Drop it; the next
            // one arrives in ~33 ms and reporting it would spam the UI.
            return null;
        }
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
