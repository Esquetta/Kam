using System.Xml.Linq;
using FluentAssertions;

namespace SmartVoiceAgent.Tests.Infrastructure.DependencyInjection;

public class MediatorPackagePolicyTests
{
    [Fact]
    public void ProjectFiles_DoNotReferenceLuckyPennyMediatR()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFiles = Directory.GetFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var blockedReferences = projectFiles
            .SelectMany(ReadMediatRReferences)
            .Where(reference => reference.Version.Major >= 13)
            .Select(reference => $"{Path.GetRelativePath(repositoryRoot, reference.ProjectPath)} -> MediatR {reference.Version}")
            .ToArray();

        blockedReferences.Should().BeEmpty(
            "MediatR 13+ emits LuckyPenny production license warnings; production builds must use the pinned free package line or a fully migrated free mediator");
    }

    private static IEnumerable<MediatRReference> ReadMediatRReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("PackageReference")
            .Where(element => string.Equals(
                (string?)element.Attribute("Include"),
                "MediatR",
                StringComparison.OrdinalIgnoreCase))
            .Select(element => new MediatRReference(
                projectPath,
                Version.Parse((string?)element.Attribute("Version") ?? "0.0.0")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kam.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record MediatRReference(string ProjectPath, Version Version);
}
