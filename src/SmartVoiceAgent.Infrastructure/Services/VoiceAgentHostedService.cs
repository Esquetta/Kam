using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services
{
    public class VoiceAgentHostedService : BackgroundService
    {
        private readonly ICommandRuntimeService _commandRuntime;
        private readonly ILogger<VoiceAgentHostedService> _logger;
        private readonly ICommandInputService _commandInput;
        private readonly VoiceAgentHostControlService _hostControl;

        public VoiceAgentHostedService(
            ICommandRuntimeService commandRuntime,
            ILogger<VoiceAgentHostedService> logger,
            ICommandInputService commandInput,
            VoiceAgentHostControlService hostControl)
        {
            _commandRuntime = commandRuntime;
            _logger = logger;
            _commandInput = commandInput;
            _hostControl = hostControl;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Voice Agent Service starting...");

            try
            {
                _logger.LogInformation("Ready for commands...");

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!_hostControl.ShouldProcess())
                        {
                            await Task.Delay(500, stoppingToken);
                            continue;
                        }

                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            stoppingToken,
                            _hostControl.GetCancellationToken());

                        var input = await _commandInput.ReadCommandAsync(linkedCts.Token);

                        if (!_hostControl.ShouldProcess())
                        {
                            _logger.LogInformation("Voice Agent Host is paused, skipping command");
                            continue;
                        }

                        _logger.LogInformation("Processing command: {Command}", input);

                        var result = await _commandRuntime.ExecuteAsync(input, linkedCts.Token);

                        _logger.LogInformation("Result: {Result}", result.Message);

                        _commandInput.PublishResult(
                            input,
                            result.Message,
                            result.Success || result.RequiresConfirmation);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!_hostControl.ShouldProcess() && !stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("Voice Agent Host paused");
                            continue;
                        }

                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing command");
                        _commandInput.PublishResult("unknown", ex.Message, false);
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Fatal error");
                throw;
            }
        }
    }
}
