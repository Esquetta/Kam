using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class ApplicationRestartPlannerTests
{
    [Fact]
    public void CreateRestartPlan_WhenMsiPackageIsProvided_ReturnsInstallerHandoffSteps()
    {
        var planner = new ApplicationRestartPlanner();
        using var package = TemporaryPackage(".msi");

        var plan = planner.CreateRestartPlan(package.Path);

        plan.CanRestart.Should().BeTrue();
        plan.UpdatePackagePath.Should().Be(package.Path);
        plan.Steps.Should().Contain(step => step.Contains("msiexec", StringComparison.OrdinalIgnoreCase));
        plan.Steps.Should().Contain(step => step.Contains($"\"{package.Path}\"", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateRestartPlan_WhenZipPackageIsProvided_RequiresManualExtraction()
    {
        var planner = new ApplicationRestartPlanner();
        using var package = TemporaryPackage(".zip");

        var plan = planner.CreateRestartPlan(package.Path);

        plan.CanRestart.Should().BeFalse();
        plan.Message.Should().Contain("manual extraction");
    }

    [Fact]
    public void CreateRestartPlan_WhenPackageDoesNotExist_DoesNotCreateInstallerHandoff()
    {
        var planner = new ApplicationRestartPlanner();

        var plan = planner.CreateRestartPlan(Path.Combine(
            Path.GetTempPath(),
            "kam-missing-update.msi"));

        plan.CanRestart.Should().BeFalse();
        plan.Message.Should().Contain("could not be found");
    }

    [Theory]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    [InlineData(".ps1")]
    public void CreateRestartPlan_WhenPackageTypeCanRunScripts_DoesNotCreateInstallerHandoff(string extension)
    {
        var planner = new ApplicationRestartPlanner();
        using var package = TemporaryPackage(extension);

        var plan = planner.CreateRestartPlan(package.Path);

        plan.CanRestart.Should().BeFalse();
        plan.Message.Should().Contain("not supported");
    }

    private static TemporaryPackageFile TemporaryPackage(string extension)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "kam-restart-planner-tests",
            $"{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "package");
        return new TemporaryPackageFile(path);
    }

    private sealed class TemporaryPackageFile : IDisposable
    {
        public TemporaryPackageFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
