using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Efiron.Application.Live;

namespace Efiron.Desktop.Presentation;

public sealed class LiveChannelItem : INotifyPropertyChanged
{
    private bool _isFavorite;
    private bool _isPlaying;

    public LiveChannelItem(
        int number,
        LiveChannelSnapshot snapshot,
        bool isFavorite,
        DateTimeOffset now,
        string noProgrammeText,
        string nextFormat)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Number = number;
        Snapshot = snapshot;
        Name = snapshot.Channel.Name;
        Initials = CreateInitials(Name);
        LogoUrl = snapshot.Channel.LogoUri?.ToString();
        Category = snapshot.Channel.Category ?? string.Empty;
        CurrentProgramme = snapshot.CurrentProgramme?.Title ?? noProgrammeText;
        CurrentDescription = snapshot.CurrentProgramme?.Description ?? string.Empty;
        CurrentTime = FormatTimeRange(
            snapshot.CurrentProgramme?.Start,
            snapshot.CurrentProgramme?.Stop);
        NextProgramme = snapshot.NextProgramme is null
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                nextFormat,
                snapshot.NextProgramme.Start.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture),
                snapshot.NextProgramme.Title);
        NextProgrammeTitle = snapshot.NextProgramme?.Title ?? noProgrammeText;
        NextDescription = snapshot.NextProgramme?.Description ?? string.Empty;
        NextTime = FormatTimeRange(
            snapshot.NextProgramme?.Start,
            snapshot.NextProgramme?.Stop);
        Progress = CalculateProgress(snapshot, now);
        _isFavorite = isFavorite;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number { get; }

    public LiveChannelSnapshot Snapshot { get; }

    public string Name { get; }

    public string Initials { get; }

    public string? LogoUrl { get; }

    public string Category { get; }

    public string CurrentProgramme { get; }

    public string CurrentDescription { get; }

    public string CurrentTime { get; }

    public string NextProgramme { get; }

    public string NextProgrammeTitle { get; }

    public string NextDescription { get; }

    public string NextTime { get; }

    public double Progress { get; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayingIndicator));
        }
    }

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string PlayingIndicator => IsPlaying ? "▮▮▮" : string.Empty;

    private static string CreateInitials(string name)
    {
        var words = name.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "TV";
        }

        return string.Concat(words
            .Take(2)
            .Select(static word => char.ToUpperInvariant(word[0])));
    }

    private static string FormatTimeRange(
        DateTimeOffset? start,
        DateTimeOffset? stop)
    {
        if (start is null)
        {
            return string.Empty;
        }

        var startText = start.Value.ToLocalTime().ToString(
            "HH:mm",
            CultureInfo.CurrentCulture);
        if (stop is null)
        {
            return startText;
        }

        return $"{startText}–{stop.Value.ToLocalTime():HH:mm}";
    }

    private static double CalculateProgress(
        LiveChannelSnapshot snapshot,
        DateTimeOffset now)
    {
        var current = snapshot.CurrentProgramme;
        if (current?.Stop is null || current.Stop <= current.Start)
        {
            return 0;
        }

        var elapsed = now - current.Start;
        var duration = current.Stop.Value - current.Start;
        return Math.Clamp(
            elapsed.TotalMilliseconds / duration.TotalMilliseconds * 100,
            0,
            100);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}