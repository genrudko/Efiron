using Efiron.Application.Live;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Efiron.Desktop.Presentation;

public sealed record EpgChannelRowItem(
    int Number,
    LiveChannelSnapshot Snapshot,
    IReadOnlyList<EpgProgrammeBlockItem> Programmes,
    double TimelineWidth)
{
    public string StableId => Snapshot.Channel.StableId;

    public string Name => Snapshot.Channel.Name;

    public string Category => Snapshot.Channel.Category ?? string.Empty;

    public ImageSource? LogoUrl => Snapshot.Channel.LogoUri is { } uri
        ? new BitmapImage(uri)
        : null;

    public string Initials
    {
        get
        {
            var words = Name
                .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return "TV";
            }

            return string.Concat(words.Take(2).Select(static word =>
                char.ToUpperInvariant(word[0])));
        }
    }
}
