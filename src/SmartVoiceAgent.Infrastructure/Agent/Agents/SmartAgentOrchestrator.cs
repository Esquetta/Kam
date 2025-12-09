using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Dtos.Agent;
using SmartVoiceAgent.Core.Enums.Agent;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public class SmartAgentOrchestrator : IAgentOrchestrator
{
    private readonly IAgentRegistry _registry;
    private readonly AIAgent _routerAgent;
    private readonly ILogger<SmartAgentOrchestrator> _logger;

    public SmartAgentOrchestrator(
        IAgentRegistry registry,
        ILogger<SmartAgentOrchestrator> logger)
    {
        _registry = registry;
        _logger = logger;
        _routerAgent = _registry.GetAgent("RouterAgent");
    }

    public async Task<string> ExecuteAsync(string userRequest)
    {
        _logger.LogInformation("🎬 Processing: {Request}", userRequest);

        // Step 1: Router agent analyzes and decides
        var routingDecision = await GetRoutingDecisionAsync(userRequest);

        _logger.LogInformation("📊 Route: {Agents} ({Mode})",
            string.Join(", ", routingDecision.TargetAgents),
            routingDecision.ExecutionMode);

        // Step 2: Execute based on decision
        var results = routingDecision.ExecutionMode == ExecutionMode.Parallel
            ? await ExecuteParallelAsync(routingDecision.TargetAgents, userRequest)
            : await ExecuteSequentialAsync(routingDecision.TargetAgents, userRequest);

        // Step 3: Combine results
        return CombineResults(results);
    }

    public async IAsyncEnumerable<AgentExecutionUpdate> ExecuteStreamAsync(string userRequest)
    {
        _logger.LogInformation("🎬 Streaming: {Request}", userRequest);

        // Step 1: Get routing decision
        var routingDecision = await GetRoutingDecisionAsync(userRequest);

        yield return new AgentExecutionUpdate
        {
            AgentName = "Router",
            Message = $"Routing to: {string.Join(", ", routingDecision.TargetAgents)}",
            IsComplete = true
        };

        // Step 2: Execute agents
        if (routingDecision.ExecutionMode == ExecutionMode.Parallel)
        {
            await foreach (var update in StreamParallelAsync(routingDecision.TargetAgents, userRequest))
            {
                yield return update;
            }
        }
        else
        {
            await foreach (var update in StreamSequentialAsync(routingDecision.TargetAgents, userRequest))
            {
                yield return update;
            }
        }
    }

    private async Task<RoutingDecision> GetRoutingDecisionAsync(string userRequest)
    {
        var routerMessage = new ChatMessage(ChatRole.User, userRequest);
        var routerResponse = await _routerAgent.RunAsync(new[] { routerMessage });

        var responseText = routerResponse.Messages.First().Text ?? "";
        return ParseRoutingDecision(responseText);
    }

    private RoutingDecision ParseRoutingDecision(string agentResponse)
    {
        var response = agentResponse.ToLowerInvariant();
        var agents = new List<string>();
        var mode = ExecutionMode.Sequential;

        // Extract agent names
        if (response.Contains("systemagent")) agents.Add("SystemAgent");
        if (response.Contains("taskagent")) agents.Add("TaskAgent");
        if (response.Contains("researchagent")) agents.Add("ResearchAgent");

        // Determine execution mode
        if (response.Contains("parallel") || response.Contains("simultaneously") ||
            response.Contains("aynı anda") || agents.Count > 1)
        {
            mode = ExecutionMode.Parallel;
        }

        // Fallback
        if (agents.Count == 0)
        {
            agents.Add("SystemAgent");
        }

        return new RoutingDecision
        {
            TargetAgents = agents,
            ExecutionMode = mode,
            Reasoning = agentResponse
        };
    }

    private async Task<Dictionary<string, string>> ExecuteParallelAsync(
        List<string> agentNames,
        string userRequest)
    {
        _logger.LogInformation("🔀 Executing {Count} agents in parallel", agentNames.Count);

        var tasks = agentNames.Select(async agentName =>
        {
            var agent = _registry.GetAgent(agentName);
            var message = new ChatMessage(ChatRole.User, userRequest);
            var response = await agent.RunAsync(new[] { message });
            return (AgentName: agentName, Result: response.Messages.First().Text ?? "");
        });

        var results = await Task.WhenAll(tasks);

        return results.ToDictionary(r => r.AgentName, r => r.Result);
    }

    private async Task<Dictionary<string, string>> ExecuteSequentialAsync(
        List<string> agentNames,
        string userRequest)
    {
        _logger.LogInformation("➡️ Executing {Count} agents sequentially", agentNames.Count);

        var results = new Dictionary<string, string>();
        var contextMessages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, userRequest)
            };

        foreach (var agentName in agentNames)
        {
            var agent = _registry.GetAgent(agentName);
            var response = await agent.RunAsync(contextMessages);
            var resultText = response.Messages.First().Text ?? "";

            results[agentName] = resultText;

            // Add response to context for next agent
            contextMessages.Add(new ChatMessage(ChatRole.Assistant, resultText));
        }

        return results;
    }

    private async IAsyncEnumerable<AgentExecutionUpdate> StreamParallelAsync(
        List<string> agentNames,
        string userRequest)
    {
        var tasks = agentNames.Select(async agentName =>
        {
            var agent = _registry.GetAgent(agentName);
            var message = new ChatMessage(ChatRole.User, userRequest);

            var updates = new List<AgentExecutionUpdate>();

            await foreach (var update in agent.RunStreamingAsync(new[] { message }))
            {
                if (update.Contents != null)
                {
                    var text = string.Join("", update.Contents
                        .OfType<TextContent>()
                        .Select(c => c.Text));

                    updates.Add(new AgentExecutionUpdate
                    {
                        AgentName = agentName,
                        Message = text,
                        IsComplete = false
                    });
                }
            }

            updates.Add(new AgentExecutionUpdate
            {
                AgentName = agentName,
                Message = "",
                IsComplete = true
            });

            return updates;
        });

        var allUpdates = await Task.WhenAll(tasks);

        foreach (var updates in allUpdates)
        {
            foreach (var update in updates)
            {
                yield return update;
            }
        }
    }

    private async IAsyncEnumerable<AgentExecutionUpdate> StreamSequentialAsync(
        List<string> agentNames,
        string userRequest)
    {
        var contextMessages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, userRequest)
            };

        foreach (var agentName in agentNames)
        {
            var agent = _registry.GetAgent(agentName);
            var fullResponse = new System.Text.StringBuilder();

            await foreach (var update in agent.RunStreamingAsync(contextMessages))
            {
                if (update.Contents != null)
                {
                    var text = string.Join("", update.Contents
                        .OfType<TextContent>()
                        .Select(c => c.Text));

                    fullResponse.Append(text);

                    yield return new AgentExecutionUpdate
                    {
                        AgentName = agentName,
                        Message = text,
                        IsComplete = false
                    };
                }
            }

            yield return new AgentExecutionUpdate
            {
                AgentName = agentName,
                Message = "",
                IsComplete = true
            };

            // Add to context for next agent
            contextMessages.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
        }
    }

    private string CombineResults(Dictionary<string, string> results)
    {
        if (results.Count == 1)
        {
            return results.Values.First();
        }

        var combined = new System.Text.StringBuilder();
        foreach (var result in results)
        {
            combined.AppendLine($"[{result.Key}]:");
            combined.AppendLine(result.Value);
            combined.AppendLine();
        }
        return combined.ToString();
    }

}

