using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Efiron.Application.ProgrammeGuide;
using Efiron.Domain.ProgrammeGuide;

namespace Efiron.Infrastructure.ProgrammeGuide;

public sealed partial class XmlTvProgrammeGuideParser : IWindowedProgrammeGuideParser
{
    public const int MaximumDecompressedBytes = 256 * 1024 * 1024;

    public ProgrammeGuideDocument Parse(ReadOnlyMemory<byte> content) =>
        Parse(content, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

    public ProgrammeGuideDocument Parse(
        ReadOnlyMemory<byte> content,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        if (content.IsEmpty)
        {
            throw new InvalidDataException("The XMLTV payload is empty.");
        }

        if (windowEnd <= windowStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowEnd),
                "The XMLTV retention window must end after it starts.");
        }

        using var source = new MemoryStream(content.ToArray(), writable: false);
        using var xmlStream = OpenXmlStream(source);
        return ParseXml(xmlStream, windowStart, windowEnd);
    }

    private static ProgrammeGuideDocument ParseXml(
        Stream xmlStream,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var channels = new List<ProgrammeGuideChannel>();
        var programmes = new List<Programme>();
        var warnings = new List<ProgrammeGuideParseWarning>();
        var channelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var settings = new XmlReaderSettings
        {
            CloseInput = false,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumDecompressedBytes,
        };

        using var reader = XmlReader.Create(xmlStream, settings);
        reader.MoveToContent();

        if (reader.NodeType != XmlNodeType.Element ||
            !reader.LocalName.Equals("tv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The document is not an XMLTV television guide.");
        }

        if (reader.IsEmptyElement)
        {
            return ProgrammeGuideDocument.Empty;
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
                        warnings.Add(new ProgrammeGuideParseWarning(
                            $"Duplicate XMLTV channel id '{channel.Id}' was ignored."));
                    }
                }

                continue;
            }

            if (reader.LocalName.Equals("programme", StringComparison.OrdinalIgnoreCase))
            {
                if (IsOutsideWindow(reader, windowStart, windowEnd))
                {
                    reader.Skip();
                    continue;
                }

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
        return new ProgrammeGuideDocument(channels, programmes, warnings);
    }

    private static bool IsOutsideWindow(
        XmlReader reader,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        if (!TryParseTimestamp(reader.GetAttribute("start"), out var start))
        {
            return false;
        }

        if (start >= windowEnd)
        {
            return true;
        }

        var stopValue = reader.GetAttribute("stop");
        return TryParseTimestamp(stopValue, out var stop) && stop <= windowStart;
    }

    private static Stream OpenXmlStream(MemoryStream source)
    {
        var first = source.ReadByte();
        var second = source.ReadByte();
        source.Position = 0;

        return first == 0x1f && second == 0x8b
            ? new GZipStream(source, CompressionMode.Decompress, leaveOpen: false)
            : source;
    }

    private static XElement ReadCurrentElement(XmlReader reader)
    {
        using var subtree = reader.ReadSubtree();
        var element = XElement.Load(subtree, LoadOptions.None);
        reader.Skip();
        return element;
    }

    private static ProgrammeGuideChannel? ParseChannel(
        XElement element,
        ICollection<ProgrammeGuideParseWarning> warnings)
    {
        var id = element.Attribute("id")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            warnings.Add(new ProgrammeGuideParseWarning(
                "An XMLTV channel without an id was ignored."));
            return null;
        }

        var displayNames = element
            .Elements()
            .Where(static child => child.Name.LocalName.Equals(
                "display-name",
                StringComparison.OrdinalIgnoreCase))
            .Select(static child => child.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var iconSource = element
            .Elements()
            .FirstOrDefault(static child => child.Name.LocalName.Equals(
                "icon",
                StringComparison.OrdinalIgnoreCase))?
            .Attribute("src")?
            .Value
            .Trim();

        Uri? iconUri = null;
        if (!string.IsNullOrWhiteSpace(iconSource))
        {
            Uri.TryCreate(iconSource, UriKind.Absolute, out iconUri);
        }

        return new ProgrammeGuideChannel(id, displayNames, iconUri);
    }

    private static Programme? ParseProgramme(
        XElement element,
        ICollection<ProgrammeGuideParseWarning> warnings)
    {
        var channelId = element.Attribute("channel")?.Value.Trim();
        var startValue = element.Attribute("start")?.Value.Trim();

        if (string.IsNullOrWhiteSpace(channelId))
        {
            warnings.Add(new ProgrammeGuideParseWarning(
                "An XMLTV programme without a channel id was ignored."));
            return null;
        }

        if (!TryParseTimestamp(startValue, out var start))
        {
            warnings.Add(new ProgrammeGuideParseWarning(
                $"An XMLTV programme for channel '{channelId}' has an invalid start timestamp and was ignored."));
            return null;
        }

        DateTimeOffset? stop = null;
        var stopValue = element.Attribute("stop")?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(stopValue))
        {
            if (TryParseTimestamp(stopValue, out var parsedStop) && parsedStop >= start)
            {
                stop = parsedStop;
            }
            else
            {
                warnings.Add(new ProgrammeGuideParseWarning(
                    $"An XMLTV programme for channel '{channelId}' has an invalid stop timestamp."));
            }
        }

        var title = FirstValue(element, "title") ?? string.Empty;
        var subtitle = FirstValue(element, "sub-title");
        var description = FirstValue(element, "desc");
        var categories = element
            .Elements()
            .Where(static child => child.Name.LocalName.Equals(
                "category",
                StringComparison.OrdinalIgnoreCase))
            .Select(static child => child.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new Programme(
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
            .Where(child => child.Name.LocalName.Equals(
                localName,
                StringComparison.OrdinalIgnoreCase))
            .Select(static child => child.Value.Trim())
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    internal static bool TryParseTimestamp(
        string? value,
        out DateTimeOffset timestamp)
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

        localDateTime = DateTime.SpecifyKind(
            localDateTime,
            DateTimeKind.Unspecified);
        if (!TryParseOffset(match.Groups["offset"].Value, out var offset))
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
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal);
        if (compact.Length != 5 || (compact[0] != '+' && compact[0] != '-'))
        {
            return false;
        }

        if (!int.TryParse(
                compact.AsSpan(1, 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var hours) ||
            !int.TryParse(
                compact.AsSpan(3, 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minutes) ||
            hours > 14 ||
            minutes > 59)
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
