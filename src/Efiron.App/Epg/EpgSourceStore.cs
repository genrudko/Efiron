namespace Efiron.App.Epg;

internal static class EpgSourceStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Efiron");

    private static readonly string SourceFilePath = Path.Combine(SettingsDirectory, "epg-source.txt");

    public static Uri? Load()
    {
        try
        {
            if (!File.Exists(SourceFilePath))
            {
                return null;
            }

            var value = File.ReadAllText(SourceFilePath).Trim();
            return TryCreateSupportedUri(value, out var source) ? source : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool TrySave(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!TryCreateSupportedUri(source.AbsoluteUri, out _))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SourceFilePath, source.AbsoluteUri);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateSupportedUri(string value, out Uri? source)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out source))
        {
            return false;
        }

        return source.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
