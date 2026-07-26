using Efiron.Core.Epg;
using Xunit;

namespace Efiron.Core.Tests.Epg;

public sealed class XmlTvProgrammeTests
{
    [Fact]
    public void Constructor_PreservesUserCategoriesAndRemovesProviderAliases()
    {
        var programme = CreateProgramme(
            null,
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
    public void Constructor_ExtractsProviderCategoryPrefixFromDescription()
    {
        var programme = CreateProgramme(
            "[Познавательные передачи,alias:PoznavatelnyePeredachi] В передаче Никита Михалков размышляет о человеке.");

        Assert.Equal("В передаче Никита Михалков размышляет о человеке.", programme.Description);
        var category = Assert.Single(programme.Categories);
        Assert.Equal("Познавательные передачи", category);
    }

    [Fact]
    public void Constructor_PreservesOrdinaryBracketedDescriptionPrefix()
    {
        var programme = CreateProgramme("[16+] Художественный фильм.");

        Assert.Equal("[16+] Художественный фильм.", programme.Description);
        Assert.Empty(programme.Categories);
    }

    [Fact]
    public void Constructor_DoesNotSplitOrdinaryCategoryContainingComma()
    {
        var programme = CreateProgramme(null, "Фильмы, сериалы");

        var category = Assert.Single(programme.Categories);
        Assert.Equal("Фильмы, сериалы", category);
    }

    [Fact]
    public void Constructor_RemovesStandaloneAliasesAndDuplicateCategories()
    {
        var programme = CreateProgramme(
            null,
            "alias:News",
            "Новости",
            "новости",
            " alias=Movies ",
            "");

        var category = Assert.Single(programme.Categories);
        Assert.Equal("Новости", category);
    }

    private static XmlTvProgramme CreateProgramme(string? description, params string[] categories) =>
        new(
            "channel-1",
            new DateTimeOffset(2026, 7, 26, 18, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 7, 26, 19, 0, 0, TimeSpan.FromHours(3)),
            "Programme",
            null,
            description,
            categories);
}
