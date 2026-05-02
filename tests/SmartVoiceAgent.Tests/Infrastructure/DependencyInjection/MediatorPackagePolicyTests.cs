using System.Xml.Linq;
using FluentAssertions;

namespace SmartVoiceAgent.Tests.Infrastructure.DependencyInjection;

public class MediatorPackagePolicyTests
{
    [Fact]
    public void ProjectFiles_UseCortexMediatorWithoutMediatR()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFiles = Directory.GetFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var mediatRReferences = projectFiles
            .SelectMany(projectPath => ReadPackageReferences(projectPath, "MediatR"))
            .Select(reference => $"{Path.GetRelativePath(repositoryRoot, reference.ProjectPath)} -> {reference.PackageName} {reference.Version}")
            .ToArray();

        mediatRReferences.Should().BeEmpty(
            "production builds should use Cortex.Mediator instead of any MediatR package line");

        var cortexReferences = projectFiles
            .SelectMany(projectPath => ReadPackageReferences(projectPath, "Cortex.Mediator"))
            .ToArray();

        cortexReferences.Should().NotBeEmpty(
            "the application mediator pipeline should be backed by the selected free Cortex.Mediator package");
    }

    private static IEnumerable<PackageReference> ReadPackageReferences(string projectPath, string packageName)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("PackageReference")
            .Where(element => string.Equals(
                (string?)element.Attribute("Include"),
                packageName,
                StringComparison.OrdinalIgnoreCase))
            .Select(element => new PackageReference(
                projectPath,
                packageName,
                (string?)element.Attribute("Version") ?? string.Empty));
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

    private sealed record PackageReference(string ProjectPath, string PackageName, string Version);
}
