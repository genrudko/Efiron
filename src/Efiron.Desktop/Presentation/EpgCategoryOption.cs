namespace Efiron.Desktop.Presentation;

public sealed record EpgCategoryOption(string Label, string? Value)
{
    public override string ToString() => Label;
}
