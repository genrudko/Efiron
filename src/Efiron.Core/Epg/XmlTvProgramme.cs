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
        Description = description;
        Categories = NormalizeCategories(categories);
    }

    public string ChannelId { get; }

    public DateTimeOffset Start { get; }

    public DateTimeOffset? Stop { get; }

    public string Title { get; }

    public string? Subtitle { get; }

    public string? Description { get; }

    public IReadOnlyList<string> Categories { get; }

    private static IReadOnlyList<string> NormalizeCategories(IReadOnlyList<string> categories)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

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

        return normalized;
    }

    private static bool IsTechnicalAlias(string value) =>
        value.StartsWith("alias:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("alias=", StringComparison.OrdinalIgnoreCase);
}
