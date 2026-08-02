using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private const string FullscreenVerificationEnvironmentVariable =
        "EFIRON_CI_FULLSCREEN_VERIFICATION";
    private const long WsSysMenuEvidence = 0x00080000L;
    private const long WsMinimizeBoxEvidence = 0x00020000L;
    private const long WsMaximizeBoxEvidence = 0x00010000L;

    private bool _fullscreenVerificationStarted;

    private bool TryStartFullscreenVerification()
    {
        if (_fullscreenVerificationStarted ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    FullscreenVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return false;
        }

        _fullscreenVerificationStarted = true;
        _ = CaptureFullscreenEvidenceAsync();
        return true;
    }

    private async Task CaptureFullscreenEvidenceAsync()
    {
        var diagnosticsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "diagnostics");
        var evidencePath = Path.Combine(
            diagnosticsDirectory,
            "fullscreen-runtime.json");
        var previewPath = Path.Combine(
            diagnosticsDirectory,
            "fullscreen-preview.png");
        var errorPath = Path.Combine(
            diagnosticsDirectory,
            "fullscreen-preview-error.log");

        try
        {
            await Task.Delay(450, _lifetime.Token);
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var expectedNormalStyle = _normalWindowStyle.ToInt64();
            var expectedNormalExStyle = _normalWindowExStyle.ToInt64();
            var cycles = new List<WindowChromeCycleEvidence>();

            SetFullscreen(true);
            var readinessClock = await WaitForFullscreenVideoAsync();

            for (var cycle = 1; cycle <= 3; cycle++)
            {
                if (cycle > 1)
                {
                    SetFullscreen(true);
                    await Task.Delay(650, _lifetime.Token);
                }

                SetFullscreen(false);
                await Task.Delay(800, _lifetime.Token);

                var style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
                var exStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
                var normalizedStyle = style & ~WsVisible;
                var normalizedExpectedStyle = expectedNormalStyle & ~WsVisible;
                var dwmRestoreHResult = ApplyDwmWindowedPolicyProbe(windowHandle);
                var regionProbe = CreateRectRgn(0, 0, 1, 1);
                var regionResult = regionProbe == 0
                    ? -1
                    : GetWindowRgn(windowHandle, regionProbe);
                if (regionProbe != 0)
                {
                    _ = DeleteObject(regionProbe);
                }

                var captionStylesPresent =
                    (style & WsSysMenuEvidence) != 0 &&
                    (style & WsMinimizeBoxEvidence) != 0 &&
                    (style & WsMaximizeBoxEvidence) != 0;
                var cycleEvidence = new WindowChromeCycleEvidence(
                    cycle,
                    AppWindow.Presenter.Kind.ToString(),
                    style,
                    exStyle,
                    expectedNormalStyle,
                    expectedNormalExStyle,
                    normalizedStyle == normalizedExpectedStyle,
                    exStyle == expectedNormalExStyle,
                    captionStylesPresent,
                    regionResult == 0,
                    DwmNcRenderingUseWindowStyle,
                    dwmRestoreHResult,
                    dwmRestoreHResult == 0,
                    ExtendsContentIntoTitleBar,
                    TitleBarDragRegion.Visibility.ToString(),
                    WindowRoot.RowDefinitions[0].Height.Value,
                    DateTimeOffset.UtcNow);
                cycles.Add(cycleEvidence);

                if (!string.Equals(
                        cycleEvidence.PresenterKind,
                        AppWindowPresenterKind.Overlapped.ToString(),
                        StringComparison.Ordinal) ||
                    !cycleEvidence.NormalStyleRestored ||
                    !cycleEvidence.NormalExStyleRestored ||
                    !cycleEvidence.CaptionButtonStylesPresent ||
                    !cycleEvidence.WindowRegionCleared ||
                    !cycleEvidence.DwmWindowStyleRenderingRestored ||
                    !cycleEvidence.ExtendsContentIntoTitleBar ||
                    !string.Equals(
                        cycleEvidence.TitleBarDragRegionVisibility,
                        Microsoft.UI.Xaml.Visibility.Visible.ToString(),
                        StringComparison.Ordinal) ||
                    cycleEvidence.TitleBarRowHeight <= 0)
                {
                    throw new InvalidOperationException(
                        $"Window chrome was not restored after fullscreen cycle {cycle}: " +
                        JsonSerializer.Serialize(cycleEvidence));
                }
            }

            SetFullscreen(true);
            await Task.Delay(900, _lifetime.Token);
            var surface = LiveTvWorkspace.GetFullscreenSurfaceEvidence();
            if (!string.Equals(
                    surface.PlaybackState,
                    "Playing",
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(surface.PlaybackSource) ||
                string.IsNullOrWhiteSpace(surface.VideoCropGeometry))
            {
                throw new InvalidOperationException(
                    $"Fullscreen video was not ready after chrome cycles: " +
                    $"state={surface.PlaybackState}, source={surface.PlaybackSource}, " +
                    $"crop={surface.VideoCropGeometry}.");
            }

            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(WindowRoot);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                throw new InvalidOperationException(
                    "The fullscreen preview rendered with an empty pixel size.");
            }

            var edge = MeasureHorizontalEdges(
                pixels,
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                rowsPerEdge: 6);
            surface = LiveTvWorkspace.GetFullscreenSurfaceEvidence();
            var evidence = new
            {
                PresenterKind = AppWindow.Presenter.Kind.ToString(),
                TitleBarRowHeight = WindowRoot.RowDefinitions[0].Height.Value,
                NavigationColumnWidth = ShellNavigationColumn.Width.Value,
                WindowBackground = WindowRoot.Background is
                    Microsoft.UI.Xaml.Media.SolidColorBrush windowBrush
                        ? windowBrush.Color.ToString()
                        : string.Empty,
                Surface = surface,
                WindowedCycles = cycles,
                PixelWidth = bitmap.PixelWidth,
                PixelHeight = bitmap.PixelHeight,
                edge.TopWhitePixelRatio,
                edge.BottomWhitePixelRatio,
                VideoReadyMilliseconds = readinessClock.Elapsed.TotalMilliseconds,
                RecordedAtUtc = DateTimeOffset.UtcNow,
            };

            Directory.CreateDirectory(diagnosticsDirectory);
            await File.WriteAllTextAsync(
                evidencePath,
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);
            await WritePngAsync(
                previewPath,
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                pixels,
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            try
            {
                Directory.CreateDirectory(diagnosticsDirectory);
                await File.WriteAllTextAsync(errorPath, exception.ToString());
            }
            catch
            {
            }
        }
    }

    private async Task<Stopwatch> WaitForFullscreenVideoAsync()
    {
        var readinessClock = Stopwatch.StartNew();
        Views.LiveTvView.FullscreenSurfaceEvidence? surface = null;
        while (readinessClock.Elapsed < TimeSpan.FromSeconds(20))
        {
            surface = LiveTvWorkspace.GetFullscreenSurfaceEvidence();
            if (string.Equals(
                    surface.PlaybackState,
                    "Playing",
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(surface.PlaybackSource) &&
                !string.IsNullOrWhiteSpace(surface.VideoCropGeometry))
            {
                return readinessClock;
            }

            await Task.Delay(200, _lifetime.Token);
        }

        surface = LiveTvWorkspace.GetFullscreenSurfaceEvidence();
        throw new InvalidOperationException(
            $"Fullscreen video was not ready: state={surface.PlaybackState}, " +
            $"source={surface.PlaybackSource}, crop={surface.VideoCropGeometry}.");
    }

    private static int ApplyDwmWindowedPolicyProbe(nint windowHandle)
    {
        var policy = DwmNcRenderingUseWindowStyle;
        var result = DwmSetWindowAttribute(
            windowHandle,
            DwmwaNcRenderingPolicy,
            ref policy,
            Marshal.SizeOf<int>());
        _ = DwmFlush();
        return result;
    }

    private static (double TopWhitePixelRatio, double BottomWhitePixelRatio)
        MeasureHorizontalEdges(
        IReadOnlyList<byte> pixels,
        int width,
        int height,
        int rowsPerEdge)
    {
        var rowCount = Math.Min(rowsPerEdge, Math.Max(1, height / 2));
        var topWhite = 0L;
        var bottomWhite = 0L;
        var samples = (long)width * rowCount;

        for (var row = 0; row < rowCount; row++)
        {
            topWhite += CountWhitePixels(pixels, width, row);
            bottomWhite += CountWhitePixels(pixels, width, height - 1 - row);
        }

        return (
            samples == 0 ? 0 : (double)topWhite / samples,
            samples == 0 ? 0 : (double)bottomWhite / samples);
    }

    private static long CountWhitePixels(
        IReadOnlyList<byte> pixels,
        int width,
        int row)
    {
        var white = 0L;
        var offset = row * width * 4;
        for (var column = 0; column < width; column++)
        {
            var pixel = offset + column * 4;
            var blue = pixels[pixel];
            var green = pixels[pixel + 1];
            var red = pixels[pixel + 2];
            if (red >= 235 && green >= 235 && blue >= 235)
            {
                white++;
            }
        }

        return white;
    }

    private static async Task WritePngAsync(
        string path,
        int width,
        int height,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(path, [], cancellationToken);
        var file = await StorageFile.GetFileFromPathAsync(path);
        await using var stream = await file.OpenStreamForWriteAsync();
        stream.SetLength(0);
        var encoder = await BitmapEncoder.CreateAsync(
            BitmapEncoder.PngEncoderId,
            stream.AsRandomAccessStream());
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)width,
            (uint)height,
            96,
            96,
            pixels);
        await encoder.FlushAsync();
        await stream.FlushAsync(cancellationToken);
    }

    private sealed record WindowChromeCycleEvidence(
        int Cycle,
        string PresenterKind,
        long Style,
        long ExStyle,
        long ExpectedStyle,
        long ExpectedExStyle,
        bool NormalStyleRestored,
        bool NormalExStyleRestored,
        bool CaptionButtonStylesPresent,
        bool WindowRegionCleared,
        int DwmNonClientRenderingPolicy,
        int DwmRestoreHResult,
        bool DwmWindowStyleRenderingRestored,
        bool ExtendsContentIntoTitleBar,
        string TitleBarDragRegionVisibility,
        double TitleBarRowHeight,
        DateTimeOffset RecordedAtUtc);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint hwnd, nint region);
}
