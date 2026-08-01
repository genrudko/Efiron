using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Efiron.Desktop.Diagnostics;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private const string EpgVerificationEnvironmentVariable =
        "EFIRON_CI_EPG_VERIFICATION";

    private bool _epgVerificationNavigationStarted;
    private bool _epgVerificationCaptureStarted;

    private bool TryOpenEpgVerificationWorkspace()
    {
        if (_epgVerificationNavigationStarted ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    EpgVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return false;
        }

        _epgVerificationNavigationStarted = true;
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                try
                {
                    ShowProgrammeWorkspace();
                }
                catch (Exception exception)
                {
                    StartupDiagnostics.RecordCrash(
                        "epg-verification-navigation",
                        exception);
                    TryWriteEpgNavigationError(exception);
                }
            });
        return true;
    }

    private static void TryWriteEpgNavigationError(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "epg-preview-error.log"),
                exception.ToString());
        }
        catch
        {
        }
    }

    private async Task CaptureEpgEvidenceAsync()
    {
        if (_epgVerificationCaptureStarted ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    EpgVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        _epgVerificationCaptureStarted = true;
        var diagnosticsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "diagnostics");
        var evidencePath = Path.Combine(
            diagnosticsDirectory,
            "epg-runtime.json");
        var previewPath = Path.Combine(
            diagnosticsDirectory,
            "epg-preview.png");
        var scrollbarPixelsPath = Path.Combine(
            diagnosticsDirectory,
            "epg-scrollbar-pixels.json");
        var errorPath = Path.Combine(
            diagnosticsDirectory,
            "epg-preview-error.log");

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(950), _lifetime.Token);
            var evidence = await ProgrammeGuideWorkspace.CreateRuntimeEvidenceAsync(
                _lifetime.Token);
            var persistentScrollBar =
                ProgrammeGuideWorkspace.GetPersistentVerticalScrollBarEvidence();
            var scrollbarPixelGeometry =
                ProgrammeGuideWorkspace.GetPersistentScrollbarPixelEvidence(WindowRoot);
            if (evidence.AllChannelsMode &&
                (!persistentScrollBar.IsVisible ||
                 persistentScrollBar.RailWidth < 20 ||
                 persistentScrollBar.RailHeight <= 100 ||
                 persistentScrollBar.ThumbWidth < 10 ||
                 persistentScrollBar.ThumbHeight < 40 ||
                 persistentScrollBar.ThumbHeight >= persistentScrollBar.RailHeight ||
                 persistentScrollBar.ThumbOpacity < 0.9 ||
                 string.IsNullOrWhiteSpace(persistentScrollBar.RailColor) ||
                 string.IsNullOrWhiteSpace(persistentScrollBar.ThumbColor) ||
                 string.Equals(
                     persistentScrollBar.RailColor,
                     persistentScrollBar.ThumbColor,
                     StringComparison.OrdinalIgnoreCase) ||
                 persistentScrollBar.Maximum <= persistentScrollBar.Minimum ||
                 persistentScrollBar.ViewportSize <= 0 ||
                 Math.Abs(
                     persistentScrollBar.Value - evidence.VerticalOffset) >= 1 ||
                 !scrollbarPixelGeometry.IsVisible ||
                 scrollbarPixelGeometry.RailBounds.Width < 20 ||
                 scrollbarPixelGeometry.ThumbBounds.Width < 10 ||
                 scrollbarPixelGeometry.ThumbBounds.Height < 40))
            {
                throw new InvalidOperationException(
                    "The persistent EPG scrollbar rail/thumb was not rendered, themed or synchronized: " +
                    JsonSerializer.Serialize(new
                    {
                        Runtime = persistentScrollBar,
                        Pixels = scrollbarPixelGeometry,
                    }));
            }

            const int maximumCaptureAttempts = 8;
            RenderTargetBitmap? bitmap = null;
            byte[]? pixelBytes = null;
            BgraPixel? thumbPixel = null;
            BgraPixel? railPixel = null;
            double colorDistance = 0;
            double luminanceDelta = 0;
            var captureAttempts = 0;

            for (var attempt = 0; attempt < maximumCaptureAttempts; attempt++)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(attempt == 0 ? 350 : 175),
                    _lifetime.Token);

                var candidateBitmap = new RenderTargetBitmap();
                await candidateBitmap.RenderAsync(WindowRoot);
                var candidatePixels = await candidateBitmap.GetPixelsAsync();
                if (candidateBitmap.PixelWidth <= 0 ||
                    candidateBitmap.PixelHeight <= 0)
                {
                    continue;
                }

                bitmap = candidateBitmap;
                pixelBytes = candidatePixels.ToArray();
                captureAttempts = attempt + 1;

                if (!evidence.AllChannelsMode)
                {
                    break;
                }

                var scaleX = bitmap.PixelWidth /
                    Math.Max(1d, WindowRoot.ActualWidth);
                var scaleY = bitmap.PixelHeight /
                    Math.Max(1d, WindowRoot.ActualHeight);
                thumbPixel = ReadBgraPixel(
                    pixelBytes,
                    bitmap.PixelWidth,
                    bitmap.PixelHeight,
                    (scrollbarPixelGeometry.ThumbBounds.X +
                     scrollbarPixelGeometry.ThumbBounds.Width / 2) * scaleX,
                    (scrollbarPixelGeometry.ThumbBounds.Y +
                     scrollbarPixelGeometry.ThumbBounds.Height / 2) * scaleY);
                var railSampleY = scrollbarPixelGeometry.RailBounds.Y +
                    scrollbarPixelGeometry.RailBounds.Height * 0.82;
                if (railSampleY >= scrollbarPixelGeometry.ThumbBounds.Y &&
                    railSampleY <= scrollbarPixelGeometry.ThumbBounds.Bottom)
                {
                    railSampleY = scrollbarPixelGeometry.RailBounds.Y +
                        scrollbarPixelGeometry.RailBounds.Height * 0.55;
                }

                railPixel = ReadBgraPixel(
                    pixelBytes,
                    bitmap.PixelWidth,
                    bitmap.PixelHeight,
                    (scrollbarPixelGeometry.RailBounds.X +
                     scrollbarPixelGeometry.RailBounds.Width / 2) * scaleX,
                    railSampleY * scaleY);
                colorDistance = ColorDistance(thumbPixel, railPixel);
                luminanceDelta = Math.Abs(
                    RelativeLuminance(thumbPixel) -
                    RelativeLuminance(railPixel));

                if (thumbPixel.A >= 180 &&
                    colorDistance >= 45 &&
                    luminanceDelta >= 24)
                {
                    break;
                }
            }

            if (bitmap is null || pixelBytes is null)
            {
                throw new InvalidOperationException(
                    "The EPG preview rendered with an empty pixel size.");
            }

            Directory.CreateDirectory(diagnosticsDirectory);
            if (evidence.AllChannelsMode)
            {
                if (thumbPixel is null || railPixel is null ||
                    thumbPixel.A < 180 ||
                    colorDistance < 45 ||
                    luminanceDelta < 24)
                {
                    throw new InvalidOperationException(
                        "The physical EPG scrollbar thumb did not become visible after repeated composition frames: " +
                        JsonSerializer.Serialize(new
                        {
                            scrollbarPixelGeometry.ActualTheme,
                            CaptureAttempts = captureAttempts,
                            MaximumCaptureAttempts = maximumCaptureAttempts,
                            ThumbPixel = thumbPixel,
                            RailPixel = railPixel,
                            ColorDistance = colorDistance,
                            LuminanceDelta = luminanceDelta,
                        }));
                }

                await File.WriteAllTextAsync(
                    scrollbarPixelsPath,
                    JsonSerializer.Serialize(new
                    {
                        scrollbarPixelGeometry.ActualTheme,
                        scrollbarPixelGeometry.RailBounds,
                        scrollbarPixelGeometry.ThumbBounds,
                        ExpectedRailColor = scrollbarPixelGeometry.RailColor,
                        ExpectedThumbColor = scrollbarPixelGeometry.ThumbColor,
                        scrollbarPixelGeometry.ThumbOpacity,
                        CaptureAttempts = captureAttempts,
                        ThumbPixel = thumbPixel,
                        RailPixel = railPixel,
                        ColorDistance = colorDistance,
                        LuminanceDelta = luminanceDelta,
                        RecordedAtUtc = DateTimeOffset.UtcNow,
                    }),
                    _lifetime.Token);
            }

            await File.WriteAllTextAsync(
                evidencePath,
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);
            await File.WriteAllBytesAsync(previewPath, [], _lifetime.Token);
            var file = await StorageFile.GetFileFromPathAsync(previewPath);
            await using var randomAccessStream = await file.OpenStreamForWriteAsync();
            randomAccessStream.SetLength(0);
            var encoder = await BitmapEncoder.CreateAsync(
                BitmapEncoder.PngEncoderId,
                randomAccessStream.AsRandomAccessStream());
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth,
                (uint)bitmap.PixelHeight,
                96,
                96,
                pixelBytes);
            await encoder.FlushAsync();
            await randomAccessStream.FlushAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StartupDiagnostics.RecordCrash(
                "epg-verification-capture",
                exception);
            try
            {
                if (File.Exists(evidencePath))
                {
                    File.Delete(evidencePath);
                }
                if (File.Exists(scrollbarPixelsPath))
                {
                    File.Delete(scrollbarPixelsPath);
                }
                Directory.CreateDirectory(diagnosticsDirectory);
                await File.WriteAllTextAsync(
                    errorPath,
                    exception.ToString());
            }
            catch
            {
            }
        }
    }

    private static BgraPixel ReadBgraPixel(
        byte[] pixels,
        int width,
        int height,
        double x,
        double y)
    {
        var pixelX = Math.Clamp((int)Math.Round(x), 0, width - 1);
        var pixelY = Math.Clamp((int)Math.Round(y), 0, height - 1);
        var index = checked((pixelY * width + pixelX) * 4);
        return new BgraPixel(
            pixels[index + 2],
            pixels[index + 1],
            pixels[index],
            pixels[index + 3]);
    }

    private static double ColorDistance(BgraPixel first, BgraPixel second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static double RelativeLuminance(BgraPixel pixel) =>
        0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B;

    private sealed record BgraPixel(byte R, byte G, byte B, byte A);
}