using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Efiron.Application.Playlists;
using Efiron.Application.ProgrammeGuide;
using Efiron.Application.Sources;
using Efiron.Domain.Playlists;
using Efiron.Domain.ProgrammeGuide;

namespace Efiron.Application.Live;

public sealed class LiveCatalogRefreshService(
    ISourceContentLoader sourceContentLoader,
    IPlaylistParser playlistParser,
    IProgrammeGuideParser programmeGuideParser,
    ProgrammeGuideChannelMatcher programmeGuideMatcher)
{
    private const int PastProgrammeGuideDays = 1;
    private const int FutureProgrammeGuideDays = 7;
    private const int MaximumGuideCacheFiles = 4;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string GuideCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Efiron",
        "epg-cache");

    public async ValueTask<LiveCatalogSnapshot> RefreshPlaylistAsync(
        SourceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var (playlist, payload) = await LoadPlaylistAsync(
            configuration,
            cancellationToken).ConfigureAwait(false);
        var channels = playlist.Channels
            .Select(static channel => new LiveChannelSnapshot(
                channel,
                ProgrammeGuideChannelId: null,
                CurrentProgramme: null,
                NextProgramme: null))
            .ToArray();

        return new LiveCatalogSnapshot(
            channels,
            BuildCategories(playlist),
            playlist.Warnings,
            [],
            0,
            0,
            DateTimeOffset.UtcNow)
        {
            PlaylistSourceCacheHit = payload.IsCacheHit,
        };
    }

    public async ValueTask<LiveCatalogSnapshot> RefreshAsync(
        SourceConfiguration configuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var (playlist, playlistPayload) = await LoadPlaylistAsync(
            configuration,
            cancellationToken).ConfigureAwait(false);

        ProgrammeGuideDocument guide = ProgrammeGuideDocument.Empty;
        var guideSourceCacheHit = false;
        var guideParseCacheHit = false;
        if (configuration.ProgrammeGuide is { IsEnabled: true } guideSource)
        {
            var guidePayload = await sourceContentLoader.LoadAsync(
                    guideSource,
                    cancellationToken)
                .ConfigureAwait(false);
            guideSourceCacheHit = guidePayload.IsCacheHit;

            var dayStart = new DateTimeOffset(
                now.Year,
                now.Month,
                now.Day,
                0,
                0,
                0,
                now.Offset);
            var windowStart = dayStart.AddDays(-PastProgrammeGuideDays);
            var windowEnd = dayStart.AddDays(FutureProgrammeGuideDays + 1);
            var cachePath = GetGuideCachePath(
                guidePayload.Content,
                windowStart,
                windowEnd);
            guide = TryLoadGuideCache(cachePath);
            if (!ReferenceEquals(guide, ProgrammeGuideDocument.Empty))
            {
                guideParseCacheHit = true;
            }
            else
            {
                guide = programmeGuideParser is IWindowedProgrammeGuideParser windowed
                    ? windowed.Parse(guidePayload.Content, windowStart, windowEnd)
                    : programmeGuideParser.Parse(guidePayload.Content);
                TrySaveGuideCache(cachePath, guide);
            }
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

        return new LiveCatalogSnapshot(
            channels,
            BuildCategories(playlist),
            playlist.Warnings,
            guide.Warnings,
            matches.ExactIdMatches,
            matches.UniqueNameMatches,
            DateTimeOffset.UtcNow)
        {
            PlaylistSourceCacheHit = playlistPayload.IsCacheHit,
            ProgrammeGuideSourceCacheHit = guideSourceCacheHit,
            ProgrammeGuideParseCacheHit = guideParseCacheHit,
        };
    }

    private async ValueTask<(PlaylistDocument Document, LoadedSourceContent Payload)>
        LoadPlaylistAsync(
            SourceConfiguration configuration,
            CancellationToken cancellationToken)
    {
        var playlistSource = configuration.Playlist;
        if (playlistSource is not { IsEnabled: true })
        {
            throw new InvalidOperationException(
                "A configured and enabled playlist source is required.");
        }

        var payload = await sourceContentLoader.LoadAsync(
                playlistSource,
                cancellationToken)
            .ConfigureAwait(false);
        var playlistContent = DecodePlaylist(payload.Content);
        return (
            playlistParser.Parse(playlistContent, payload.EffectiveUri),
            payload);
    }

    private static IReadOnlyList<string> BuildCategories(PlaylistDocument playlist) =>
        playlist.Channels
            .Select(static channel => channel.Category)
            .Where(static category => !string.IsNullOrWhiteSpace(category))
            .Select(static category => category!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static LiveChannelSnapshot BuildChannelSnapshot(
        Efiron.Domain.Channels.ChannelDefinition channel,
        IReadOnlyDictionary<string, string> guideChannelByStableId,
        IReadOnlyDictionary<string, IReadOnlyList<Programme>> programmesByChannel,
        DateTimeOffset now)
    {
        if (!guideChannelByStableId.TryGetValue(channel.StableId, out var guideChannelId))
        {
            return new LiveChannelSnapshot(channel, null, null, null);
        }

        if (!programmesByChannel.TryGetValue(guideChannelId, out var schedule))
        {
            return new LiveChannelSnapshot(channel, guideChannelId, null, null);
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
            next)
        {
            Schedule = schedule,
        };
    }

    private static string GetGuideCachePath(
        ReadOnlyMemory<byte> content,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var hash = Convert.ToHexString(SHA256.HashData(content.Span));
        var fileName = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"guide-{hash}-{windowStart:yyyyMMdd}-{windowEnd:yyyyMMdd}.json.gz");
        return Path.Combine(GuideCacheDirectory, fileName);
    }

    private static ProgrammeGuideDocument TryLoadGuideCache(string path)
    {
        if (!File.Exists(path))
        {
            return ProgrammeGuideDocument.Empty;
        }

        try
        {
            using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using var gzip = new GZipStream(
                file,
                CompressionMode.Decompress,
                leaveOpen: false);
            return JsonSerializer.Deserialize<ProgrammeGuideDocument>(
                       gzip,
                       CacheJsonOptions) ??
                   ProgrammeGuideDocument.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                JsonException or
                NotSupportedException)
        {
            return ProgrammeGuideDocument.Empty;
        }
    }

    private static void TrySaveGuideCache(
        string path,
        ProgrammeGuideDocument guide)
    {
        try
        {
            Directory.CreateDirectory(GuideCacheDirectory);
            var temporaryPath = path + ".tmp";
            try
            {
                using (var file = new FileStream(
                           temporaryPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None,
                           64 * 1024,
                           FileOptions.SequentialScan))
                using (var gzip = new GZipStream(
                           file,
                           CompressionLevel.Fastest,
                           leaveOpen: false))
                {
                    JsonSerializer.Serialize(gzip, guide, CacheJsonOptions);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }

            foreach (var stale in new DirectoryInfo(GuideCacheDirectory)
                         .EnumerateFiles("guide-*.json.gz")
                         .OrderByDescending(static file => file.LastWriteTimeUtc)
                         .Skip(MaximumGuideCacheFiles))
            {
                TryDelete(stale.FullName);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
