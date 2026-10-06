using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Extensions;

public sealed class AgentSkillCatalogTests : IDisposable
{
    private readonly ExtensionTestFolder _folder = new();

    [Fact]
    public void GetSkills_FindsUserWorkspaceAndPluginSkills_UserWinsOnName()
    {
        _folder.Write("user/pdf/SKILL.md", "---\nname: pdf\ndescription: User PDF skill\n---\nBody");
        _folder.Write("work/.claude/skills/pdf/SKILL.md", "---\nname: pdf\ndescription: Workspace copy\n---\nBody");
        _folder.Write("work/.kam/skills/release/SKILL.md", "---\nname: release\ndescription: Cut a release\n---\nBody");
        _folder.Write("plugin/skills/lint/SKILL.md", "---\nname: lint\ndescription: Lint the code\n---\nBody");
        _folder.Write("user/not-a-skill/README.md", "ignored");
        var plugins = PluginsWith(new AgentPluginInfo
        {
            Name = "tools",
            Directory = _folder.Path("plugin"),
            Enabled = true,
            SkillDirectories = [_folder.Path("plugin", "skills", "lint")]
        });

        var catalog = new AgentSkillCatalog(_folder.Path("user"), plugins, workspaceRoot: () => _folder.Path("work"));
        var skills = catalog.GetSkills();

        skills.Select(skill => $"{skill.Name}|{skill.Source}|{skill.Description}").Should().Equal(
            "pdf|user|User PDF skill",
            "release|workspace|Cut a release",
            "tools:lint|plugin:tools|Lint the code");
    }

    [Fact]
    public void GetSkills_DisabledPlugin_IsSkipped()
    {
        _folder.Write("plugin/skills/lint/SKILL.md", "---\nname: lint\ndescription: d\n---\n");
        var plugins = PluginsWith(new AgentPluginInfo
        {
            Name = "tools",
            Directory = _folder.Path("plugin"),
            Enabled = false,
            SkillDirectories = [_folder.Path("plugin", "skills", "lint")]
        });

        new AgentSkillCatalog(_folder.Path("user"), plugins).GetSkills().Should().BeEmpty();
    }

    [Fact]
    public void GetSkills_ImportedSkills_OnlyWhenReviewedAndEnabled()
    {
        _folder.Write("imported/a/SKILL.md", "---\nname: approved\ndescription: d\n---\n");
        _folder.Write("imported/b/SKILL.md", "---\nname: pending\ndescription: d\n---\n");
        var registry = new Mock<ISkillRegistry>();
        registry.Setup(r => r.GetAll()).Returns(
        [
            new KamSkillManifest { Id = "local.approved", ExecutorType = "local", Source = "local:" + _folder.Path("imported", "a"), Enabled = true, ReviewRequired = false },
            new KamSkillManifest { Id = "local.pending", ExecutorType = "local", Source = "local:" + _folder.Path("imported", "b"), Enabled = false, ReviewRequired = true },
            new KamSkillManifest { Id = "files.read", ExecutorType = "builtin", Source = "builtin", Enabled = true }
        ]);

        var skills = new AgentSkillCatalog(_folder.Path("user"), registry: registry.Object).GetSkills();

        skills.Should().ContainSingle().Which.Should().Match<AgentSkillInfo>(skill => skill.Name == "approved" && skill.Source == "imported");
    }

    [Fact]
    public void ReadSkill_WithoutFrontmatter_UsesFolderNameAndFirstParagraph()
    {
        _folder.Write("user/commit-helper/SKILL.md", "# Commit helper\n\nWrites commit messages\nin our style.\n\nMore text.");

        var skill = new AgentSkillCatalog(_folder.Path("user")).GetSkills().Single();

        skill.Name.Should().Be("commit-helper");
        skill.Description.Should().Be("Writes commit messages in our style.");
    }

    [Fact]
    public void Install_CopiesFolderIntoUserSkills_AndUninstallRemovesIt()
    {
        _folder.Write("download/pdf-tools/SKILL.md", "---\nname: pdf\ndescription: d\n---\n");
        _folder.Write("download/pdf-tools/scripts/extract.py", "print('hi')");
        _folder.Write("download/pdf-tools/.git/HEAD", "ref");
        var catalog = new AgentSkillCatalog(_folder.Path("user"));

        var installed = catalog.Install(_folder.Path("download", "pdf-tools"));

        installed.Directory.Should().Be(_folder.Path("user", "pdf"));
        File.Exists(_folder.Path("user", "pdf", "scripts", "extract.py")).Should().BeTrue();
        Directory.Exists(_folder.Path("user", "pdf", ".git")).Should().BeFalse();
        catalog.GetSkills().Should().ContainSingle(skill => skill.Name == "pdf");

        catalog.Uninstall("pdf").Should().BeTrue();
        catalog.GetSkills().Should().BeEmpty();
        catalog.Uninstall("pdf").Should().BeFalse();
    }

    [Fact]
    public void Install_FolderWithoutSkill_Throws()
    {
        Directory.CreateDirectory(_folder.Path("empty"));

        var act = () => new AgentSkillCatalog(_folder.Path("user")).Install(_folder.Path("empty"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*SKILL.md*");
    }

    [Fact]
    public async Task LoadSkill_ReturnsInstructionsAndListsOtherFiles()
    {
        _folder.Write("user/pdf/SKILL.md", "---\nname: pdf\ndescription: d\n---\n# PDF\n\nRead reference.md for forms.");
        _folder.Write("user/pdf/reference.md", "Form fields: ...");
        _folder.Write("user/pdf/scripts/fill.py", "pass");
        var tool = await LoadSkillToolAsync();

        var result = (string)(await tool.Function.InvokeAsync(Arguments(("name", "pdf"))))!;

        tool.Risk.Should().Be(ToolRisk.Read);
        result.Should().Contain("Skill: pdf").And.Contain("Read reference.md for forms.").And.NotContain("description: d");
        result.Should().Contain("reference.md, scripts/fill.py");
    }

    [Fact]
    public async Task LoadSkill_ReadsFileInsideSkill_RejectsPathsOutside()
    {
        _folder.Write("user/pdf/SKILL.md", "---\nname: pdf\ndescription: d\n---\nBody");
        _folder.Write("user/pdf/reference.md", "Form fields");
        _folder.Write("user/secret.txt", "secret");
        var tool = await LoadSkillToolAsync();

        (await tool.Function.InvokeAsync(Arguments(("name", "pdf"), ("file", "reference.md")))).Should().Be("Form fields");
        (await tool.Function.InvokeAsync(Arguments(("name", "pdf"), ("file", "../secret.txt"))))
            .Should().Be("Error: the file must be inside the skill folder.");
        (await tool.Function.InvokeAsync(Arguments(("name", "nope"))))
            .Should().BeOfType<string>().Which.Should().StartWith("Error: there is no skill named 'nope'");
    }

    [Fact]
    public async Task LoadSkill_SchemaListsSkillNames()
    {
        _folder.Write("user/pdf/SKILL.md", "---\nname: pdf\ndescription: d\n---\n");
        _folder.Write("user/xlsx/SKILL.md", "---\nname: xlsx\ndescription: d\n---\n");

        var tool = await LoadSkillToolAsync();

        tool.Function.JsonSchema.GetProperty("properties").GetProperty("name").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()).Should().Equal("pdf", "xlsx");
    }

    [Fact]
    public async Task ToolProvider_NoSkills_OffersNoTool()
    {
        var tools = await new AgentSkillToolProvider(new AgentSkillCatalog(_folder.Path("user"))).GetToolsAsync();

        tools.Should().BeEmpty();
    }

    [Fact]
    public void PromptContributor_ListsNamesAndDescriptions()
    {
        _folder.Write("user/pdf/SKILL.md", "---\nname: pdf\ndescription: Extract PDF text\n---\n");
        var contributor = new AgentSkillPromptContributor(new AgentSkillCatalog(_folder.Path("user")));

        var section = contributor.GetPromptSection();

        section.Should().StartWith("Skills:").And.Contain("load_skill").And.Contain("- pdf: Extract PDF text");
        new AgentSkillPromptContributor(new AgentSkillCatalog(_folder.Path("none"))).GetPromptSection().Should().BeNull();
    }

    public void Dispose() => _folder.Dispose();

    private async Task<AgentToolDescriptor> LoadSkillToolAsync() =>
        (await new AgentSkillToolProvider(new AgentSkillCatalog(_folder.Path("user"))).GetToolsAsync()).Single();

    private static AIFunctionArguments Arguments(params (string Key, string Value)[] values)
    {
        var arguments = new AIFunctionArguments();
        foreach (var (key, value) in values)
        {
            arguments[key] = JsonSerializer.SerializeToElement(value);
        }

        return arguments;
    }

    private static IAgentPluginCatalog PluginsWith(params AgentPluginInfo[] plugins)
    {
        var catalog = new Mock<IAgentPluginCatalog>();
        catalog.Setup(c => c.GetPlugins()).Returns(plugins);
        return catalog.Object;
    }
}
