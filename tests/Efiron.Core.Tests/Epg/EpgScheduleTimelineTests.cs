using Efiron.Core.Epg;
using Xunit;

namespace Efiron.Core.Tests.Epg;

public sealed class EpgScheduleTimelineTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 7, 26, 18, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void FindRange_ReturnsAndClipsOverlappingProgrammes()
    {
        var index = CreateIndex(
            Programme("channel-1", -60, 90, "Before"),
            Programme("channel-1", 90, 300, "After"));

        var result = index.FindRange(
            "channel-1",
            BaseTime,
            BaseTime.AddHours(3));

        Assert.Collection(
            result,
            entry =>
            {
                Assert.Equal("Before", entry.Programme.Title);
                Assert.Equal(BaseTime, entry.VisibleStart);
                Assert.Equal(BaseTime.AddMinutes(90), entry.VisibleStop);
                Assert.True(entry.StartsBeforeWindow);
                Assert.False(entry.EndsAfterWindow);
            },
            entry =>
            {
                Assert.Equal("After", entry.Programme.Title);
                Assert.Equal(BaseTime.AddMinutes(90), entry.VisibleStart);
                Assert.Equal(BaseTime.AddHours(3), entry.VisibleStop);
                Assert.False(entry.StartsBeforeWindow);
                Assert.True(entry.EndsAfterWindow);
            });
    }

    [Fact]
    public void FindRange_UsesNextStartForOpenEndedProgramme()
    {
        var first = new XmlTvProgramme(
            "channel-1",
            BaseTime,
            null,
            "Open",
            null,
            null,
            Array.Empty<string>());
        var second = Programme("channel-1", 120, 180, "Next");
        var index = CreateIndex(first, second);

        var result = index.FindRange(
            "channel-1",
            BaseTime.AddMinutes(30),
            BaseTime.AddMinutes(150));

        Assert.Equal(2, result.Count);
        Assert.Equal(BaseTime.AddMinutes(120), result[0].EffectiveStop);
        Assert.Equal(BaseTime.AddMinutes(120), result[0].VisibleStop);
    }

    [Fact]
    public void FindRange_ExtendsLastOpenEndedProgrammeToWindowEnd()
    {
        var programme = new XmlTvProgramme(
            "channel-1",
            BaseTime,
            null,
            "Open",
            null,
            null,
            Array.Empty<string>());
        var index = CreateIndex(programme);
        var windowEnd = BaseTime.AddHours(4);

        var result = Assert.Single(index.FindRange("CHANNEL-1", BaseTime.AddHours(1), windowEnd));

        Assert.Equal(windowEnd, result.EffectiveStop);
        Assert.Equal(windowEnd, result.VisibleStop);
        Assert.True(result.StartsBeforeWindow);
        Assert.False(result.EndsAfterWindow);
    }

    [Fact]
    public void FindRange_ExcludesProgrammesOutsideWindowAndPreservesGaps()
    {
        var index = CreateIndex(
            Programme("channel-1", -180, -120, "Old"),
            Programme("channel-1", 120, 180, "Upcoming"),
            Programme("channel-1", 360, 420, "Later"));

        var result = index.FindRange(
            "channel-1",
            BaseTime,
            BaseTime.AddHours(4));

        var entry = Assert.Single(result);
        Assert.Equal("Upcoming", entry.Programme.Title);
        Assert.Equal(BaseTime.AddMinutes(120), entry.VisibleStart);
    }

    [Fact]
    public void FindRange_ReturnsEmptyForUnknownChannel()
    {
        var index = CreateIndex(Programme("channel-1", 0, 60, "One"));

        Assert.Empty(index.FindRange("unknown", BaseTime, BaseTime.AddHours(1)));
    }

    [Fact]
    public void FindRange_RejectsEmptyOrReversedWindow()
    {
        var index = CreateIndex(Programme("channel-1", 0, 60, "One"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.FindRange("channel-1", BaseTime, BaseTime));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.FindRange("channel-1", BaseTime, BaseTime.AddMinutes(-1)));
    }

    private static EpgScheduleIndex CreateIndex(params XmlTvProgramme[] programmes) =>
        new(programmes);

    private static XmlTvProgramme Programme(
        string channelId,
        int startMinutes,
        int stopMinutes,
        string title) =>
        new(
            channelId,
            BaseTime.AddMinutes(startMinutes),
            BaseTime.AddMinutes(stopMinutes),
            title,
            null,
            null,
            Array.Empty<string>());
}
