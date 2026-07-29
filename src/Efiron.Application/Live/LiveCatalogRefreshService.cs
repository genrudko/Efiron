using System.Text;
using Efiron.Application.Playlists;
using Efiron.Application.ProgrammeGuide;
using Efiron.Application.Sources;
using Efiron.Domain.ProgrammeGuide;

namespace Efiron.Application.Live;

public sealed class LiveCatalogRefreshService(
    ISourceContentLoader sourceContentLoader,
    IPlaylistParser playlistParser,
    IProgrammeGuideParser programmeGuideParser,
    ProgrammeGuideChannelMatcher programmeGuideMatcher)
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async ValueTask<LiveCatalogSnapshot> RefreshAsync(
        SourceConfiguration configuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var playlistSource = configuration.Playlist;
        if (playlistSource is not { IsEnabled: true })
        {
            throw new InvalidOperationException(
                "A configured and enabled playlist source is required.");
        }

        var playlistPayload = await sourceContentLoader.LoadAsync(
            playlistSource,
            cancellationToken);
        var playlistContent = DecodePlaylist(playlistPayload.Content);
        var playlist = playlistParser.Parse(
            playlistContent,
            playlistPayload.EffectiveUri);

        ProgrammeGuideDocument guide = ProgrammeGuideDocument.Empty;
        if (configuration.ProgrammeGuide is { IsEnabled: true } guideSource)
        {
            var guidePayload = await sourceContentLoader.LoadAsync(
                guideSource,
                cancellationToken);
            guide = programmeGuideParser.Parse(guidePayload.Content);
        }

        var matches = programmeGuideMatcher.Match(
            playlist.Channels,
            guide.Channels);
        var programmesByChannel = guide.Programmes
            .GroupBy(static programme => programme.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<Programme>)group
                    .OrderBy(static programme => programme.Start)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var channels = playlist.Channels
            .Select(channel => BuildChannelSnapshot(
                channel,
                matches.ProgrammeGuideChannelByStableId,
                programmesByChannel,
                now))
            .ToArray();
        var categories = playlist.Channels
            .Select(static channel => channel.Category)
            .Where(static category => !string.IsNullOrWhiteSpace(category))
            .Select(static category => category!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new LiveCatalogSnapshot(
            channels,
            categories,
            playlist.Warnings,
            guide.Warnings,
            matches.ExactIdMatches,
            matches.UniqueNameMatches,
            DateTimeOffset.UtcNow);
    }

    private static LiveChannelSnapshot BuildChannelSnapshot(
        Efiron.Domain.Channels.ChannelDefinition channel,
        IReadOnlyDictionary<string, string> guideChannelByStableId,
        IReadOnlyDictionary<string, IReadOnlyList<Programme>> programmesByChannel,
        DateTimeOffset now)
    {
        if (!guideChannelByStableId.TryGetValue(channel.StableId, out var guideChannelId) ||
            !programmesByChannel.TryGetValue(guideChannelId, out var schedule))
        {
            return new LiveChannelSnapshot(channel, null, null, null);
        }

        Programme? current = null;
        Programme? next = null;

        for (var index = 0; index < schedule.Count; index++)
        {
            var programme = schedule[index];
            if (programme.Start > now)
            {
                next = programme;
                break;
            }

            var effectiveStop = programme.Stop ??
                (index + 1 < schedule.Count ? schedule[index + 1].Start : null);
            if (effectiveStop is null || now < effectiveStop)
            {
                current = programme;
            }
        }

        if (next is null)
        {
            next = schedule.FirstOrDefault(programme => programme.Start > now);
        }

        return new LiveChannelSnapshot(
            channel,
            guideChannelId,
            current,
            next);
    }

    private static string DecodePlaylist(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var reader = new StreamReader(
                stream,
                StrictUtf8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: false);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "The playlist text encoding is not valid UTF-8/UTF-16/UTF-32.",
                exception);
        }
    }
}
