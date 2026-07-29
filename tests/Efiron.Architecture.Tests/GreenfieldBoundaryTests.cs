using System.Xml.Linq;

namespace Efiron.Architecture.Tests;

public sealed class GreenfieldBoundaryTests
{
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
        "Efiron.App",
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
                include => include!.Contains(
                    "Efiron.App",
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

        Assert.Equal(["Efiron.Domain"], references);
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
