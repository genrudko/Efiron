using Efiron.Core.Epg;
using Xunit;

namespace Efiron.Core.Tests.Epg;

public sealed class EpgScheduleIndexTests
{
    [Fact]
    public void Find_ReturnsCurrentNextAndProgress()
    {
        var first = Programme(10, 0, 11, 0, "Current");
        var second = Programme(11, 0, 12, 0, "Next");
        var index = new EpgScheduleIndex([second, first]);

        var result = index.Find("channel-1", At(10, 30));

        Assert.Same(first, result.Current);
        Assert.Same(second, result.Next);
        Assert.Equal(first.Stop, result.EffectiveCurrentStop);
        Assert.True(result.IsProgressKnown);
        Assert.Equal(50, result.ProgressPercent, precision: 6);
    }

    [Fact]
    public void Find_ReturnsUpcomingProgrammeDuringGap()
    {
        var first = Programme(10, 0, 11, 0, "Morning");
        var second = Programme(12, 0, 13, 0, "Noon");
        var index = new EpgScheduleIndex([first, second]);

        var result = index.Find("channel-1", At(11, 30));

        Assert.Null(result.Current);
        Assert.Same(second, result.Next);
        Assert.False(result.IsProgressKnown);
        Assert.Equal(0, result.ProgressPercent);
    }

    [Fact]
    public void Find_UsesNextStartAsEffectiveStopForOpenEndedProgramme()
    {
        var first = Programme(10, 0, null, null, "Open ended");
        var second = Programme(11, 0, 12, 0, "Next");
        var index = new EpgScheduleIndex([first, second]);

        var result = index.Find("CHANNEL-1", At(10, 30));

        Assert.Same(first, result.Current);
        Assert.Same(second, result.Next);
        Assert.Equal(second.Start, result.EffectiveCurrentStop);
        Assert.True(result.IsProgressKnown);
        Assert.Equal(50, result.ProgressPercent, precision: 6);
    }

    [Fact]
    public void Find_KeepsLastOpenEndedProgrammeCurrentWithUnknownProgress()
    {
        var programme = Programme(10, 0, null, null, "Open ended");
        var index = new EpgScheduleIndex([programme]);

        var result = index.Find("channel-1", At(15, 0));

        Assert.Same(programme, result.Current);
        Assert.Null(result.Next);
        Assert.Null(result.EffectiveCurrentStop);
        Assert.False(result.IsProgressKnown);
        Assert.Equal(0, result.ProgressPercent);
    }

    [Fact]
    public void Find_ReturnsFirstProgrammeBeforeScheduleStarts()
    {
        var programme = Programme(10, 0, 11, 0, "First");
        var index = new EpgScheduleIndex([programme]);

        var result = index.Find("channel-1", At(9, 30));

        Assert.Null(result.Current);
        Assert.Same(programme, result.Next);
    }

    [Fact]
    public void Find_ReturnsEmptyResultForUnknownChannel()
    {
        var index = new EpgScheduleIndex([Programme(10, 0, 11, 0, "First")]);

        var result = index.Find("missing", At(10, 30));

        Assert.Null(result.Current);
        Assert.Null(result.Next);
        Assert.False(result.IsProgressKnown);
    }

    private static XmlTvProgramme Programme(
        int startHour,
        int startMinute,
        int? stopHour,
        int? stopMinute,
        string title) =>
        new(
            "channel-1",
            At(startHour, startMinute),
            stopHour is null || stopMinute is null ? null : At(stopHour.Value, stopMinute.Value),
            title,
            null,
            null,
            []);

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 7, 26, hour, minute, 0, TimeSpan.FromHours(3));
}
