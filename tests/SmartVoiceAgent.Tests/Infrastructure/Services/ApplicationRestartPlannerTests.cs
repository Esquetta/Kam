using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class ApplicationRestartPlannerTests
{
    [Fact]
    public void CreateRestartPlan_WhenMsiPackageIsProvided_ReturnsInstallerHandoffSteps()
    {
        var planner = new ApplicationRestartPlanner();

        var plan = planner.CreateRestartPlan(@"C:\Updates\Kam-1.2.0-x64.msi");

        plan.CanRestart.Should().BeTrue();
        plan.UpdatePackagePath.Should().Be(@"C:\Updates\Kam-1.2.0-x64.msi");
        plan.Steps.Should().Contain(step => step.Contains("msiexec", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateRestartPlan_WhenZipPackageIsProvided_RequiresManualExtraction()
    {
        var planner = new ApplicationRestartPlanner();

        var plan = planner.CreateRestartPlan(@"C:\Updates\Kam-1.2.0-x64.zip");

        plan.CanRestart.Should().BeFalse();
        plan.Message.Should().Contain("manual extraction");
    }
}
