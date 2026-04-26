using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Adapters;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Adapters;

public class SkillAdapterRegistryTests : IDisposable
{
    private readonly string _workspace;

    public SkillAdapterRegistryTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-skill-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public async Task DiscoverAsync_LocalSkillFolder_ReadsSkillMarkdownFrontMatter()
    {
        var skillDirectory = CreateSkillDirectory(
            "desktop-navigation",
            "Navigate desktop windows reliably.");
        var registry = new SkillAdapterRegistry([new LocalSkillAdapter()]);

        var manifests = await registry.DiscoverAsync(
        [
            new SkillSourceDefinition
            {
                Id = "local-dev",
                Kind = SkillSourceKind.LocalDirectory,
                Location = skillDirectory
            }
        ]);

        manifests.Should().ContainSingle(manifest =>
            manifest.Id == "local.desktop-navigation"
            && manifest.DisplayName == "desktop-navigation"
            && manifest.Description == "Navigate desktop windows reliably."
            && manifest.ExecutorType == "local"
            && manifest.Source == $"local:{skillDirectory}");
    }

    [Fact]
    public async Task DiscoverAsync_SkillsShDownloadedFolder_UsesSkillsShSourcePrefix()
    {
        var skillDirectory = CreateSkillDirectory(
            "browser-automation",
            "Control browser tasks from a downloaded skill.");
        var registry = new SkillAdapterRegistry([new SkillsShSkillAdapter()]);

        var manifests = await registry.DiscoverAsync(
        [
            new SkillSourceDefinition
            {
                Id = "skills-sh",
                Kind = SkillSourceKind.SkillsSh,
                Location = skillDirectory
            }
        ]);

        manifests.Should().ContainSingle(manifest =>
            manifest.Id == "skills-sh.browser-automation"
            && manifest.ExecutorType == "skills.sh"
            && manifest.Source == $"skills.sh:{skillDirectory}");
    }

    [Fact]
    public async Task DiscoverAsync_McpSource_NormalizesConfiguredToolDefinitions()
    {
        var registry = new SkillAdapterRegistry([new McpSkillAdapter()]);

        var manifests = await registry.DiscoverAsync(
        [
            new SkillSourceDefinition
            {
                Id = "todoist",
                Kind = SkillSourceKind.Mcp,
                Location = "https://todoist.mcp.example",
                Skills =
                [
                    new ExternalSkillDefinition
                    {
                        Name = "create_task",
                        DisplayName = "Create Todoist Task",
                        Description = "Create a task through Todoist MCP.",
                        Arguments =
                        [
                            new SkillArgumentDefinition
                            {
                                Name = "title",
                                Type = SkillArgumentType.String,
                                Required = true
                            }
                        ]
                    }
                ]
            }
        ]);

        var manifest = manifests.Should().ContainSingle().Subject;
        manifest.Id.Should().Be("mcp.todoist.create_task");
        manifest.Source.Should().Be("mcp:todoist");
        manifest.ExecutorType.Should().Be("mcp");
        manifest.Arguments.Should().ContainSingle(argument => argument.Name == "title" && argument.Required);
    }

    [Fact]
    public async Task DiscoverAsync_DisabledSource_IsSkipped()
    {
        var skillDirectory = CreateSkillDirectory("disabled-skill", "Should not be discovered.");
        var registry = new SkillAdapterRegistry([new LocalSkillAdapter()]);

        var manifests = await registry.DiscoverAsync(
        [
            new SkillSourceDefinition
            {
                Id = "local-dev",
                Kind = SkillSourceKind.LocalDirectory,
                Location = skillDirectory,
                Enabled = false
            }
        ]);

        manifests.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
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
