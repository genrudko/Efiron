namespace Efiron.Core.Epg;

public sealed record EpgNowNext(
    XmlTvProgramme? Current,
    XmlTvProgramme? Next,
    DateTimeOffset? EffectiveCurrentStop,
    double ProgressPercent,
    bool IsProgressKnown);

public sealed record EpgTimelineEntry(
    XmlTvProgramme Programme,
    DateTimeOffset EffectiveStop,
    DateTimeOffset VisibleStart,
    DateTimeOffset VisibleStop,
    bool StartsBeforeWindow,
    bool EndsAfterWindow);

public sealed class EpgScheduleIndex
{
    private readonly Dictionary<string, XmlTvProgramme[]> _programmesByChannel;

    public EpgScheduleIndex(IEnumerable<XmlTvProgramme> programmes)
    {
        ArgumentNullException.ThrowIfNull(programmes);

        _programmesByChannel = programmes
            .GroupBy(static programme => programme.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static programme => programme.Start)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public EpgNowNext Find(string channelId, DateTimeOffset instant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);

        if (!_programmesByChannel.TryGetValue(channelId, out var programmes) || programmes.Length == 0)
        {
            return new EpgNowNext(null, null, null, 0, false);
        }

        var candidateIndex = FindLastStartedProgrammeIndex(programmes, instant);
        if (candidateIndex < 0)
        {
            return new EpgNowNext(null, programmes[0], null, 0, false);
        }

        var candidate = programmes[candidateIndex];
        var following = candidateIndex + 1 < programmes.Length
            ? programmes[candidateIndex + 1]
            : null;
        var effectiveStop = candidate.Stop ?? following?.Start;

        if (effectiveStop is not null && effectiveStop <= instant)
        {
            return new EpgNowNext(null, following, null, 0, false);
        }

        var progress = CalculateProgress(candidate.Start, effectiveStop, instant);
        return new EpgNowNext(
            candidate,
            following,
            effectiveStop,
            progress.ProgressPercent,
            progress.IsKnown);
    }

    public IReadOnlyList<EpgTimelineEntry> FindRange(
        string channelId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        if (windowEnd <= windowStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowEnd),
                windowEnd,
                "The timeline window end must be later than its start.");
        }

        if (!_programmesByChannel.TryGetValue(channelId, out var programmes) || programmes.Length == 0)
        {
            return Array.Empty<EpgTimelineEntry>();
        }

        var firstIndex = FindLastStartedProgrammeIndex(programmes, windowStart);
        firstIndex = Math.Max(firstIndex, 0);

        while (firstIndex < programmes.Length)
        {
            var effectiveStop = GetEffectiveStop(programmes, firstIndex, windowEnd);
            if (effectiveStop > windowStart)
            {
                break;
            }

            firstIndex++;
        }

        var entries = new List<EpgTimelineEntry>();
        for (var index = firstIndex; index < programmes.Length; index++)
        {
            var programme = programmes[index];
            if (programme.Start >= windowEnd)
            {
                break;
            }

            var effectiveStop = GetEffectiveStop(programmes, index, windowEnd);
            if (effectiveStop <= windowStart || effectiveStop <= programme.Start)
            {
                continue;
            }

            var visibleStart = programme.Start < windowStart ? windowStart : programme.Start;
            var visibleStop = effectiveStop > windowEnd ? windowEnd : effectiveStop;
            if (visibleStop <= visibleStart)
            {
                continue;
            }

            entries.Add(new EpgTimelineEntry(
                programme,
                effectiveStop,
                visibleStart,
                visibleStop,
                programme.Start < windowStart,
                effectiveStop > windowEnd));
        }

        return entries;
    }

    private static DateTimeOffset GetEffectiveStop(
        IReadOnlyList<XmlTvProgramme> programmes,
        int index,
        DateTimeOffset fallbackStop)
    {
        var programme = programmes[index];
        if (programme.Stop is not null)
        {
            return programme.Stop.Value;
        }

        return index + 1 < programmes.Count
            ? programmes[index + 1].Start
            : fallbackStop;
    }

    private static int FindLastStartedProgrammeIndex(
        IReadOnlyList<XmlTvProgramme> programmes,
        DateTimeOffset instant)
    {
        var low = 0;
        var high = programmes.Count - 1;
        var result = -1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (programmes[middle].Start <= instant)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    private static (double ProgressPercent, bool IsKnown) CalculateProgress(
        DateTimeOffset start,
        DateTimeOffset? stop,
        DateTimeOffset instant)
    {
        if (stop is null || stop <= start)
        {
            return (0, false);
        }

        var duration = stop.Value - start;
        var elapsed = instant - start;
        var progress = elapsed.TotalMilliseconds / duration.TotalMilliseconds * 100;
        return (Math.Clamp(progress, 0, 100), true);
    }
}
