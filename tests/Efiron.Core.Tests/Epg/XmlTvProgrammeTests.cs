using Efiron.Core.Epg;

namespace Efiron.Core.Tests.Epg;

public sealed class XmlTvProgrammeTests
{
    [Fact]
    public void Constructor_PreservesUserCategoriesAndRemovesProviderAliases()
    {
        var programme = CreateProgramme(
            "[Новости,alias:Novosti]",
            "[Фильмы, alias=Filmy]",
            "Спорт");

        Assert.Equal(["Новости", "Фильмы", "Спорт"], programme.Categories);
    }

    [Fact]
    public void Constructor_DoesNotSplitOrdinaryCategoryContainingComma()
    {
        var programme = CreateProgramme("Фильмы, сериалы");

        Assert.Equal(["Фильмы, сериалы"], programme.Categories);
    }

    [Fact]
    public void Constructor_RemovesStandaloneAliasesAndDuplicateCategories()
    {
        var programme = CreateProgramme(
            "alias:News",
            "Новости",
            "новости",
            " alias=Movies ",
            "");

        Assert.Equal(["Новости"], programme.Categories);
    }

    private static XmlTvProgramme CreateProgramme(params string[] categories) =>
        new(
            "channel-1",
            new DateTimeOffset(2026, 7, 26, 18, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 7, 26, 19, 0, 0, TimeSpan.FromHours(3)),
            "Programme",
            null,
            null,
            categories);
}
