using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace C0n50le.Services;

/// <summary>
/// Records the screen in front (the monitor under the foreground window) to an MP4 file, video only: Windows Graphics
/// Capture hands over the frames on the GPU and Windows' own H.264 encoder writes them, so nothing else is installed.
/// It records what the desktop shows, games in a window or borderless included.
/// </summary>
public sealed class ScreenRecorder : IAsyncDisposable
{
    private readonly BlockingCollection<Direct3D11CaptureFrame> _frames = new(boundedCapacity: 8);
    private readonly CancellationTokenSource _stop = new();
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _device;
    private Task? _transcode;
    private TimeSpan? _firstFrame;

    public string Path { get; }
    public DateTimeOffset Started { get; } = DateTimeOffset.Now;

    private ScreenRecorder(string path) => Path = path;

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    /// <summary>Starts recording the monitor under the foreground window into <paramref name="folder"/>.</summary>
    public static async Task<ScreenRecorder> StartAsync(string folder)
    {
        if (!IsSupported) throw new NotSupportedException("Windows Graphics Capture non disponibile");
        Directory.CreateDirectory(folder);
        var recorder = new ScreenRecorder(System.IO.Path.Combine(folder, $"c0n50le {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4"));
        try
        {
            await recorder.BeginAsync();
            return recorder;
        }
        catch
        {
            await recorder.DisposeAsync();
            throw;
        }
    }

    private async Task BeginAsync()
    {
        _device = Direct3D.CreateDevice();
        var item = Direct3D.CaptureItemForMonitor(MonitorFromWindow(GetForegroundWindow(), 2));
        // H.264 wants even sizes.
        var width = (uint)(item.Size.Width & ~1);
        var height = (uint)(item.Size.Height & ~1);

        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = true;

        var video = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, width, height);
        var source = new MediaStreamSource(new VideoStreamDescriptor(video)) { BufferTime = TimeSpan.Zero, IsLive = true };
        source.Starting += (_, args) => args.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += OnSampleRequested;

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null;
        profile.Video.Width = width;
        profile.Video.Height = height;
        profile.Video.FrameRate.Numerator = 60;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = (uint)Math.Clamp(width * height * 60L / 10, 8_000_000, 60_000_000);

        await File.WriteAllBytesAsync(Path, []);
        var file = await StorageFile.GetFileFromPathAsync(Path);
        var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
        if (!prepared.CanTranscode)
        {
            stream.Dispose();
            throw new InvalidOperationException($"Codifica non possibile: {prepared.FailureReason}");
        }
        _session.StartCapture();
        _transcode = Task.Run(async () =>
        {
            try { await prepared.TranscodeAsync(); }
            finally { stream.Dispose(); }
        });
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var frame = sender.TryGetNextFrame();
        if (frame is null) return;
        if (_stop.IsCancellationRequested || !_frames.TryAdd(frame)) frame.Dispose();
    }

    /// <summary>The encoder asks for the next frame; a null sample ends the file.</summary>
    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        try
        {
            while (true)
            {
                if (!_frames.TryTake(out var frame, Timeout.Infinite, _stop.Token)) break;
                // Only the newest frame matters when the encoder falls behind.
                while (_frames.TryTake(out var newer))
                {
                    frame.Dispose();
                    frame = newer;
                }
                _firstFrame ??= frame.SystemRelativeTime;
                var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, frame.SystemRelativeTime - _firstFrame.Value);
                var owned = frame;
                sample.Processed += (_, _) => owned.Dispose();
                args.Request.Sample = sample;
                return;
            }
        }
        catch (OperationCanceledException) { }
        args.Request.Sample = null;
    }

    /// <summary>Stops and finishes the file; returns its path.</summary>
    public async Task<string> StopAsync()
    {
        _stop.Cancel();
        _session?.Dispose();
        _session = null;
        if (_transcode is not null) await _transcode.ConfigureAwait(false);
        await DisposeAsync().ConfigureAwait(false);
        return Path;
    }

    public ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _session?.Dispose();
        _pool?.Dispose();
        while (_frames.TryTake(out var frame)) frame.Dispose();
        _device?.Dispose();
        _session = null;
        _pool = null;
        _device = null;
        return ValueTask.CompletedTask;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);

    /// <summary>The bits of Direct3D and capture interop WinRT doesn't project.</summary>
    private static class Direct3D
    {
        private static readonly Guid DxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static readonly Guid CaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        private static readonly Guid CaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        public static IDirect3DDevice CreateDevice()
        {
            // Hardware, with BGRA support (what capture and the encoder share).
            var result = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var device, out _, out var context);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in DxgiDevice, out var dxgi));
                try
                {
                    Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                    try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
                    finally { Marshal.Release(inspectable); }
                }
                finally { Marshal.Release(dxgi); }
            }
            finally
            {
                Marshal.Release(context);
                Marshal.Release(device);
            }
        }

        public static GraphicsCaptureItem CaptureItemForMonitor(nint monitor)
        {
            var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in CaptureItemInterop, out var interopPointer));
            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(interopPointer);
                var iid = CaptureItem;
                var item = interop.CreateForMonitor(monitor, ref iid);
                try { return GraphicsCaptureItem.FromAbi(item); }
                finally { Marshal.Release(item); }
            }
            finally { Marshal.Release(interopPointer); }
        }

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            nint CreateForWindow(nint window, ref Guid iid);
            nint CreateForMonitor(nint monitor, ref Guid iid);
        }

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels,
            uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
    }
}
