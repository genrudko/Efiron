using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private const string PresentationCaptureEnvironmentVariable =
        "EFIRON_CI_PRESENTATION_CAPTURE";

    private bool _presentationCaptureStarted;

    private async Task CapturePresentationPreviewAsync()
    {
        if (_presentationCaptureStarted ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    PresentationCaptureEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        _presentationCaptureStarted = true;
        var diagnosticsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "diagnostics");
        var previewPath = Path.Combine(
            diagnosticsDirectory,
            "poster-preview.png");
        var errorPath = Path.Combine(
            diagnosticsDirectory,
            "poster-preview-error.log");

        try
        {
            var readinessDeadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (!LiveTvWorkspace.IsPlaybackVisualReady &&
                   DateTimeOffset.UtcNow < readinessDeadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), _lifetime.Token);
            }

            if (!LiveTvWorkspace.IsPlaybackVisualReady)
            {
                throw new InvalidOperationException(
                    "The presentation preview timed out before the playback visual became ready.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300), _lifetime.Token);
            Directory.CreateDirectory(diagnosticsDirectory);

            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(WindowRoot);
            var pixels = await bitmap.GetPixelsAsync();
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                throw new InvalidOperationException(
                    "The presentation preview rendered with an empty pixel size.");
            }

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
                pixels.ToArray());
            await encoder.FlushAsync();
            await randomAccessStream.FlushAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            try
            {
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
}
