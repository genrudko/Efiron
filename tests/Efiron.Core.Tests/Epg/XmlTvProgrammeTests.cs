using Efiron.Core.Epg;
using Xunit;

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

        Assert.Collection(
            programme.Categories,
            category => Assert.Equal("Новости", category),
            category => Assert.Equal("Фильмы", category),
            category => Assert.Equal("Спорт", category));
    }

    [Fact]
    public void Constructor_DoesNotSplitOrdinaryCategoryContainingComma()
    {
        var programme = CreateProgramme("Фильмы, сериалы");

        var category = Assert.Single(programme.Categories);
        Assert.Equal("Фильмы, сериалы", category);
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

        var category = Assert.Single(programme.Categories);
        Assert.Equal("Новости", category);
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
