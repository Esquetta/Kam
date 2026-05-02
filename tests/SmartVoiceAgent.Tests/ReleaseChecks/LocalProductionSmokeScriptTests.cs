using FluentAssertions;

namespace SmartVoiceAgent.Tests.Release;

public sealed class LocalProductionSmokeScriptTests
{
    [Fact]
    public void SmokeScript_ContainsReleaseCandidateGates()
    {
        var script = File.ReadAllText(FindRepoFile("scripts", "local-production-smoke.ps1"));

        script.Should().Contain("dotnet\", \"restore\"");
        script.Should().Contain("dotnet\", \"build\"");
        script.Should().Contain("dotnet\", \"test\"");
        script.Should().Contain("--skill-smoke");
        script.Should().Contain("dotnet\", \"publish\"");
        script.Should().Contain("MaxBuildWarnings");
    }

    [Fact]
    public void DotnetWorkflow_RunsLocalProductionSmokeGate()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github", "workflows", "dotnet.yml"));

        workflow.Should().Contain("scripts/local-production-smoke.ps1");
        workflow.Should().Contain("-Configuration Release");
        workflow.Should().Contain("-Runtime win-x64");
        workflow.Should().Contain("-SkipTests");
        workflow.Should().Contain("-SkipSkillSmoke");
        workflow.Should().Contain("-RequireAiConfig:$false");
        workflow.Should().Contain("kam-windows-release");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from test output.");
    }
}
