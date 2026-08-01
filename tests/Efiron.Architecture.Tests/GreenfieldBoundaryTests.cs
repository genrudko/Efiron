using System.Xml.Linq;
using Xunit;

namespace Efiron.Architecture.Tests;

public sealed class GreenfieldBoundaryTests
{
    private const string LegacyProjectFileName = "Efiron.App.csproj";

    private static readonly string[] GreenfieldProjectNames =
    [
        "Efiron.Domain",
        "Efiron.Application",
        "Efiron.Infrastructure",
        "Efiron.Playback",
        "Efiron.Desktop",
    ];

    private static readonly string[] BannedSourceTokens =
    [
        "using Efiron.App;",
        "using Efiron.App.",
        "namespace Efiron.App;",
        "namespace Efiron.App.",
        "CompatibilityBridge",
        "LoadPlaylistButton_Click",
        "LoadEpgButton_Click",
    ];

    [Fact]
    public void Greenfield_projects_must_not_reference_legacy_app()
    {
        var repositoryRoot = FindRepositoryRoot();

        foreach (var projectPath in EnumerateExistingGreenfieldProjects(repositoryRoot))
        {
            var document = XDocument.Load(projectPath);
            var references = document
                .Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(static include => !string.IsNullOrWhiteSpace(include))
                .ToArray();

            Assert.DoesNotContain(
                references,
                include => string.Equals(
                    Path.GetFileName(include),
                    LegacyProjectFileName,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Greenfield_source_must_not_contain_legacy_bridge_tokens()
    {
        var repositoryRoot = FindRepositoryRoot();

        foreach (var projectPath in EnumerateExistingGreenfieldProjects(repositoryRoot))
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var sourceFiles = Directory.EnumerateFiles(
                projectDirectory,
                "*.*",
                SearchOption.AllDirectories)
                .Where(static path =>
                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

            foreach (var sourceFile in sourceFiles)
            {
                var content = File.ReadAllText(sourceFile);
                foreach (var bannedToken in BannedSourceTokens)
                {
                    Assert.DoesNotContain(
                        bannedToken,
                        content,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void Application_project_may_reference_only_domain()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Efiron.Application",
            "Efiron.Application.csproj");
        var document = XDocument.Load(projectPath);
        var references = document
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                (string?)element.Attribute("Include")))
            .ToArray();

        Assert.Equal(new[] { "Efiron.Domain" }, references);
    }

    [Fact]
    public void Desktop_presentation_must_preserve_approved_poster_composition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var desktopDirectory = Path.Combine(repositoryRoot, "src", "Efiron.Desktop");
        var shell = File.ReadAllText(Path.Combine(desktopDirectory, "MainWindow.xaml"));
        var lazyWorkspaces = File.ReadAllText(Path.Combine(
            desktopDirectory,
            "MainWindow.LazyWorkspaces.cs"));
        var appearance = File.ReadAllText(Path.Combine(
            desktopDirectory,
            "MainWindow.Appearance.cs"));
        var live = File.ReadAllText(Path.Combine(
            desktopDirectory,
            "Views",
            "LiveTvView.xaml"));
        var presentationPolish = File.ReadAllText(Path.Combine(
            desktopDirectory,
            "Views",
            "LiveTvView.PresentationPolish.cs"));
        var programme = File.ReadAllText(Path.Combine(
            desktopDirectory,
            "Views",
            "ProgrammeGuideView.xaml"));
        var programmeRenderer = File.ReadAllText(Path.Combine(
            desktopDirectory,
            "Views",
            "ProgrammeGuideView.VirtualizedSurface.cs"));
        var programmeProjection = File.ReadAllText(Path.Combine(
            desktopDirectory,
            "Views",
            "ProgrammeGuideView.ProgressiveLoad.cs"));
        var channelPresentation = File.ReadAllText(Path.Combine(
            desktopDirectory,
            "Presentation",
            "LiveChannelItem.cs"));
        var channelSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Efiron.Application",
            "Live",
            "LiveChannelSnapshot.cs"));

        Assert.Contains("x:Name=\"AppNavigationRail\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveNavigationButton\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProgrammeNavigationButton\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsNavigationButton\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveTvWorkspaceHost\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProgrammeGuideWorkspaceHost\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<views:LiveTvView", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<views:ProgrammeGuideView", shell, StringComparison.Ordinal);
        Assert.Contains("new LiveTvView", lazyWorkspaces, StringComparison.Ordinal);
        Assert.Contains("new ProgrammeGuideView", lazyWorkspaces, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveTvWorkspace", lazyWorkspaces, StringComparison.Ordinal);
        Assert.Contains("EnsureProgrammeGuideWorkspace", lazyWorkspaces, StringComparison.Ordinal);
        Assert.Contains(
            "RequestedTheme = WindowRoot.RequestedTheme",
            lazyWorkspaces,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyThemeToLazyWorkspaces();",
            appearance,
            StringComparison.Ordinal);

        Assert.Contains("x:Name=\"CategoryRailCard\"", live, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ChannelBrowserCard\"", live, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlayerWorkspace\"", live, StringComparison.Ordinal);
        Assert.Contains("EfironVideoOverlayBrush", live, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProgrammeCard\"", live, StringComparison.Ordinal);
        Assert.Contains("Text=\"СЕЙЧАС\"", live, StringComparison.Ordinal);
        Assert.Contains("Text=\"ДАЛЕЕ\"", live, StringComparison.Ordinal);
        Assert.Contains("Source=\"{Binding LogoUrl}\"", live, StringComparison.Ordinal);
        Assert.Contains("snapshot.Channel.LogoUri", channelPresentation, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "LiveRoot.LayoutUpdated +=",
            presentationPolish,
            StringComparison.Ordinal);
        Assert.Contains("SetIfChanged", presentationPolish, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"TimelineHeaderCanvas\"", programme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EpgRowsViewport\"", programme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EpgRowsCanvas\"", programme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EpgVerticalScrollBar\"", programme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EpgHorizontalScrollBar\"", programme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TimelineZoomSlider\"", programme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CurrentTimeLine\"", programme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProgrammeDetailsCard\"", programme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlayProgrammeChannelButton\"", programme, StringComparison.Ordinal);
        Assert.DoesNotContain("ChannelRowsListView", programme, StringComparison.Ordinal);
        Assert.DoesNotContain("TimelineRowsListView", programme, StringComparison.Ordinal);
        Assert.Contains("RenderViewport", programmeRenderer, StringComparison.Ordinal);
        Assert.Contains("manual-two-axis-virtualization", programmeProjection, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<Programme> Schedule", channelSnapshot, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateExistingGreenfieldProjects(
        string repositoryRoot)
    {
        foreach (var projectName in GreenfieldProjectNames)
        {
            var projectPath = Path.Combine(
                repositoryRoot,
                "src",
                projectName,
                $"{projectName}.csproj");

            if (File.Exists(projectPath))
            {
                yield return projectPath;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Efiron.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to locate the Efiron repository root.");
    }
}
