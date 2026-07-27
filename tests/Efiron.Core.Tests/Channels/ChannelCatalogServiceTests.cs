using Efiron.Core.Channels;
using Efiron.Core.Playlists;
using Xunit;

namespace Efiron.Core.Tests.Channels;

public sealed class ChannelCatalogServiceTests
{
    private readonly ChannelCatalogService _service = new();

    [Fact]
    public void Build_AppliesOverridesWithoutMutatingProviderChannel()
    {
        var provider = Channel("one", "Provider name", "News");
        var snapshot = Snapshot(
            ChannelNumberingMode.Continuous,
            Override(provider, customName: "My name", customCategory: "Pinned"));

        var result = Assert.Single(_service.Build([provider], snapshot));

        Assert.Equal("My name", result.DisplayName);
        Assert.Equal("Pinned", result.CategoryName);
        Assert.Equal("Provider name", provider.Name);
        Assert.Equal("News", provider.GroupName);
    }

    [Fact]
    public void Build_ContinuousNumberingSkipsHiddenByDefault()
    {
        var first = Channel("one", "One", "A");
        var hidden = Channel("two", "Two", "A");
        var third = Channel("three", "Three", "A");
        var snapshot = Snapshot(
            ChannelNumberingMode.Continuous,
            Override(hidden, isHidden: true));

        var result = _service.Build([first, hidden, third], snapshot);

        Assert.Equal(1, result[0].Number);
        Assert.Null(result[1].Number);
        Assert.Equal(2, result[2].Number);
    }

    [Fact]
    public void Build_CanIncludeHiddenChannelsInContinuousNumbering()
    {
        var first = Channel("one", "One", "A");
        var hidden = Channel("two", "Two", "A");
        var snapshot = Snapshot(
            ChannelNumberingMode.Continuous,
            Override(hidden, isHidden: true)) with
        {
            Settings = new ChannelLibrarySettings(
                ChannelNumberingMode.Continuous,
                IncludeHiddenInNumbering: true,
                FavoritesUseIndependentNumbering: true),
        };

        var result = _service.Build([first, hidden], snapshot);

        Assert.Equal(1, result[0].Number);
        Assert.Equal(2, result[1].Number);
    }

    [Fact]
    public void Build_PerCategoryNumberingRestartsForEffectiveCategory()
    {
        var first = Channel("one", "One", "A");
        var moved = Channel("two", "Two", "A");
        var third = Channel("three", "Three", "B");
        var snapshot = Snapshot(
            ChannelNumberingMode.PerCategory,
            Override(moved, customCategory: "B"));

        var result = _service.Build([first, moved, third], snapshot);

        Assert.Equal(1, result.Single(item => item.ProviderChannel.StableId == first.StableId).Number);
        Assert.Equal(1, result.Single(item => item.ProviderChannel.StableId == moved.StableId).Number);
        Assert.Equal(2, result.Single(item => item.ProviderChannel.StableId == third.StableId).Number);
    }

    [Fact]
    public void Build_ManualModeDoesNotInventOrOverwriteNumbers()
    {
        var first = Channel("one", "One", "A");
        var second = Channel("two", "Two", "A");
        var snapshot = Snapshot(
            ChannelNumberingMode.Manual,
            Override(second, manualNumber: 77));

        var result = _service.Build([first, second], snapshot);

        Assert.Null(result[0].Number);
        Assert.Equal(77, result[1].Number);
    }

    [Fact]
    public void BuildFavorites_UsesPersistentFavoriteOrderAndIndependentNumbers()
    {
        var first = Channel("one", "One", "A");
        var second = Channel("two", "Two", "A");
        var third = Channel("three", "Three", "A");
        var snapshot = Snapshot(
            ChannelNumberingMode.ProviderOrder,
            Override(first, isFavorite: true, favoriteOrder: 20),
            Override(third, isFavorite: true, favoriteOrder: 10));

        var result = _service.BuildFavorites([first, second, third], snapshot);

        Assert.Equal([third.StableId, first.StableId], result.Select(item => item.ProviderChannel.StableId));
        Assert.Equal([1, 2], result.Select(item => item.Number));
    }

    [Fact]
    public void Build_ReusesOverridesAfterProviderRefreshWhenStableIdIsUnchanged()
    {
        var oldChannel = Channel("stable", "Old provider name", "Old group");
        var refreshed = oldChannel with
        {
            Name = "New provider name",
            GroupName = "New group",
            StreamUri = new Uri("https://example.test/new-stream.m3u8"),
        };
        var snapshot = Snapshot(
            ChannelNumberingMode.Manual,
            Override(oldChannel, customName: "My channel", manualNumber: 9, isFavorite: true));

        var result = Assert.Single(_service.Build([refreshed], snapshot));

        Assert.Equal("My channel", result.DisplayName);
        Assert.Equal(9, result.Number);
        Assert.True(result.IsFavorite);
        Assert.Equal(refreshed.StreamUri, result.ProviderChannel.StreamUri);
    }

    [Fact]
    public void Normalize_PreservesOrphanOverridesButDropsEmptyEntries()
    {
        var orphan = new ChannelUserOverride(
            "missing",
            "Saved name",
            null,
            false,
            null,
            false,
            null,
            null);
        var empty = new ChannelUserOverride(
            "empty",
            null,
            null,
            false,
            null,
            false,
            null,
            null);
        var snapshot = new ChannelLibrarySnapshot(
            ChannelLibrarySnapshot.CurrentVersion,
            ChannelLibrarySettings.Default,
            new Dictionary<string, ChannelUserOverride>
            {
                [orphan.StableId] = orphan,
                [empty.StableId] = empty,
            });

        var normalized = snapshot.Normalize();

        Assert.Single(normalized.Overrides);
        Assert.Equal("Saved name", normalized.Overrides["missing"].CustomName);
    }

    private static ChannelLibrarySnapshot Snapshot(
        ChannelNumberingMode numberingMode,
        params ChannelUserOverride[] overrides) =>
        new(
            ChannelLibrarySnapshot.CurrentVersion,
            new ChannelLibrarySettings(
                numberingMode,
                IncludeHiddenInNumbering: false,
                FavoritesUseIndependentNumbering: true),
            overrides.ToDictionary(item => item.StableId, StringComparer.Ordinal));

    private static ChannelUserOverride Override(
        PlaylistChannel channel,
        string? customName = null,
        int? manualNumber = null,
        bool isFavorite = false,
        int? favoriteOrder = null,
        bool isHidden = false,
        string? customCategory = null) =>
        new(
            channel.StableId,
            customName,
            manualNumber,
            isFavorite,
            favoriteOrder,
            isHidden,
            customCategory,
            null);

    private static PlaylistChannel Channel(string stableId, string name, string? group) =>
        new(
            stableId,
            name,
            new Uri($"https://example.test/{stableId}.m3u8"),
            stableId,
            name,
            null,
            group,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            1);
}
