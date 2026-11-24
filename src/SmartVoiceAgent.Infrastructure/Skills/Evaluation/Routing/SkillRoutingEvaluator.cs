using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;
using SmartVoiceAgent.Infrastructure.Skills.Execution;
using SmartVoiceAgent.Infrastructure.Skills.Planning;

namespace SmartVoiceAgent.Infrastructure.Skills.Evaluation.Routing;

/// <summary>
/// Asks a model to route one request to a skill, either as a JSON plan or as a native tool call,
/// and scores the answer. Nothing is executed.
/// </summary>
public sealed class SkillRoutingEvaluator
{
    private const int MaxAttempts = 3;
    private const int MaxToolSteps = 4;
    // Plain success text: telling the model the result is fake changes what it does next.
    private const string SimulatedResult = "OK";

    private readonly InMemorySkillRegistry _registry = new();
    private readonly Dictionary<string, KamSkillManifest> _byToolName = new(StringComparer.Ordinal);
    private readonly List<AITool> _tools = [];
    private readonly DateTimeOffset _now;
    private readonly TimeSpan _retryDelay;

    /// <summary>
    /// Creates the evaluator over a fixed set of skills. Both modes see the same skills.
    /// </summary>
    /// <param name="skills">Skills offered to the model.</param>
    /// <param name="now">Time written into the agent system prompt, fixed so runs are comparable.</param>
    /// <param name="retryDelay">Wait between retries of a failed provider call.</param>
    public SkillRoutingEvaluator(
        IEnumerable<KamSkillManifest> skills,
        DateTimeOffset now,
        TimeSpan? retryDelay = null)
    {
        _now = now;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);

        foreach (var manifest in skills.OrderBy(skill => skill.Id, StringComparer.Ordinal))
        {
            _registry.Register(manifest);
            var name = SkillToolProvider.ToToolName(manifest.Id);
            _byToolName[name] = manifest;
            _tools.Add(AIFunctionFactory.CreateDeclaration(
                name,
                SkillToolProvider.BuildDescription(manifest),
                SkillToolProvider.BuildSchema(manifest)));
        }
    }

    /// <summary>Number of skills offered.</summary>
    public int SkillCount => _tools.Count;

    /// <summary>
    /// Runs one case.
    /// </summary>
    /// <param name="client">The model to ask.</param>
    /// <param name="model">Model name for the report.</param>
    /// <param name="testCase">The request and what it should route to.</param>
    /// <param name="mode">JSON plan or tool call.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public async Task<SkillRoutingAttempt> RunAsync(
        IChatClient client,
        string model,
        SkillRoutingCase testCase,
        SkillRoutingMode mode,
        CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var usage = new UsageRecordingChatClient(client);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var choice = mode == SkillRoutingMode.JsonPlan
                    ? await RunJsonPlanAsync(usage, testCase, cancellationToken)
                    : await RunToolCallAsync(usage, testCase, cancellationToken);
                stopwatch.Stop();

                return Score(testCase, model, mode, choice, stopwatch.ElapsedMilliseconds, usage.Usage);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(_retryDelay, cancellationToken);
                }
            }
        }

        return new SkillRoutingAttempt
        {
            CaseId = testCase.Id,
            Language = testCase.Language,
            Model = model,
            Mode = mode,
            TransportError = true,
            Error = OneLine(lastError?.Message)
        };
    }

    private async Task<Choice> RunJsonPlanAsync(
        IChatClient client,
        SkillRoutingCase testCase,
        CancellationToken cancellationToken)
    {
        // The production planner, unchanged: its prompt, parser and validation.
        var planner = new ModelSkillPlannerService(client, _registry);
        var result = await planner.CreatePlanAsync(testCase.Request, cancellationToken);

        var expected = testCase.ExpectedSkills.Contains(result.Plan?.SkillId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        return new Choice(
            result.Plan,
            result.IsValid,
            result.IsValid ? string.Empty : result.ErrorMessage ?? "Invalid plan.",
            ToolCallCount: 0,
            Steps: 1,
            FirstCallCorrect: expected);
    }

    private async Task<Choice> RunToolCallAsync(
        IChatClient client,
        SkillRoutingCase testCase,
        CancellationToken cancellationToken)
    {
        // The same prompt and tool options the agent loop uses.
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, AgentSystemPrompt.Build(_now, _tools.Count)),
            new(ChatRole.User, testCase.Request)
        };
        var options = new ChatOptions
        {
            Tools = _tools,
            ToolMode = ChatToolMode.Auto,
            AllowMultipleToolCalls = true
        };
        var expected = testCase.ExpectedSkills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var called = new List<string>();
        string? firstSkill = null;

        // Like the agent loop, a model may look before it acts (files_exists, then files_delete).
        // Calls that are not the expected skill get a simulated success and the model goes on.
        for (var step = 1; step <= MaxToolSteps; step++)
        {
            var response = await client.GetResponseAsync(history, options, cancellationToken);
            var calls = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (calls.Count == 0)
            {
                var reason = step == 1
                    ? "No tool call: " + OneLine(response.Text)
                    : $"Stopped after calling {string.Join(", ", called)}: {OneLine(response.Text)}";
                return Failed(firstSkill, reason, called.Count, step);
            }

            var names = calls.Select(call => SkillIdFor(call.Name)).ToList();
            called.AddRange(names);
            firstSkill ??= names[0];

            var match = calls.FirstOrDefault(call => expected.Contains(SkillIdFor(call.Name)));
            if (match is not null)
            {
                var manifest = _byToolName[match.Name];
                var plan = ToPlan(manifest.Id, match.Arguments);
                var validationError = SkillArgumentValidator.Validate(manifest, plan);
                return new Choice(
                    plan,
                    validationError is null,
                    validationError ?? string.Empty,
                    called.Count,
                    step,
                    FirstCallCorrect: step == 1 && expected.Contains(names[0]));
            }

            history.AddRange(response.Messages);
            history.Add(new ChatMessage(
                ChatRole.Tool,
                calls.Select(call => (AIContent)new FunctionResultContent(call.CallId, SimulatedResult)).ToList()));
        }

        return Failed(firstSkill, $"No expected skill in {MaxToolSteps} steps; called {string.Join(", ", called)}.", called.Count, MaxToolSteps);
    }

    private string SkillIdFor(string toolName)
    {
        return _byToolName.TryGetValue(toolName, out var manifest) ? manifest.Id : toolName;
    }

    private static Choice Failed(string? firstSkill, string reason, int toolCallCount, int steps)
    {
        var plan = firstSkill is null ? null : new SkillPlan { SkillId = firstSkill };
        return new Choice(plan, false, reason, toolCallCount, steps, FirstCallCorrect: false);
    }

    private static SkillPlan ToPlan(string skillId, IDictionary<string, object?>? arguments)
    {
        var plan = new SkillPlan { SkillId = skillId };
        foreach (var (key, value) in arguments ?? new Dictionary<string, object?>())
        {
            plan.Arguments[key] = value switch
            {
                JsonElement element => element.Clone(),
                null => JsonSerializer.SerializeToElement<object?>(null),
                _ => JsonSerializer.SerializeToElement(value, value.GetType())
            };
        }

        return plan;
    }

    private static SkillRoutingAttempt Score(
        SkillRoutingCase testCase,
        string model,
        SkillRoutingMode mode,
        Choice choice,
        long durationMilliseconds,
        UsageDetails? usage)
    {
        var skillId = choice.Plan?.SkillId ?? string.Empty;
        var skillCorrect = testCase.ExpectedSkills.Contains(skillId, StringComparer.OrdinalIgnoreCase);
        var argumentsCorrect = skillCorrect
            && choice.Valid
            && choice.Plan is not null
            && ArgumentsMatch(testCase.ExpectedArguments, choice.Plan.Arguments);

        return new SkillRoutingAttempt
        {
            CaseId = testCase.Id,
            Language = testCase.Language,
            Model = model,
            Mode = mode,
            ChosenSkill = skillId,
            Arguments = choice.Plan is null ? "{}" : JsonSerializer.Serialize(choice.Plan.Arguments),
            ToolCallCount = choice.ToolCallCount,
            Steps = choice.Steps,
            FirstCallCorrect = choice.FirstCallCorrect,
            Valid = choice.Valid,
            SkillCorrect = skillCorrect,
            ArgumentsCorrect = argumentsCorrect,
            Error = OneLine(choice.Error),
            DurationMilliseconds = durationMilliseconds,
            InputTokens = usage?.InputTokenCount,
            OutputTokens = usage?.OutputTokenCount
        };
    }

    /// <summary>
    /// True when every expected argument is present and matches one of its alternatives
    /// (see <see cref="SkillRoutingCase.ExpectedArguments"/>). Case, path separators and the
    /// Turkish dotted and dotless i are ignored.
    /// </summary>
    /// <param name="expected">Argument name to accepted values.</param>
    /// <param name="actual">The arguments the model gave.</param>
    public static bool ArgumentsMatch(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, JsonElement> actual)
    {
        foreach (var (name, expectedText) in expected)
        {
            var value = actual.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (value.Key is null)
            {
                return false;
            }

            var actualText = value.Value.ValueKind == JsonValueKind.String
                ? value.Value.GetString() ?? string.Empty
                : value.Value.GetRawText();

            var normalized = Normalize(actualText);
            var matched = expectedText
                .Split('|')
                .Any(alternative => alternative.StartsWith('=')
                    ? normalized == Normalize(alternative[1..])
                    : normalized.Contains(Normalize(alternative), StringComparison.Ordinal));
            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string value)
    {
        // Windows reads C:\\Users\\me as C:\Users\me, so repeated separators count as one.
        // A URL scheme (two or more letters before the colon) keeps its "//"; a drive letter does not.
        var path = Regex.Replace(value.Replace('\\', '/'), "(?<![A-Za-z]{2}:)/{2,}", "/");

        return path
            .Replace('İ', 'i')
            .Replace('I', 'i')
            .Replace('ı', 'i')
            .ToLowerInvariant()
            .Trim();
    }

    private static string OneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var line = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length <= 300 ? line : line[..300] + "...";
    }

    private sealed record Choice(
        SkillPlan? Plan,
        bool Valid,
        string Error,
        int ToolCallCount,
        int Steps,
        bool FirstCallCorrect);

    private sealed class UsageRecordingChatClient : DelegatingChatClient
    {
        public UsageRecordingChatClient(IChatClient inner)
            : base(inner)
        {
        }

        public UsageDetails? Usage { get; private set; }

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            if (response.Usage is not null)
            {
                Usage ??= new UsageDetails();
                Usage.Add(response.Usage);
            }

            return response;
        }
    }
}
