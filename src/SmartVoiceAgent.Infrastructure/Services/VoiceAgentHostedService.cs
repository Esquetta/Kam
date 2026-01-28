using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

public class VoiceAgentHostedService : BackgroundService
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IAgentRegistry _registry;
    private readonly IAgentFactory _factory;
    private readonly ILogger<VoiceAgentHostedService> _logger;
    private readonly ICommandInputService _commandInput;

    public VoiceAgentHostedService(
        IAgentOrchestrator orchestrator,
        IAgentRegistry registry,
        IAgentFactory factory,
        ILogger<VoiceAgentHostedService> logger,
        ICommandInputService commandInput)
    {
        _orchestrator = orchestrator;
        _registry = registry;
        _factory = factory;
        _logger = logger;
        _commandInput = commandInput;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Voice Agent Service starting...");

        try
        {
            await InitializeAgentsAsync();

            _logger.LogInformation("🎤 Ready for commands...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Read command from UI instead of Console
                    var input = await _commandInput.ReadCommandAsync(stoppingToken);
                    
                    _logger.LogInformation("📝 Processing command: {Command}", input);
                    
                    var result = await _orchestrator.ExecuteAsync(input);
                    
                    _logger.LogInformation("✅ Result: {Result}", result);
                    
                    // Publish result back to UI
                    _commandInput.PublishResult(input, result, true);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error processing command");
                    _commandInput.PublishResult("unknown", ex.Message, false);
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "💥 Fatal error");
            throw;
        }
    }

    private async Task InitializeAgentsAsync()
    {
        _logger.LogInformation("⚙️ Initializing agents...");

        _registry.RegisterAgent("Coordinator", _factory.CreateCoordinatorAgent());
        _registry.RegisterAgent("SystemAgent", _factory.CreateSystemAgent());
        _registry.RegisterAgent("TaskAgent", await _factory.CreateTaskAgentAsync());
        _registry.RegisterAgent("ResearchAgent", _factory.CreateResearchAgent());

        _logger.LogInformation("✅ All agents ready");
        await Task.CompletedTask;
    }
}
