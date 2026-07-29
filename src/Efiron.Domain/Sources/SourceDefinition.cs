namespace Efiron.Domain.Sources;

public sealed record SourceDefinition
{
    private SourceDefinition(SourceKind kind, string location, bool isEnabled)
    {
        Kind = kind;
        Location = location;
        IsEnabled = isEnabled;
    }

    public SourceKind Kind { get; }

    public string Location { get; }

    public bool IsEnabled { get; }

    public static SourceDefinition Create(
        SourceKind kind,
        string location,
        bool isEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        return new SourceDefinition(
            kind,
            location.Trim(),
            isEnabled);
    }
}
