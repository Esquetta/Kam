using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class FileSkillExecutorTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _outsideWorkspace;

    public FileSkillExecutorTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-file-skill-{Guid.NewGuid():N}");
        _outsideWorkspace = Path.Combine(Path.GetTempPath(), $"kam-file-skill-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_outsideWorkspace);
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
    public async Task ExecuteAsync_FileRead_BlocksPathOutsideDefaultWorkspace()
    {
        var filePath = Path.Combine(_outsideWorkspace, "secret.txt");
        await File.WriteAllTextAsync(filePath, "outside workspace");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject("file.read", new { filePath }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Güvenlik");
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
    public async Task ExecuteAsync_FileReplaceRange_UpdatesSelectedLinesAndReturnsDiff()
    {
        var filePath = Path.Combine(_workspace, "notes.md");
        await File.WriteAllTextAsync(filePath, "one\ntwo\nthree\nfour");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "file.replace_range",
            new
            {
                filePath,
                startLine = 2,
                lineCount = 2,
                replacement = "TWO\nTHREE"
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Diff Preview");
        result.Message.Should().Contain("-two");
        result.Message.Should().Contain("+TWO");
        var updated = await File.ReadAllTextAsync(filePath);
        updated.Should().Be("one\nTWO\nTHREE\nfour");
    }

    [Fact]
    public async Task ExecuteAsync_FilePatch_ReplacesExactTextOnceAndReturnsDiff()
    {
        var filePath = Path.Combine(_workspace, "Program.cs");
        await File.WriteAllTextAsync(filePath, "class Program\n{\n    void Run() {}\n}");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "file.patch",
            new
            {
                filePath,
                oldText = "void Run() {}",
                newText = "void RunAsync() {}",
                expectedOccurrences = 1
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Patch applied");
        result.Message.Should().Contain("-    void Run() {}");
        result.Message.Should().Contain("+    void RunAsync() {}");
        var updated = await File.ReadAllTextAsync(filePath);
        updated.Should().Contain("void RunAsync() {}");
        updated.Should().NotContain("void Run() {}");
    }

    [Fact]
    public async Task ExecuteAsync_FilesWritePreviewOnly_ReturnsDiffWithoutModifyingFile()
    {
        var filePath = Path.Combine(_workspace, "notes.md");
        await File.WriteAllTextAsync(filePath, "# Kam\nold line");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "files.write",
            new
            {
                filePath,
                content = "# Kam\nnew line",
                previewOnly = true
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Diff Preview");
        result.Message.Should().Contain("-old line");
        result.Message.Should().Contain("+new line");
        var unchanged = await File.ReadAllTextAsync(filePath);
        unchanged.Should().Be("# Kam\nold line");
    }

    [Fact]
    public async Task ExecuteAsync_FilesDelete_BlocksPathOutsideDefaultWorkspace()
    {
        var filePath = Path.Combine(_outsideWorkspace, "keep-me.txt");
        await File.WriteAllTextAsync(filePath, "outside workspace");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject("files.delete", new { filePath }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Güvenlik");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceDiffPreview_DoesNotModifyFile()
    {
        var filePath = Path.Combine(_workspace, "README.md");
        await File.WriteAllTextAsync(filePath, "# Kam\nold line");
        var executor = new FileSkillExecutor(new FileAgentTools(_workspace));

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "workspace.diff_preview",
            new
            {
                filePath,
                proposedContent = "# Kam\nnew line"
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Diff Preview");
        result.Message.Should().Contain("-old line");
        result.Message.Should().Contain("+new line");
        var unchanged = await File.ReadAllTextAsync(filePath);
        unchanged.Should().Be("# Kam\nold line");
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

        if (Directory.Exists(_outsideWorkspace))
        {
            Directory.Delete(_outsideWorkspace, recursive: true);
        }
    }
}
