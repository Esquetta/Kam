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
    public async Task ExecuteAsync_FilesSearchContent_ReturnsMatchingFileAndLine()
    {
        var filePath = Path.Combine(_workspace, "notes.md");
        await File.WriteAllTextAsync(filePath, "first line\ncodex style search\nlast line");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "files.search_content",
            new
            {
                directoryPath = _workspace,
                query = "codex",
                searchPattern = "*.md",
                recursive = false,
                maxMatches = 10
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("notes.md");
        result.Message.Should().Contain("2:");
        result.Message.Should().Contain("codex style search");
    }

    [Fact]
    public async Task ExecuteAsync_FilesTree_ReturnsBoundedDirectoryTree()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "src"));
        await File.WriteAllTextAsync(Path.Combine(_workspace, "README.md"), "hello");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "src", "app.cs"), "class App {}");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "files.tree",
            new
            {
                directoryPath = _workspace,
                maxDepth = 2,
                maxEntries = 20
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("README.md");
        result.Message.Should().Contain("src");
        result.Message.Should().Contain("app.cs");
    }

    [Fact]
    public async Task ExecuteAsync_FileReadAlias_MapsCodexStyleReadRequest()
    {
        var filePath = Path.Combine(_workspace, "README.md");
        await File.WriteAllTextAsync(filePath, "# Kam\nCodex-style file reader");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject("file.read", new { filePath }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Codex-style file reader");
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceMap_ReturnsBoundedTreeAndExtensionSummary()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "src"));
        await File.WriteAllTextAsync(Path.Combine(_workspace, "README.md"), "# Kam");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "src", "Program.cs"), "public class Program {}");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "src", "app.ts"), "export const app = true;");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "workspace.map",
            new
            {
                directoryPath = _workspace,
                maxDepth = 2,
                maxEntries = 20
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Workspace Map");
        result.Message.Should().Contain("README.md");
        result.Message.Should().Contain(".cs: 1");
        result.Message.Should().Contain(".ts: 1");
    }

    [Fact]
    public async Task ExecuteAsync_CodeOutline_ReturnsLineNumberedSymbols()
    {
        var filePath = Path.Combine(_workspace, "Worker.cs");
        await File.WriteAllTextAsync(
            filePath,
            """
            namespace Demo;

            public sealed class Worker
            {
                public Task RunAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """);
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "code.outline",
            new
            {
                filePath,
                maxSymbols = 10
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Code Outline");
        result.Message.Should().Contain("3: public sealed class Worker");
        result.Message.Should().Contain("5: public Task RunAsync()");
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
