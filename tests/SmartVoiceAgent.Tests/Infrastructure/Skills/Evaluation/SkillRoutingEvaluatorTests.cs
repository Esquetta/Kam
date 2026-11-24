using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;
using SmartVoiceAgent.Infrastructure.Skills.Evaluation.Routing;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Evaluation;

public class SkillRoutingEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

    private static readonly SkillRoutingCase OpenSpotify = new()
    {
        Id = "open-app",
        Language = "en",
        Request = "Open Spotify",
        ExpectedSkills = ["apps.open"],
        ExpectedArguments = new Dictionary<string, string> { ["applicationName"] = "spotify" }
    };

    [Fact]
    public async Task JsonPlan_ScoresTheProductionPlannersAnswer()
    {
        var client = new ScriptedChatClient(_ => Text(
            """{"skillId":"apps.open","arguments":{"applicationName":"Spotify"},"confidence":0.9,"requiresConfirmation":false,"reasoning":"open"}"""));

        var attempt = await CreateEvaluator().RunAsync(client, "m", OpenSpotify, SkillRoutingMode.JsonPlan);

        attempt.Should().BeEquivalentTo(new
        {
            ChosenSkill = "apps.open",
            Valid = true,
            SkillCorrect = true,
            ArgumentsCorrect = true,
            TransportError = false,
            InputTokens = (long?)120,
            OutputTokens = (long?)30
        });
        client.LastOptions?.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ToolCall_OffersSkillsAsToolsAndScoresTheCall()
    {
        var client = new ScriptedChatClient(_ => Call("apps_open", new Dictionary<string, object?>
        {
            ["applicationName"] = "Spotify"
        }));

        var attempt = await CreateEvaluator().RunAsync(client, "m", OpenSpotify, SkillRoutingMode.ToolCall);

        attempt.ChosenSkill.Should().Be("apps.open");
        attempt.ArgumentsCorrect.Should().BeTrue();
        attempt.ToolCallCount.Should().Be(1);
        client.LastOptions!.Tools!.Select(tool => tool.Name).Should().Contain(["apps_open", "apps_close"]);
        client.LastOptions.ToolMode.Should().Be(ChatToolMode.Auto);
    }

    [Fact]
    public async Task ToolCall_WithoutACall_IsInvalid()
    {
        var client = new ScriptedChatClient(_ => Text("Which Spotify do you mean?"));

        var attempt = await CreateEvaluator().RunAsync(client, "m", OpenSpotify, SkillRoutingMode.ToolCall);

        attempt.Valid.Should().BeFalse();
        attempt.SkillCorrect.Should().BeFalse();
        attempt.Error.Should().StartWith("No tool call");
    }

    [Fact]
    public async Task ToolCall_LookingFirstThenActing_CountsAsRightButNotFirstCall()
    {
        var steps = 0;
        var client = new ScriptedChatClient(messages =>
        {
            steps++;
            return steps == 1
                ? Call("apps_check", new Dictionary<string, object?> { ["applicationName"] = "Spotify" })
                : Call("apps_open", new Dictionary<string, object?> { ["applicationName"] = "Spotify" });
        });

        var attempt = await CreateEvaluator().RunAsync(client, "m", OpenSpotify, SkillRoutingMode.ToolCall);

        attempt.ArgumentsCorrect.Should().BeTrue();
        attempt.FirstCallCorrect.Should().BeFalse();
        attempt.Steps.Should().Be(2);
        attempt.ToolCallCount.Should().Be(2);
        attempt.InputTokens.Should().Be(240);
        client.LastMessages.Should().Contain(message => message.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task ToolCall_NeverReachingTheSkill_FailsAfterFourSteps()
    {
        var client = new ScriptedChatClient(_ => Call("apps_check", new Dictionary<string, object?> { ["applicationName"] = "Spotify" }));

        var attempt = await CreateEvaluator().RunAsync(client, "m", OpenSpotify, SkillRoutingMode.ToolCall);

        attempt.SkillCorrect.Should().BeFalse();
        attempt.ChosenSkill.Should().Be("apps.check");
        attempt.Steps.Should().Be(4);
        attempt.Error.Should().StartWith("No expected skill in 4 steps");
    }

    [Fact]
    public async Task ToolCall_MissingRequiredArgument_PicksTheSkillButFailsArguments()
    {
        var client = new ScriptedChatClient(_ => Call("apps_open", new Dictionary<string, object?>()));

        var attempt = await CreateEvaluator().RunAsync(client, "m", OpenSpotify, SkillRoutingMode.ToolCall);

        attempt.SkillCorrect.Should().BeTrue();
        attempt.Valid.Should().BeFalse();
        attempt.ArgumentsCorrect.Should().BeFalse();
        attempt.Error.Should().Contain("applicationName");
    }

    [Fact]
    public async Task ProviderFailures_AreRetriedThenMarked()
    {
        var calls = 0;
        var client = new ScriptedChatClient(_ =>
        {
            calls++;
            throw new HttpRequestException("rate limited");
        });

        var attempt = await CreateEvaluator().RunAsync(client, "m", OpenSpotify, SkillRoutingMode.ToolCall);

        calls.Should().Be(3);
        attempt.TransportError.Should().BeTrue();
        attempt.Error.Should().Be("rate limited");
    }

    [Theory]
    [InlineData("filePath", "c:/notes/todo.txt", "C:\\Notes\\TODO.txt", true)]
    [InlineData("filePath", "downloads/report.pdf", "C:\\\\Users\\\\me\\\\Downloads\\\\report.pdf", true)]
    [InlineData("url", "example.com/pricing", "https://example.com/pricing", true)]
    [InlineData("subject", "toplanti", "Toplantı", true)]
    [InlineData("count", "5", 5, true)]
    [InlineData("applicationName", "spotify", "chrome", false)]
    [InlineData("action", "=increase|=up", "up", true)]
    [InlineData("action", "=increase|=up", "volume_up", false)]
    public void ArgumentsMatch_IgnoresCaseSeparatorsAndTurkishI(string name, string expected, object actual, bool matches)
    {
        var arguments = new Dictionary<string, JsonElement> { [name] = JsonSerializer.SerializeToElement(actual) };

        SkillRoutingEvaluator
            .ArgumentsMatch(new Dictionary<string, string> { [name] = expected }, arguments)
            .Should().Be(matches);
    }

    [Fact]
    public void Report_ShowsOneRowPerModelAndMode()
    {
        var attempts = new[]
        {
            new SkillRoutingAttempt { Model = "m", Mode = SkillRoutingMode.JsonPlan, Language = "en", Valid = true, SkillCorrect = true, ArgumentsCorrect = true, DurationMilliseconds = 100 },
            new SkillRoutingAttempt { Model = "m", Mode = SkillRoutingMode.JsonPlan, Language = "tr", CaseId = "x", ChosenSkill = "apps.close", Valid = true, DurationMilliseconds = 300 },
            new SkillRoutingAttempt { Model = "m", Mode = SkillRoutingMode.ToolCall, Language = "en", TransportError = true, Error = "timeout" }
        };

        var markdown = SkillRoutingReport.ToMarkdown(attempts, ["date: test"]);

        markdown.Should().Contain("| `m` | JSON plan | 2 | 50% | 50% | 0% | 100% | 0% | 0.0 | 200 |");
        markdown.Should().Contain("| `m` | tool call | 0 |");
        markdown.Should().Contain("| x.tr | apps.close |");
        markdown.Should().Contain("provider error: timeout");
    }

    [Fact]
    public void CasesFile_OnlyNamesBuiltInSkillsAndTheirArguments()
    {
        var cases = SkillRoutingCase.ParseAll(File.ReadAllText(FindCasesFile()));
        var skills = BuiltInSkillManifestCatalog.CreateAll().ToDictionary(skill => skill.Id);

        cases.Should().NotBeEmpty();
        cases.GroupBy(testCase => testCase.Id)
            .Should().OnlyContain(group => group.Select(testCase => testCase.Language).Order().SequenceEqual(new[] { "en", "tr" }));

        foreach (var testCase in cases)
        {
            foreach (var skillId in testCase.ExpectedSkills)
            {
                skills.Should().ContainKey(skillId, $"case {testCase.Id}.{testCase.Language} expects it");
                testCase.ExpectedArguments.Keys
                    .Should().BeSubsetOf(
                        skills[skillId].Arguments.Select(argument => argument.Name),
                        $"case {testCase.Id} checks these on {skillId}");
            }
        }
    }

    private static SkillRoutingEvaluator CreateEvaluator()
    {
        var skills = BuiltInSkillManifestCatalog.CreateAll().Where(skill => skill.Id.StartsWith("apps.", StringComparison.Ordinal));
        return new SkillRoutingEvaluator(skills, Now, TimeSpan.Zero);
    }

    private static ChatResponse Text(string text)
    {
        return WithUsage(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    private static ChatResponse Call(string name, IDictionary<string, object?> arguments)
    {
        return WithUsage(new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent("call-1", name, arguments)])));
    }

    private static ChatResponse WithUsage(ChatResponse response)
    {
        response.Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 30 };
        return response;
    }

    private static string FindCasesFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "evals", "skill-routing", "cases.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("evals/skill-routing/cases.json not found above the test output folder.");
    }

    private sealed class ScriptedChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatResponse> _respond;

        public ScriptedChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> respond)
        {
            _respond = respond;
        }

        public ChatOptions? LastOptions { get; private set; }

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            LastMessages = messages.ToList();
            return Task.FromResult(_respond(messages));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
