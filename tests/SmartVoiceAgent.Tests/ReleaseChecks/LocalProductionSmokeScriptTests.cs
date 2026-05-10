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
        script.Should().Contain("--command-smoke");
        script.Should().Contain("SkipCommandSmoke");
        script.Should().Contain("dotnet\", \"publish\"");
        script.Should().Contain("MaxBuildWarnings");
        script.Should().Contain("StepTimeoutSeconds");
        script.Should().Contain("CommandSmokeTimeoutSeconds");
        script.Should().Contain("Join-ProcessArguments");
        script.Should().Contain("Kill($true)");
        script.Should().Contain("timed out after");
        script.Should().Contain("-TimeoutSeconds $CommandSmokeTimeoutSeconds");
        script.Should().Contain("ReleaseCandidate");
        script.Should().Contain("AllowDirtyWorktree");
        script.Should().Contain("git\", \"rev-parse\", \"HEAD\"");
        script.Should().Contain("git\", \"status\", \"--porcelain\", \"--untracked-files=no\"");
        script.Should().Contain("Reject-PlaceholderReleaseCandidate");
        script.Should().Contain("summary.md");
        script.Should().Contain("skill-smoke.md");
        script.Should().Contain("command-smoke.md");
        script.Should().Contain("SmartVoiceAgent.Ui.exe");
    }

    [Fact]
    public void DotnetWorkflow_RunsLocalProductionSmokeGate()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github", "workflows", "dotnet.yml"));

        workflow.Should().Contain("runs-on: windows-2025");
        workflow.Should().NotContain("runs-on: windows-latest");
        workflow.Should().Contain("uses: actions/checkout@v6");
        workflow.Should().Contain("uses: actions/setup-dotnet@v5");
        workflow.Should().Contain("uses: actions/upload-artifact@v7");
        workflow.Should().Contain("scripts/local-production-smoke.ps1");
        workflow.Should().Contain("-Configuration Release");
        workflow.Should().Contain("-Runtime win-x64");
        workflow.Should().Contain("-SkipTests");
        workflow.Should().Contain("-SkipSkillSmoke");
        workflow.Should().Contain("-RequireAiConfig:$false");
        workflow.Should().Contain("-ReleaseCandidate");
        workflow.Should().Contain("kam-windows-release");
        workflow.Should().Contain("kam-production-smoke-evidence");
        workflow.Should().Contain("summary.md");
        workflow.Should().Contain("skill-smoke.md");
        workflow.Should().Contain("command-smoke.md");
    }

    [Fact]
    public void DotnetWorkflow_SecurityScanFailsWhenAnalysisFindsIssues()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github", "workflows", "dotnet.yml"));
        var securityScanJobStart = workflow.IndexOf("  security-scan:", StringComparison.Ordinal);

        securityScanJobStart.Should().BeGreaterThan(0);
        var buildJob = workflow[..securityScanJobStart];
        var securityScanJob = workflow[securityScanJobStart..];

        buildJob.Should().Contain("Setup .NET 9");
        buildJob.Should().NotContain("6.0.x");
        securityScanJob.Should().Contain("Setup .NET for security tools");
        securityScanJob.Should().Contain("6.0.x");
        securityScanJob.Should().Contain("dotnet tool update --global security-scan --version 5.6.7");
        securityScanJob.Should().Contain("$installSucceeded = $LASTEXITCODE -eq 0");
        securityScanJob.Should().Contain("if (-not $installSucceeded)");
        securityScanJob.Should().Contain("Optional security-scan tool install failed. Skipping security analysis.");
        securityScanJob.Should().Contain("$env:PATH = \"$env:USERPROFILE\\.dotnet\\tools;$env:PATH\"");
        securityScanJob.Should().Contain("security-scan Kam.sln --excl-proj=\"**/SmartVoiceAgent.Tests.csproj\"");
        workflow.Should().NotContain("security-scan Kam.sln --excl-proj=\"**/SmartVoiceAgent.Tests.csproj\" || true");
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
