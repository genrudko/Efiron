namespace Efiron.Core.Epg;

public sealed record XmlTvProgramme
{
    public XmlTvProgramme(
        string channelId,
        DateTimeOffset start,
        DateTimeOffset? stop,
        string title,
        string? subtitle,
        string? description,
        IReadOnlyList<string> categories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(categories);

        ChannelId = channelId;
        Start = start;
        Stop = stop;
        Title = title;
        Subtitle = subtitle;

        var categoryCandidates = new List<string>(categories);
        Description = NormalizeDescription(description, categoryCandidates);
        Categories = NormalizeCategories(categoryCandidates);
    }

    public string ChannelId { get; }

    public DateTimeOffset Start { get; }

    public DateTimeOffset? Stop { get; }

    public string Title { get; }

    public string? Subtitle { get; }

    public string? Description { get; }

    public IReadOnlyList<string> Categories { get; }

    private static string? NormalizeDescription(string? description, ICollection<string> categoryCandidates)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var value = description.Trim();
        if (value.Length < 3 || value[0] != '[')
        {
            return value;
        }

        var closingBracketIndex = value.IndexOf(']');
        if (closingBracketIndex <= 1)
        {
            return value;
        }

        var prefixTokens = value[1..closingBracketIndex]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!prefixTokens.Any(IsTechnicalAlias))
        {
            return value;
        }

        foreach (var token in prefixTokens)
        {
            if (!IsTechnicalAlias(token))
            {
                categoryCandidates.Add(token);
            }
        }

        var remainder = value[(closingBracketIndex + 1)..].TrimStart();
        return remainder.Length == 0 ? null : remainder;
    }

    private static IReadOnlyList<string> NormalizeCategories(IEnumerable<string> categories)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawCategory in categories)
        {
            if (string.IsNullOrWhiteSpace(rawCategory))
            {
                continue;
            }

            var value = rawCategory.Trim();
            var isProviderList = value.Length >= 2 && value[0] == '[' && value[^1] == ']';
            var candidates = isProviderList
                ? value[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [value];

            foreach (var candidate in candidates)
            {
                var category = candidate.Trim();
                if (category.Length == 0 || IsTechnicalAlias(category) || !seen.Add(category))
                {
                    continue;
                }

                normalized.Add(category);
            }
        }

        return normalized.ToArray();
    }

    private static bool IsTechnicalAlias(string value) =>
        value.StartsWith("alias:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("alias=", StringComparison.OrdinalIgnoreCase);
}
