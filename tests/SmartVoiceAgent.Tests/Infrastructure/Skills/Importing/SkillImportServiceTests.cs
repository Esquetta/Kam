using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Adapters;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
using SmartVoiceAgent.Infrastructure.Skills.Policy;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Importing;

public class SkillImportServiceTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _policyFile;

    public SkillImportServiceTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-skill-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
        _policyFile = Path.Combine(_workspace, "skill-policies.json");
    }

    [Fact]
    public async Task ImportAsync_LocalSkillFolder_RegistersPolicyAppliedManifest()
    {
        var skillDirectory = CreateSkillDirectory(
            "desktop-navigation",
            "Navigate desktop windows reliably.");
        var registry = new InMemorySkillRegistry();
        var service = CreateImportService(registry);

        var result = await service.ImportAsync(new SkillSourceDefinition
        {
            Id = "local-dev",
            Kind = SkillSourceKind.LocalDirectory,
            Location = skillDirectory
        });

        result.ImportedCount.Should().Be(1);
        result.Manifests.Should().ContainSingle();
        registry.TryGet("local.desktop-navigation", out var manifest).Should().BeTrue();
        manifest!.Enabled.Should().BeFalse();
        manifest.ReviewRequired.Should().BeTrue();
        manifest.GrantedPermissions.Should().BeEmpty();
        manifest.Checksum.Should().NotBeNullOrWhiteSpace();
        manifest.InstalledFrom.Should().Be(skillDirectory);
        manifest.InstalledAt.Should().NotBe(default);
    }

    [Fact]
    public async Task ImportAsync_SkillsShDownloadedFolder_UsesSkillsShPrefixAndPolicy()
    {
        var skillDirectory = CreateSkillDirectory(
            "browser-automation",
            "Control browser tasks from a downloaded skill.");
        var registry = new InMemorySkillRegistry();
        var service = CreateImportService(registry);

        var result = await service.ImportAsync(new SkillSourceDefinition
        {
            Id = "skills-sh",
            Kind = SkillSourceKind.SkillsSh,
            Location = skillDirectory
        });

        result.ImportedCount.Should().Be(1);
        registry.TryGet("skills-sh.browser-automation", out var manifest).Should().BeTrue();
        manifest!.Source.Should().Be($"skills.sh:{skillDirectory}");
        manifest.ExecutorType.Should().Be("skills.sh");
        manifest.Enabled.Should().BeFalse();
        manifest.ReviewRequired.Should().BeTrue();
        manifest.Checksum.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ImportAsync_PersistedApproval_IsAppliedWhenReimporting()
    {
        var skillDirectory = CreateSkillDirectory(
            "desktop-navigation",
            "Navigate desktop windows reliably.");
        var policyStore = new JsonSkillPolicyStore(_policyFile);
        policyStore.SaveState(new SkillPolicyState
        {
            SkillId = "local.desktop-navigation",
            Enabled = true,
            ReviewRequired = false,
            GrantedPermissions = [SkillPermission.ProcessLaunch]
        });
        var registry = new InMemorySkillRegistry();
        var service = CreateImportService(registry, policyStore);

        await service.ImportAsync(new SkillSourceDefinition
        {
            Id = "local-dev",
            Kind = SkillSourceKind.LocalDirectory,
            Location = skillDirectory
        });

        registry.TryGet("local.desktop-navigation", out var manifest).Should().BeTrue();
        manifest!.Enabled.Should().BeTrue();
        manifest.ReviewRequired.Should().BeFalse();
        manifest.GrantedPermissions.Should().ContainSingle().Which.Should().Be(SkillPermission.ProcessLaunch);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private ISkillImportService CreateImportService(
        ISkillRegistry registry,
        ISkillPolicyStore? policyStore = null)
    {
        var adapterRegistry = new SkillAdapterRegistry(
        [
            new LocalSkillAdapter(),
            new SkillsShSkillAdapter(),
            new McpSkillAdapter()
        ]);

        return new SkillImportService(
            adapterRegistry,
            registry,
            policyStore ?? new JsonSkillPolicyStore(_policyFile));
    }

    private string CreateSkillDirectory(string name, string description)
    {
        var skillDirectory = Path.Combine(_workspace, name);
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(
            Path.Combine(skillDirectory, "SKILL.md"),
            $"""
            ---
            name: {name}
            description: {description}
            ---

            # {name}
            """);

        return skillDirectory;
    }
}
