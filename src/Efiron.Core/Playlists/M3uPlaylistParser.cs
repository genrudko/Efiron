using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Efiron.Core.Playlists;

public sealed class M3uPlaylistParser
{
    private static readonly Regex AttributePattern = new(
        @"(?<key>[A-Za-z0-9_-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s,]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public PlaylistParseResult Parse(string content, Uri? playlistUri = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = NormalizeLines(content);
        if (lines.Any(static line => line.TrimStart().StartsWith("#EXT-X-", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The supplied document is an HLS media manifest, not an IPTV channel playlist.");
        }

        var channels = new List<PlaylistChannel>();
        var warnings = new List<PlaylistParseWarning>();
        var headerAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var identityOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        PendingEntry? pending = null;
        var headerSeen = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                headerSeen = true;
                MergeAttributes(headerAttributes, ParseAttributes(line["#EXTM3U".Length..]));
                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                if (pending is not null)
                {
                    warnings.Add(new PlaylistParseWarning(
                        pending.LineNumber,
                        "The channel entry has no stream URI and was skipped."));
                }

                pending = ParseExtInf(line, lineNumber);
                continue;
            }

            if (line.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
            {
                if (pending is not null)
                {
                    pending.ExplicitGroup = line["#EXTGRP:".Length..].Trim();
                }

                continue;
            }

            if (line.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase))
            {
                if (pending is not null)
                {
                    AddDirective(pending.Directives, "extvlcopt", line["#EXTVLCOPT:".Length..]);
                }

                continue;
            }

            if (line.StartsWith("#KODIPROP:", StringComparison.OrdinalIgnoreCase))
            {
                if (pending is not null)
                {
                    AddDirective(pending.Directives, "kodiprop", line["#KODIPROP:".Length..]);
                }

                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            if (pending is null)
            {
                warnings.Add(new PlaylistParseWarning(
                    lineNumber,
                    "A stream URI without a preceding #EXTINF entry was skipped."));
                continue;
            }

            if (!TryResolveStreamUri(line, playlistUri, out var streamUri, out var inlineOptions))
            {
                warnings.Add(new PlaylistParseWarning(
                    lineNumber,
                    "The channel stream URI is invalid and the entry was skipped."));
                pending = null;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(inlineOptions))
            {
                pending.Directives["url-options"] = inlineOptions;
            }

            var tvgId = NullIfWhiteSpace(GetAttribute(pending.Attributes, "tvg-id"));
            var tvgName = NullIfWhiteSpace(GetAttribute(pending.Attributes, "tvg-name"));
            var name = NullIfWhiteSpace(pending.DisplayName) ?? tvgName ?? $"Channel {channels.Count + 1}";
            var groupName = NullIfWhiteSpace(pending.ExplicitGroup) ??
                NullIfWhiteSpace(GetAttribute(pending.Attributes, "group-title"));
            var logoUri = ResolveOptionalUri(
                NullIfWhiteSpace(GetAttribute(pending.Attributes, "tvg-logo")),
                playlistUri);
            var stableId = CreateStableId(
                tvgId,
                tvgName,
                name,
                groupName,
                streamUri!,
                identityOccurrences);

            channels.Add(new PlaylistChannel(
                stableId,
                name,
                streamUri!,
                tvgId,
                tvgName,
                logoUri,
                groupName,
                new Dictionary<string, string>(pending.Attributes, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(pending.Directives, StringComparer.OrdinalIgnoreCase),
                pending.LineNumber));

            pending = null;
        }

        if (pending is not null)
        {
            warnings.Add(new PlaylistParseWarning(
                pending.LineNumber,
                "The channel entry has no stream URI and was skipped."));
        }

        if (!headerSeen)
        {
            warnings.Insert(0, new PlaylistParseWarning(
                1,
                "The #EXTM3U header is missing; compatible entries were parsed anyway."));
        }

        return new PlaylistParseResult(
            channels,
            new Dictionary<string, string>(headerAttributes, StringComparer.OrdinalIgnoreCase),
            warnings);
    }

    private static string[] NormalizeLines(string content)
    {
        var normalized = content.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return normalized.Split('\n');
    }

    private static PendingEntry ParseExtInf(string line, int lineNumber)
    {
        var payload = line["#EXTINF:".Length..].Trim();
        var commaIndex = FindUnquotedComma(payload);
        var metadata = commaIndex >= 0 ? payload[..commaIndex] : payload;
        var displayName = commaIndex >= 0 ? payload[(commaIndex + 1)..].Trim() : string.Empty;

        return new PendingEntry(
            lineNumber,
            displayName,
            ParseAttributes(metadata));
    }

    private static int FindUnquotedComma(string value)
    {
        var quote = '\0';
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote == '\0' && (character == '"' || character == '\''))
            {
                quote = character;
                continue;
            }

            if (character == quote)
            {
                quote = '\0';
                continue;
            }

            if (character == ',' && quote == '\0')
            {
                return index;
            }
        }

        return -1;
    }

    private static Dictionary<string, string> ParseAttributes(string value)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributePattern.Matches(value))
        {
            var key = match.Groups["key"].Value;
            var attributeValue = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["bare"].Value;
            attributes[key] = attributeValue;
        }

        return attributes;
    }

    private static void MergeAttributes(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void AddDirective(
        IDictionary<string, string> directives,
        string prefix,
        string payload)
    {
        var separatorIndex = payload.IndexOf('=');
        if (separatorIndex < 0)
        {
            directives[$"{prefix}:value"] = payload.Trim();
            return;
        }

        var key = payload[..separatorIndex].Trim();
        var value = payload[(separatorIndex + 1)..].Trim();
        directives[$"{prefix}:{key}"] = value;
    }

    private static bool TryResolveStreamUri(
        string sourceLine,
        Uri? playlistUri,
        out Uri? streamUri,
        out string? inlineOptions)
    {
        var optionSeparator = sourceLine.IndexOf('|');
        var uriText = optionSeparator >= 0 ? sourceLine[..optionSeparator].Trim() : sourceLine.Trim();
        inlineOptions = optionSeparator >= 0 ? sourceLine[(optionSeparator + 1)..].Trim() : null;

        if (Uri.TryCreate(uriText, UriKind.Absolute, out streamUri))
        {
            return true;
        }

        return playlistUri is not null && Uri.TryCreate(playlistUri, uriText, out streamUri);
    }

    private static Uri? ResolveOptionalUri(string? value, Uri? playlistUri)
    {
        if (value is null)
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        return playlistUri is not null && Uri.TryCreate(playlistUri, value, out var relativeUri)
            ? relativeUri
            : null;
    }

    private static string? GetAttribute(IReadOnlyDictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) ? value : null;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateStableId(
        string? tvgId,
        string? tvgName,
        string displayName,
        string? groupName,
        Uri streamUri,
        IDictionary<string, int> identityOccurrences)
    {
        var identity = tvgId is not null
            ? $"tvg-id:{NormalizeIdentityPart(tvgId)}"
            : tvgName is not null
                ? $"tvg-name:{NormalizeIdentityPart(tvgName)}|group:{NormalizeIdentityPart(groupName)}"
                : $"name:{NormalizeIdentityPart(displayName)}|group:{NormalizeIdentityPart(groupName)}";

        identityOccurrences.TryGetValue(identity, out var occurrence);
        identityOccurrences[identity] = occurrence + 1;

        var seed = occurrence == 0
            ? identity
            : $"{identity}|stream:{NormalizeIdentityPart(streamUri.AbsoluteUri)}|duplicate:{occurrence + 1}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return $"m3u:{Convert.ToHexString(hash).ToLowerInvariant()[..24]}";
    }

    private static string NormalizeIdentityPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', value.Trim().Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        return normalized.ToUpperInvariant();
    }

    private sealed class PendingEntry(
        int lineNumber,
        string displayName,
        Dictionary<string, string> attributes)
    {
        public int LineNumber { get; } = lineNumber;

        public string DisplayName { get; } = displayName;

        public Dictionary<string, string> Attributes { get; } = attributes;

        public Dictionary<string, string> Directives { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? ExplicitGroup { get; set; }
    }
}
