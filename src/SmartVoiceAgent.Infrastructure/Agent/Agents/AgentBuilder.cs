using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using System.Reflection;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public class AgentBuilder : IAgentBuilder
{
    private readonly IChatClient _chatClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentBuilder>? _logger;
    private string _name = "Agent";
    private string _instructions = "You are a helpful assistant.";
    private readonly List<AIFunction> _tools = new();

    public AgentBuilder(
        IChatClient chatClient,
        IServiceProvider serviceProvider,
        ILogger<AgentBuilder>? logger = null)
    {
        _chatClient = chatClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IAgentBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public IAgentBuilder WithInstructions(string instructions)
    {
        _instructions = instructions;
        return this;
    }

    /// <summary>
    /// Synchronous tool loading - Use WithToolsAsync instead to avoid blocking
    /// This method blocks if InitializeAsync is found - can cause deadlocks in UI/ASP.NET contexts
    /// </summary>
    [Obsolete("Use WithToolsAsync<TToolClass>() instead to avoid blocking. This method may cause deadlocks.")]
    public IAgentBuilder WithTools<TToolClass>() where TToolClass : class
    {
        _logger?.LogWarning("Using synchronous WithTools() - consider using WithToolsAsync() to avoid blocking");

        try
        {
            // Get tool class instance from DI
            var toolInstance = _serviceProvider.GetRequiredService<TToolClass>();

            // Try to initialize asynchronously if InitializeAsync exists
            var initMethod = typeof(TToolClass).GetMethod(
                "InitializeAsync",
                BindingFlags.Public | BindingFlags.Instance);

            if (initMethod != null &&
                typeof(Task).IsAssignableFrom(initMethod.ReturnType))
            {
                _logger?.LogDebug("Found InitializeAsync on {ToolClass}, calling it synchronously",
                    typeof(TToolClass).Name);

                var task = (Task)initMethod.Invoke(toolInstance, null)!;

                // WARNING: This blocks and can cause deadlocks
                // Use Task.Run to run on thread pool to reduce deadlock risk
                Task.Run(async () => await task).GetAwaiter().GetResult();

                _logger?.LogDebug("InitializeAsync completed for {ToolClass}",
                    typeof(TToolClass).Name);
            }

            // Get tools from the GetTools() method
            var getToolsMethod = typeof(TToolClass).GetMethod(
                "GetTools",
                BindingFlags.Public | BindingFlags.Instance);

            if (getToolsMethod != null)
            {
                var tools = getToolsMethod.Invoke(toolInstance, null) as IEnumerable<AIFunction>;
                if (tools != null)
                {
                    var toolList = tools.ToList();
                    _tools.AddRange(toolList);

                    _logger?.LogInformation(
                        "Loaded {Count} tools from {ToolClass}",
                        toolList.Count,
                        typeof(TToolClass).Name);
                }
                else
                {
                    _logger?.LogWarning(
                        "GetTools() returned null for {ToolClass}",
                        typeof(TToolClass).Name);
                }
            }
            else
            {
                _logger?.LogWarning(
                    "No GetTools() method found on {ToolClass}",
                    typeof(TToolClass).Name);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Failed to load tools from {ToolClass}",
                typeof(TToolClass).Name);
            throw new InvalidOperationException(
                $"Failed to load tools from {typeof(TToolClass).Name}. " +
                $"Ensure the class has a public parameterless GetTools() method. " +
                $"Error: {ex.Message}",
                ex);
        }

        return this;
    }

    /// <summary>
    /// Asynchronous tool loading - preferred for tools with async initialization
    /// Use this when you can await the result
    /// </summary>
    public async Task<IAgentBuilder> WithToolsAsync<TToolClass>() where TToolClass : class
    {
        _logger?.LogDebug("Loading tools asynchronously from {ToolClass}",
            typeof(TToolClass).Name);

        try
        {
            // Get tool class instance from DI
            var toolInstance = _serviceProvider.GetRequiredService<TToolClass>();

            // Try to initialize asynchronously if InitializeAsync exists
            var initMethod = typeof(TToolClass).GetMethod(
                "InitializeAsync",
                BindingFlags.Public | BindingFlags.Instance);

            if (initMethod != null &&
                typeof(Task).IsAssignableFrom(initMethod.ReturnType))
            {
                _logger?.LogDebug("Found InitializeAsync on {ToolClass}, awaiting it",
                    typeof(TToolClass).Name);

                var task = (Task)initMethod.Invoke(toolInstance, null)!;

                // Properly await the initialization
                await task.ConfigureAwait(false);

                _logger?.LogDebug("InitializeAsync completed for {ToolClass}",
                    typeof(TToolClass).Name);
            }

            // Get tools from the GetTools() method
            var getToolsMethod = typeof(TToolClass).GetMethod(
                "GetTools",
                BindingFlags.Public | BindingFlags.Instance);

            if (getToolsMethod != null)
            {
                var tools = getToolsMethod.Invoke(toolInstance, null) as IEnumerable<AIFunction>;
                if (tools != null)
                {
                    var toolList = tools.ToList();
                    _tools.AddRange(toolList);

                    _logger?.LogInformation(
                        "Loaded {Count} tools asynchronously from {ToolClass}",
                        toolList.Count,
                        typeof(TToolClass).Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Failed to load tools asynchronously from {ToolClass}",
                typeof(TToolClass).Name);
            throw new InvalidOperationException(
                $"Failed to load tools from {typeof(TToolClass).Name}. Error: {ex.Message}",
                ex);
        }

        return this;
    }

    public IAgentBuilder WithTools(IEnumerable<AIFunction> tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    public AIAgent Build()
    {
        _logger?.LogInformation(
            "Building agent '{AgentName}' with {ToolCount} tools",
            _name,
            _tools.Count);

        return _chatClient.CreateAIAgent(
            name: _name,
            instructions: _instructions,
            tools: _tools.ToArray()
        );
    }

    public async Task<AIAgent> BuildAsync()
    {
        _logger?.LogInformation(
            "Building agent '{AgentName}' asynchronously with {ToolCount} tools",
            _name,
            _tools.Count);

        // For now, just wraps Build() in a Task
        // Can be extended for async agent creation if needed
        return await Task.Run(() => Build());
    }
}