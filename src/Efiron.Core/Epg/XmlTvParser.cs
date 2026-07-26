using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Efiron.Core.Epg;

public sealed partial class XmlTvParser
{
    public XmlTvDocument Parse(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        if (!xmlStream.CanRead)
        {
            throw new ArgumentException("The XMLTV stream must be readable.", nameof(xmlStream));
        }

        var channels = new List<XmlTvChannel>();
        var programmes = new List<XmlTvProgramme>();
        var warnings = new List<XmlTvParseWarning>();
        var channelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var settings = new XmlReaderSettings
        {
            CloseInput = false,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            XmlResolver = null,
        };

        using var reader = XmlReader.Create(xmlStream, settings);
        reader.MoveToContent();

        if (reader.NodeType != XmlNodeType.Element ||
            !reader.LocalName.Equals("tv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The document is not an XMLTV television guide.");
        }

        if (reader.IsEmptyElement)
        {
            return new XmlTvDocument(channels, programmes, warnings);
        }

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            if (reader.LocalName.Equals("channel", StringComparison.OrdinalIgnoreCase))
            {
                var element = ReadCurrentElement(reader);
                var channel = ParseChannel(element, warnings);
                if (channel is not null)
                {
                    if (channelIds.Add(channel.Id))
                    {
                        channels.Add(channel);
                    }
                    else
                    {
                        warnings.Add(new XmlTvParseWarning(
                            $"Duplicate XMLTV channel id '{channel.Id}' was ignored."));
                    }
                }

                continue;
            }

            if (reader.LocalName.Equals("programme", StringComparison.OrdinalIgnoreCase))
            {
                var element = ReadCurrentElement(reader);
                var programme = ParseProgramme(element, warnings);
                if (programme is not null)
                {
                    programmes.Add(programme);
                }

                continue;
            }

            reader.Skip();
        }

        programmes.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return new XmlTvDocument(channels, programmes, warnings);
    }

    private static XElement ReadCurrentElement(XmlReader reader)
    {
        using var subtree = reader.ReadSubtree();
        var element = XElement.Load(subtree, LoadOptions.None);
        reader.Skip();
        return element;
    }

    private static XmlTvChannel? ParseChannel(
        XElement element,
        ICollection<XmlTvParseWarning> warnings)
    {
        var id = element.Attribute("id")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            warnings.Add(new XmlTvParseWarning("An XMLTV channel without an id was ignored."));
            return null;
        }

        var displayNames = element
            .Elements()
            .Where(static child => child.Name.LocalName.Equals("display-name", StringComparison.OrdinalIgnoreCase))
            .Select(static child => child.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var iconSource = element
            .Elements()
            .FirstOrDefault(static child => child.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))?
            .Attribute("src")?
            .Value
            .Trim();

        Uri? iconUri = null;
        if (!string.IsNullOrWhiteSpace(iconSource))
        {
            Uri.TryCreate(iconSource, UriKind.Absolute, out iconUri);
        }

        return new XmlTvChannel(id, displayNames, iconUri);
    }

    private static XmlTvProgramme? ParseProgramme(
        XElement element,
        ICollection<XmlTvParseWarning> warnings)
    {
        var channelId = element.Attribute("channel")?.Value.Trim();
        var startValue = element.Attribute("start")?.Value.Trim();

        if (string.IsNullOrWhiteSpace(channelId))
        {
            warnings.Add(new XmlTvParseWarning("An XMLTV programme without a channel id was ignored."));
            return null;
        }

        if (!TryParseTimestamp(startValue, out var start))
        {
            warnings.Add(new XmlTvParseWarning(
                $"An XMLTV programme for channel '{channelId}' has an invalid start timestamp and was ignored."));
            return null;
        }

        DateTimeOffset? stop = null;
        var stopValue = element.Attribute("stop")?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(stopValue))
        {
            if (TryParseTimestamp(stopValue, out var parsedStop))
            {
                stop = parsedStop;
            }
            else
            {
                warnings.Add(new XmlTvParseWarning(
                    $"An XMLTV programme for channel '{channelId}' has an invalid stop timestamp."));
            }
        }

        var title = FirstValue(element, "title") ?? string.Empty;
        var subtitle = FirstValue(element, "sub-title");
        var description = FirstValue(element, "desc");
        var categories = element
            .Elements()
            .Where(static child => child.Name.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase))
            .Select(static child => child.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new XmlTvProgramme(
            channelId,
            start,
            stop,
            title,
            subtitle,
            description,
            categories);
    }

    private static string? FirstValue(XElement element, string localName) =>
        element
            .Elements()
            .Where(child => child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            .Select(static child => child.Value.Trim())
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    internal static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = TimestampPattern().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var dateText = match.Groups["date"].Value;
        var dateFormat = dateText.Length switch
        {
            8 => "yyyyMMdd",
            10 => "yyyyMMddHH",
            12 => "yyyyMMddHHmm",
            14 => "yyyyMMddHHmmss",
            _ => null,
        };

        if (dateFormat is null ||
            !DateTime.TryParseExact(
                dateText,
                dateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localDateTime))
        {
            return false;
        }

        localDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        var offsetText = match.Groups["offset"].Value;
        if (!TryParseOffset(offsetText, out var offset))
        {
            return false;
        }

        try
        {
            timestamp = new DateTimeOffset(localDateTime, offset);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseOffset(string value, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal);
        if (compact.Length != 5 || (compact[0] != '+' && compact[0] != '-'))
        {
            return false;
        }

        if (!int.TryParse(compact.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(compact.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            hours > 14 || minutes > 59)
        {
            return false;
        }

        offset = new TimeSpan(hours, minutes, 0);
        if (compact[0] == '-')
        {
            offset = -offset;
        }

        return true;
    }

    [GeneratedRegex(
        "^(?<date>\\d{8}(?:\\d{2}){0,3})(?:\\s*(?<offset>Z|[+-]\\d{2}:?\\d{2}))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();
}
