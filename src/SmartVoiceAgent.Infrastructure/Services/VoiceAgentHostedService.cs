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

    public VoiceAgentHostedService(
        IAgentOrchestrator orchestrator,
        IAgentRegistry registry,
        IAgentFactory factory,
        ILogger<VoiceAgentHostedService> logger)
    {
        _orchestrator = orchestrator;
        _registry = registry;
        _factory = factory;
        _logger = logger;
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
                    var input = Console.ReadLine();
                    var result = await _orchestrator.ExecuteAsync(input);
                    Console.WriteLine($"📝 Result: {result}");

                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error");
                    await Task.Delay(5000, stoppingToken);
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
