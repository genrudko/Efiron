using Efiron.Application.Live;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Efiron.Desktop.Presentation;

public enum EpgLogoLoadState
{
    NotRequested,
    Loading,
    Loaded,
    Failed,
}

public sealed record EpgChannelRowItem(
    int Number,
    LiveChannelSnapshot Snapshot,
    IReadOnlyList<EpgProgrammeBlockItem> Programmes,
    double TimelineWidth)
{
    private ImageSource? _logoUrl;
    private bool _logoUrlResolved;

    public string StableId => Snapshot.Channel.StableId;

    public string Name => Snapshot.Channel.Name;

    public string Category => Snapshot.Channel.Category ?? string.Empty;

    public EpgLogoLoadState LogoLoadState { get; private set; }

    public ImageSource? LogoUrl
    {
        get
        {
            if (_logoUrlResolved)
            {
                return _logoUrl;
            }

            _logoUrlResolved = true;
            _logoUrl = Snapshot.Channel.LogoUri is { } uri
                ? new BitmapImage(uri)
                : null;
            LogoLoadState = _logoUrl is null
                ? EpgLogoLoadState.Failed
                : EpgLogoLoadState.NotRequested;
            return _logoUrl;
        }
    }

    public void MarkLogoLoading(ImageSource source)
    {
        if (ReferenceEquals(_logoUrl, source) &&
            LogoLoadState is EpgLogoLoadState.NotRequested)
        {
            LogoLoadState = EpgLogoLoadState.Loading;
        }
    }

    public void MarkLogoLoaded(ImageSource source)
    {
        if (ReferenceEquals(_logoUrl, source))
        {
            LogoLoadState = EpgLogoLoadState.Loaded;
        }
    }

    public void MarkLogoFailed(ImageSource source)
    {
        if (ReferenceEquals(_logoUrl, source))
        {
            LogoLoadState = EpgLogoLoadState.Failed;
        }
    }
}
