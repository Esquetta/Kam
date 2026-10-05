using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Runtime;

public sealed class ToolPermissionServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "kam-permission-tests", Guid.NewGuid().ToString("N"), "agent-permissions.json");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(ApprovalMode.Ask, ToolRisk.Read, ToolPermissionDecision.Allow)]
    [InlineData(ApprovalMode.Ask, ToolRisk.Network, ToolPermissionDecision.Allow)]
    [InlineData(ApprovalMode.Ask, ToolRisk.Write, ToolPermissionDecision.Ask)]
    [InlineData(ApprovalMode.Ask, ToolRisk.Execute, ToolPermissionDecision.Ask)]
    [InlineData(ApprovalMode.Ask, ToolRisk.External, ToolPermissionDecision.Ask)]
    [InlineData(ApprovalMode.AutoEdit, ToolRisk.Write, ToolPermissionDecision.Allow)]
    [InlineData(ApprovalMode.AutoEdit, ToolRisk.Execute, ToolPermissionDecision.Ask)]
    [InlineData(ApprovalMode.AutoEdit, ToolRisk.External, ToolPermissionDecision.Ask)]
    [InlineData(ApprovalMode.FullAuto, ToolRisk.Execute, ToolPermissionDecision.Allow)]
    [InlineData(ApprovalMode.FullAuto, ToolRisk.External, ToolPermissionDecision.Allow)]
    public void Evaluate_AppliesModeToRisk(ApprovalMode mode, ToolRisk risk, ToolPermissionDecision expected)
    {
        var service = new ToolPermissionService(_path, mode);

        service.Evaluate(Tool("some_tool", risk), "{}").Should().Be(expected);
    }

    [Fact]
    public void AlwaysAllow_PersistsAcrossInstancesAndCanBeRevoked()
    {
        var first = new ToolPermissionService(_path);
        first.AlwaysAllow("shell_run");
        first.Mode = ApprovalMode.AutoEdit;

        var second = new ToolPermissionService(_path);
        second.Mode.Should().Be(ApprovalMode.AutoEdit);
        second.AllowedTools.Should().Equal("shell_run");
        second.Evaluate(Tool("shell_run", ToolRisk.Execute), "{}").Should().Be(ToolPermissionDecision.Allow);

        second.Revoke("shell_run");
        new ToolPermissionService(_path).Evaluate(Tool("shell_run", ToolRisk.Execute), "{}").Should().Be(ToolPermissionDecision.Ask);
    }

    [Fact]
    public void Constructor_CorruptFile_FallsBackToDefaultMode()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not json");

        var service = new ToolPermissionService(_path, ApprovalMode.AutoEdit);

        service.Mode.Should().Be(ApprovalMode.AutoEdit);
        service.AllowedTools.Should().BeEmpty();
    }

    private static AgentToolDescriptor Tool(string name, ToolRisk risk) =>
        new(AIFunctionFactory.Create(() => "ok", name), risk, "test", name);
}
