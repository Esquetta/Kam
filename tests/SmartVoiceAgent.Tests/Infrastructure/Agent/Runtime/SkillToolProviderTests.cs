using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;
using SmartVoiceAgent.Infrastructure.Skills;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Runtime;

public sealed class SkillToolProviderTests
{
    [Theory]
    [InlineData("files.read_lines", "files_read_lines")]
    [InlineData("apps.open", "apps_open")]
    [InlineData("mcp:github/create issue", "mcp_github_create_issue")]
    public void ToToolName_ReplacesCharactersProvidersReject(string skillId, string expected)
    {
        SkillToolProvider.ToToolName(skillId).Should().Be(expected);
    }

    [Fact]
    public void ToToolName_CapsLengthAt64()
    {
        SkillToolProvider.ToToolName(new string('a', 80)).Should().HaveLength(64);
    }

    [Theory]
    [InlineData("shell.run", SkillRiskLevel.High, new SkillPermission[0], ToolRisk.Execute)]
    [InlineData("apps.open", SkillRiskLevel.Medium, new[] { SkillPermission.ProcessLaunch }, ToolRisk.Execute)]
    [InlineData("files.write", SkillRiskLevel.Medium, new[] { SkillPermission.FileSystemWrite }, ToolRisk.Write)]
    [InlineData("communication.email.send", SkillRiskLevel.High, new[] { SkillPermission.Network }, ToolRisk.External)]
    [InlineData("web.search", SkillRiskLevel.Low, new[] { SkillPermission.Network }, ToolRisk.Network)]
    [InlineData("files.read", SkillRiskLevel.Low, new[] { SkillPermission.FileSystemRead }, ToolRisk.Read)]
    [InlineData("system.power", SkillRiskLevel.High, new SkillPermission[0], ToolRisk.Execute)]
    public void ClassifyRisk_MapsPermissionsToToolRisk(string id, SkillRiskLevel level, SkillPermission[] permissions, ToolRisk expected)
    {
        var manifest = new KamSkillManifest { Id = id, RiskLevel = level, Permissions = permissions.ToList() };

        SkillToolProvider.ClassifyRisk(manifest).Should().Be(expected);
    }

    [Fact]
    public void BuildSchema_DescribesArgumentsAndRequiredList()
    {
        var manifest = new KamSkillManifest
        {
            Id = "files.write",
            Arguments =
            [
                new SkillArgumentDefinition { Name = "path", Type = SkillArgumentType.String, Required = true, Description = "Target file" },
                new SkillArgumentDefinition { Name = "overwrite", Type = SkillArgumentType.Boolean },
                new SkillArgumentDefinition { Name = "lines", Type = SkillArgumentType.Array }
            ]
        };

        var schema = SkillToolProvider.BuildSchema(manifest);

        schema.GetProperty("type").GetString().Should().Be("object");
        var properties = schema.GetProperty("properties");
        properties.GetProperty("path").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("path").GetProperty("description").GetString().Should().Be("Target file");
        properties.GetProperty("overwrite").GetProperty("type").GetString().Should().Be("boolean");
        properties.GetProperty("lines").GetProperty("items").ValueKind.Should().Be(JsonValueKind.Object);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Equal("path");
    }

    [Fact]
    public async Task GetToolsAsync_OffersOnlyEnabledReviewedSkillsWithAnExecutor()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(Manifest("apps.open"));
        registry.Register(Manifest("apps.disabled", enabled: false));
        registry.Register(Manifest("imported.pending", reviewRequired: true));
        registry.Register(Manifest("nobody.runs"));
        registry.Register(Manifest("agents.run"));
        var provider = new SkillToolProvider(registry, new RecordingPipeline(), [new PrefixExecutor("apps."), new PrefixExecutor("imported."), new PrefixExecutor("agents.")]);

        var tools = await provider.GetToolsAsync(TestContext.Current.CancellationToken);

        tools.Select(t => t.Name).Should().Equal("apps_open");
        tools[0].DisplayName.Should().Be("Open apps.open");
        tools[0].Function.Description.Should().Be("Open apps.open. Does apps.open.");
    }

    [Fact]
    public async Task GetToolsAsync_ImportedSkillMdSkills_AreLeftToLoadSkill()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(Manifest("apps.open"));
        var imported = Manifest("local.pdf");
        imported.ExecutorType = "local";
        registry.Register(imported);
        var provider = new SkillToolProvider(registry, new RecordingPipeline(), [new PrefixExecutor("apps."), new PrefixExecutor("local.")]);

        var tools = await provider.GetToolsAsync(TestContext.Current.CancellationToken);

        tools.Select(t => t.Name).Should().Equal("apps_open");
    }

    [Fact]
    public async Task InvokedTool_RunsPipelineWithConfirmedPlanAndJsonArguments()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(Manifest("apps.open"));
        var pipeline = new RecordingPipeline { Result = SkillResult.Succeeded("Opened Notepad.", new { pid = 42 }) };
        var provider = new SkillToolProvider(registry, pipeline, [new PrefixExecutor("apps.")]);
        var tool = (await provider.GetToolsAsync(TestContext.Current.CancellationToken)).Single();

        var result = await tool.Function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["applicationName"] = "notepad", ["count"] = 2 }),
            TestContext.Current.CancellationToken);

        pipeline.Plans.Should().ContainSingle();
        var plan = pipeline.Plans[0];
        plan.SkillId.Should().Be("apps.open");
        plan.IsConfirmedByUser.Should().BeTrue();
        plan.Arguments["applicationName"].GetString().Should().Be("notepad");
        plan.Arguments["count"].GetInt32().Should().Be(2);
        result!.ToString().Should().Be("Opened Notepad.\n{\"pid\":42}");
    }

    [Fact]
    public async Task InvokedTool_Failure_ReturnsErrorText()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(Manifest("apps.open"));
        var pipeline = new RecordingPipeline { Result = SkillResult.Failed("Not installed", SkillExecutionStatus.Failed) };
        var provider = new SkillToolProvider(registry, pipeline, [new PrefixExecutor("apps.")]);
        var tool = (await provider.GetToolsAsync(TestContext.Current.CancellationToken)).Single();

        var result = await tool.Function.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        result!.ToString().Should().Be("Error (Failed): Not installed");
    }

    private static KamSkillManifest Manifest(string id, bool enabled = true, bool reviewRequired = false) => new()
    {
        Id = id,
        DisplayName = "Open " + id,
        Description = "Does " + id + ".",
        Enabled = enabled,
        ReviewRequired = reviewRequired,
        Arguments = [new SkillArgumentDefinition { Name = "applicationName", Type = SkillArgumentType.String, Required = true }]
    };

    private sealed class PrefixExecutor(string prefix) : ISkillExecutor
    {
        public bool CanExecute(string skillId) => skillId.StartsWith(prefix, StringComparison.Ordinal);

        public Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(SkillResult.Succeeded("ok"));
    }

    private sealed class RecordingPipeline : ISkillExecutionPipeline
    {
        public List<SkillPlan> Plans { get; } = [];

        public SkillResult Result { get; init; } = SkillResult.Succeeded("ok");

        public Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
        {
            Plans.Add(plan);
            return Task.FromResult(Result);
        }
    }
}
