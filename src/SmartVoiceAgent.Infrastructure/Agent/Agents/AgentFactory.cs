using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Agent.Functions;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public class AgentFactory : IAgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentFactory> _logger;

    public AgentFactory(
        IChatClient chatClient,
        IServiceProvider serviceProvider,
        ILogger<AgentFactory> logger)
    {
        _chatClient = chatClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public AIAgent CreateSystemAgent()
    {
        _logger.LogInformation("Creating SystemAgent...");

        var instructions = @"You are a System Agent responsible for desktop operations and device control.

Your capabilities:
- Opening and closing desktop applications
- Controlling system devices (volume, wifi, bluetooth)
- Playing media and music
- Managing application states
- Checking application information and paths

Always:
- Confirm critical actions before executing
- Provide clear feedback on operations
- Check application state before attempting operations
- Use Turkish for responses when appropriate";

        return new AgentBuilder(_chatClient, _serviceProvider)
            .WithName("SystemAgent")
            .WithInstructions(instructions)
            .WithTools<SystemAgentTools>()
            .Build();
    }

    public AIAgent CreateTaskAgent()
    {
        _logger.LogInformation("Creating TaskAgent...");

        var instructions = @"You are a Task Agent specialized in task and schedule management.

Your capabilities:
- Creating and organizing tasks
- Managing task priorities
- Setting reminders and deadlines
- Tracking task completion
- Providing task lists and summaries

Always:
- Help users prioritize effectively
- Suggest improvements to workflow
- Keep tasks organized and clear
- Use Turkish for responses when appropriate";

        return new AgentBuilder(_chatClient, _serviceProvider)
            .WithName("TaskAgent")
            .WithInstructions(instructions)
            .WithTools<TaskAgentTools>()
            .Build();
    }

    public AIAgent CreateResearchAgent()
    {
        _logger.LogInformation("Creating ResearchAgent...");

        var instructions = @"You are a Research Agent specialized in information gathering and analysis.

Your capabilities:
- Searching the web for information
- Analyzing documents and content
- Summarizing research findings
- Comparing different sources
- Providing citations and references

Always:
- Verify information from multiple sources
- Provide accurate, well-researched answers
- Include relevant citations
- Use Turkish for responses when appropriate";

        return new AgentBuilder(_chatClient, _serviceProvider)
            .WithName("ResearchAgent")
            .WithInstructions(instructions)
            .WithTools<WebSearchAgentTools>()
            .Build();
    }

    public AIAgent CreateCoordinatorAgent()
    {
        _logger.LogInformation("Creating CoordinatorAgent...");

        var instructions = @"You are the Coordinator Agent responsible for orchestrating multiple specialized agents.

Available agents and their capabilities:
- SystemAgent: Desktop operations, application management, device control
- TaskAgent: Task management, scheduling, reminders
- ResearchAgent: Information gathering, research, analysis

Your role:
1. Analyze user requests and break them into subtasks
2. Determine which agent(s) should handle each part
3. Coordinate workflow between agents
4. Synthesize results from multiple agents
5. Ensure task completion and quality

When routing requests:
- System operations → SystemAgent
- Task management → TaskAgent
- Information gathering → ResearchAgent
- Complex requests → Multiple agents in sequence

Always explain your reasoning and keep users informed.";

        return new AgentBuilder(_chatClient, _serviceProvider)
            .WithName("CoordinatorAgent")
            .WithInstructions(instructions)
            .Build();
    }

    public IAgentBuilder CreateCustomAgent()
    {
        return new AgentBuilder(_chatClient, _serviceProvider);
    }
}