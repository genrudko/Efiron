namespace Efiron.App.Presentation;

internal sealed record LiveCategoryItem(
    string Name,
    string? FilterTag,
    string Glyph,
    string? CountText = null);
