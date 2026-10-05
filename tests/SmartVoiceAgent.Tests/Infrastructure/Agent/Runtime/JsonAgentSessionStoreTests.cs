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
}
