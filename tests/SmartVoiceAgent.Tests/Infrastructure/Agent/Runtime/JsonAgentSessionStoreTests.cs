using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Runtime;

public sealed class JsonAgentSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kam-session-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsTextAndToolContent()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "Open notepad"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "apps_open", new Dictionary<string, object?> { ["applicationName"] = "notepad" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "Opened Notepad.")]),
            new(ChatRole.Assistant, "Notepad is open.")
        ];

        await store.SaveAsync("abc-1", messages, cancellationToken);
        var loaded = await store.LoadAsync("abc-1", cancellationToken);

        loaded.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant);
        var call = loaded[1].Contents.OfType<FunctionCallContent>().Single();
        call.CallId.Should().Be("c1");
        call.Name.Should().Be("apps_open");
        call.Arguments!["applicationName"]!.ToString().Should().Be("notepad");
        loaded[2].Contents.OfType<FunctionResultContent>().Single().Result!.ToString().Should().Be("Opened Notepad.");
        loaded[3].Text.Should().Be("Notepad is open.");
    }

    [Fact]
    public async Task ListAsync_ReturnsTitleFromFirstUserMessage()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.SaveAsync("one", [new ChatMessage(ChatRole.User, new string('a', 60))], cancellationToken);
        await store.SaveAsync("two", [new ChatMessage(ChatRole.User, "Short title")], cancellationToken);

        var sessions = await store.ListAsync(cancellationToken);

        sessions.Should().HaveCount(2);
        sessions.Single(s => s.Id == "two").Title.Should().Be("Short title");
        sessions.Single(s => s.Id == "one").Title.Should().Be(new string('a', 48) + "...");
        sessions.Single(s => s.Id == "two").MessageCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_MissingOrCorruptFile_ReturnsEmpty()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "bad.json"), "{ nope", cancellationToken);

        (await store.LoadAsync("missing", cancellationToken)).Should().BeEmpty();
        (await store.LoadAsync("bad", cancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task SessionIds_CannotEscapeTheFolder()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.SaveAsync("../../evil", [new ChatMessage(ChatRole.User, "x")], cancellationToken);

        File.Exists(Path.Combine(_directory, "evil.json")).Should().BeTrue();
        await store.Invoking(s => s.SaveAsync("../..", [], cancellationToken)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeleteAsync_RemovesSession()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.SaveAsync("gone", [new ChatMessage(ChatRole.User, "x")], cancellationToken);

        await store.DeleteAsync("gone", cancellationToken);

        (await store.ListAsync(cancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task RenameAsync_KeepsTitleAcrossSaves_AndBlankTitleGoesBackToFirstMessage()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.SaveAsync("t1", [new ChatMessage(ChatRole.User, "Fix the build")], cancellationToken);

        await store.RenameAsync("t1", "  Release work  ", cancellationToken);
        await store.SaveAsync("t1", [new ChatMessage(ChatRole.User, "Fix the build"), new ChatMessage(ChatRole.Assistant, "Done.")], cancellationToken);

        var renamed = (await new JsonAgentSessionStore(_directory).ListAsync(cancellationToken)).Single();
        renamed.Title.Should().Be("Release work");
        renamed.HasCustomTitle.Should().BeTrue();
        renamed.MessageCount.Should().Be(2);

        await store.RenameAsync("t1", "   ", cancellationToken);

        var reset = (await new JsonAgentSessionStore(_directory).GetSummaryAsync("t1", cancellationToken))!;
        reset.Title.Should().Be("Fix the build");
        reset.HasCustomTitle.Should().BeFalse();
    }

    [Fact]
    public async Task RenameAsync_DoesNotMoveTheThreadUp()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.SaveAsync("old", [new ChatMessage(ChatRole.User, "old")], cancellationToken);
        await Task.Delay(20, cancellationToken);
        await store.SaveAsync("new", [new ChatMessage(ChatRole.User, "new")], cancellationToken);

        await store.RenameAsync("old", "Renamed", cancellationToken);

        (await store.ListAsync(cancellationToken)).Select(s => s.Id).Should().Equal("new", "old");
    }

    [Fact]
    public async Task SetModelAsync_IsSavedWithTheThread()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.SaveAsync("m1", [new ChatMessage(ChatRole.User, "hi")], cancellationToken);

        await store.SetModelAsync("m1", " vendor/model-b ", cancellationToken);
        await store.SaveAsync("m1", [new ChatMessage(ChatRole.User, "hi"), new ChatMessage(ChatRole.Assistant, "hello")], cancellationToken);

        var reopened = new JsonAgentSessionStore(_directory);
        (await reopened.GetSummaryAsync("m1", cancellationToken))!.ModelId.Should().Be("vendor/model-b");
        (await reopened.LoadAsync("m1", cancellationToken)).Should().HaveCount(2);

        await reopened.SetModelAsync("m1", null, cancellationToken);
        (await new JsonAgentSessionStore(_directory).GetSummaryAsync("m1", cancellationToken))!.ModelId.Should().BeNull();
    }

    [Fact]
    public async Task TitleAndModel_SetBeforeTheFirstMessage_AreWrittenByTheFirstSave()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.RenameAsync("fresh", "Planning", cancellationToken);
        await store.SetModelAsync("fresh", "local-model", cancellationToken);
        File.Exists(Path.Combine(_directory, "fresh.json")).Should().BeFalse();
        (await store.GetSummaryAsync("fresh", cancellationToken))!.ModelId.Should().Be("local-model");

        await store.SaveAsync("fresh", [new ChatMessage(ChatRole.User, "first")], cancellationToken);

        var saved = (await new JsonAgentSessionStore(_directory).GetSummaryAsync("fresh", cancellationToken))!;
        saved.Title.Should().Be("Planning");
        saved.ModelId.Should().Be("local-model");
        (await store.GetSummaryAsync("unknown", cancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SavedFile_WritesTheHeaderBeforeTheMessages()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.RenameAsync("h1", "Header first", cancellationToken);
        await store.SaveAsync("h1", [new ChatMessage(ChatRole.User, new string('x', 20000))], cancellationToken);

        var text = await File.ReadAllTextAsync(Path.Combine(_directory, "h1.json"), cancellationToken);
        var messages = text.IndexOf("\"messages\"", StringComparison.Ordinal);

        text.IndexOf("\"messageCount\"", StringComparison.Ordinal).Should().BeInRange(0, messages);
        text.IndexOf("\"customTitle\"", StringComparison.Ordinal).Should().BeInRange(0, messages);
        (await store.ListAsync(cancellationToken)).Single().Title.Should().Be("Header first");
    }

    [Fact]
    public async Task ListAsync_ReadsFilesWrittenBeforeTheHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        var legacy = new
        {
            id = "legacy",
            title = "Old thread",
            updatedAt = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
            messages = new[] { new ChatMessage(ChatRole.User, "Old thread"), new ChatMessage(ChatRole.Assistant, "Reply") }
        };
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "legacy.json"),
            JsonSerializer.Serialize(legacy, AIJsonUtilities.DefaultOptions),
            cancellationToken);

        var summary = (await new JsonAgentSessionStore(_directory).ListAsync(cancellationToken)).Single();

        summary.Title.Should().Be("Old thread");
        summary.MessageCount.Should().Be(2);
        summary.UpdatedAt.Should().Be(legacy.updatedAt);
        summary.ModelId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ForgetsTitleAndModel()
    {
        var store = new JsonAgentSessionStore(_directory);
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.SaveAsync("d1", [new ChatMessage(ChatRole.User, "x")], cancellationToken);
        await store.RenameAsync("d1", "Named", cancellationToken);
        await store.SetModelAsync("d1", "model-a", cancellationToken);

        await store.DeleteAsync("d1", cancellationToken);
        await store.SaveAsync("d1", [new ChatMessage(ChatRole.User, "again")], cancellationToken);

        var summary = (await store.GetSummaryAsync("d1", cancellationToken))!;
        summary.Title.Should().Be("again");
        summary.ModelId.Should().BeNull();
    }
}
