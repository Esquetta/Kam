using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public sealed class InMemoryRuntimeAgentRunStore : IRuntimeAgentRunStore
{
    private const int MaxStoredRuns = 200;

    private readonly object _gate = new();
    private readonly List<RuntimeAgentRun> _runs = [];

    public RuntimeAgentRun Start(RuntimeAgentRequest request, string modelId)
    {
        var run = new RuntimeAgentRun(
            CreateRunId(),
            request.AgentName,
            request.Role,
            request.UserRequest,
            modelId,
            RuntimeAgentRunStatus.Running,
            DateTimeOffset.UtcNow,
            LastMessage: "Created automatically for this request.",
            ToolObservations: request.ToolObservations?.ToArray());

        lock (_gate)
        {
            _runs.Insert(0, run);
            Trim();
        }

        return run;
    }

    public RuntimeAgentRun Complete(string runId, string response)
    {
        return Update(
            runId,
            run => run with
            {
                Status = RuntimeAgentRunStatus.Succeeded,
                CompletedAt = DateTimeOffset.UtcNow,
                LastMessage = "Completed.",
                Response = response,
                ErrorMessage = null
            });
    }

    public RuntimeAgentRun Fail(string runId, string errorMessage)
    {
        return Update(
            runId,
            run => run with
            {
                Status = RuntimeAgentRunStatus.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                LastMessage = "Failed.",
                ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Runtime agent failed." : errorMessage.Trim()
            });
    }

    public RuntimeAgentRun Cancel(string runId, string reason)
    {
        return Update(
            runId,
            run => run with
            {
                Status = RuntimeAgentRunStatus.Canceled,
                CompletedAt = DateTimeOffset.UtcNow,
                LastMessage = "Canceled.",
                ErrorMessage = string.IsNullOrWhiteSpace(reason) ? "Runtime agent run was canceled." : reason.Trim()
            });
    }

    public RuntimeAgentRun? Get(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        lock (_gate)
        {
            return _runs.FirstOrDefault(run => run.RunId.Equals(runId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<RuntimeAgentRun> List(int maxCount = 50)
    {
        lock (_gate)
        {
            return _runs
                .Take(Math.Clamp(maxCount, 1, MaxStoredRuns))
                .ToArray();
        }
    }

    private RuntimeAgentRun Update(string runId, Func<RuntimeAgentRun, RuntimeAgentRun> update)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run id is required.", nameof(runId));
        }

        lock (_gate)
        {
            var index = _runs.FindIndex(run => run.RunId.Equals(runId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException($"Runtime agent run '{runId}' was not found.");
            }

            var updated = update(_runs[index]);
            _runs[index] = updated;
            return updated;
        }
    }

    private void Trim()
    {
        while (_runs.Count > MaxStoredRuns)
        {
            _runs.RemoveAt(_runs.Count - 1);
        }
    }

    private static string CreateRunId()
    {
        return $"run_{Guid.NewGuid():N}"[..20];
    }
}
