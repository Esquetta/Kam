using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class FileSkillExecutorTests : IDisposable
{
    private readonly string _workspace;

    public FileSkillExecutorTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-file-skill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public async Task ExecuteAsync_FilesExists_MapsJsonArgumentsToFileAgentTool()
    {
        var filePath = Path.Combine(_workspace, "notes.txt");
        await File.WriteAllTextAsync(filePath, "hello");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject("files.exists", new { filePath }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Dosya mevcut");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedSkill_ReturnsFailure()
    {
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject("files.unknown", new { }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported file skill");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }
}
