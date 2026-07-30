using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
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
            ShowProgrammeWorkspace);
        return true;
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
        var errorPath = Path.Combine(
            diagnosticsDirectory,
            "epg-preview-error.log");

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(950), _lifetime.Token);
            var evidence = await ProgrammeGuideWorkspace.CreateRuntimeEvidenceAsync(
                _lifetime.Token);
            Directory.CreateDirectory(diagnosticsDirectory);
            await File.WriteAllTextAsync(
                evidencePath,
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);

            await Task.Delay(TimeSpan.FromMilliseconds(350), _lifetime.Token);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(WindowRoot);
            var pixels = await bitmap.GetPixelsAsync();
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                throw new InvalidOperationException(
                    "The EPG preview rendered with an empty pixel size.");
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
