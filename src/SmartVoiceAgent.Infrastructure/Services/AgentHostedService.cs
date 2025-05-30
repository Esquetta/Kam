using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services
{
    /// <summary>
    ///  A hosted background service that continuously listens for voice commands,
    ///  detects intents and dispatches them to the appropriate command handlers.
    /// </summary>
    public class AgentHostedService: BackgroundService
    {
        private readonly IVoiceRecognitionService _voiceRecognition;
        private readonly IIntentDetector _intentDetector;
        private readonly ICommandBus _commandBus;
        private readonly IQueryBus _queryBus;
        private readonly ILogger<AgentHostedService> _logger;

        public AgentHostedService(IVoiceRecognitionService voiceRecognition, IIntentDetector intentDetector, ICommandBus commandBus, IQueryBus queryBus, ILogger<AgentHostedService> logger)
        {
            _voiceRecognition = voiceRecognition;
            _intentDetector = intentDetector;
            _commandBus = commandBus;
            _queryBus = queryBus;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AgentHostedService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 1. Dinle
                    var voiceText = await _voiceRecognition.ListenAsync(stoppingToken);
                    _logger.LogInformation("Heard: {0}", voiceText);

                    // 2. Intent tespit et
                    var intent = await _intentDetector.DetectIntentAsync(voiceText);
                    _logger.LogInformation("Detected intent: {0}", intent);

                    // 3. Komut yarat ve bus üzerinden gönder
                    switch (intent)
                    {
                        case CommandType.OpenApplication:
                            await _commandBus.SendAsync<OpenApplicationCommand, CommandResultDTO>(
                                new OpenApplicationCommand(voiceText));
                            break;

                        case CommandType.PlayMusic:
                            await _commandBus.SendAsync<PlayMusicCommand, CommandResultDTO>(
                                new PlayMusicCommand(voiceText));
                            break;

                        case CommandType.SearchWeb:
                            await _commandBus.SendAsync<SearchWebCommand, CommandResultDTO>(
                                new SearchWebCommand(voiceText));
                            break;

                        case CommandType.SendMessage:
                            // Örnek: "Smith'e mesaj gönder Merhaba" → pars et
                            var parts = voiceText.Split(" mesaj ");
                            if (parts.Length == 2)
                                await _commandBus.SendAsync<SendMessageCommand, CommandResultDTO>(
                                    new SendMessageCommand(parts[0], parts[1]));
                            break;

                        case CommandType.ControlDevice:
                            // Örnek: "ışıkları kapat"
                            var tokens = voiceText.Split(' ', 2);
                            if (tokens.Length == 2)
                                await _commandBus.SendAsync<ControlDeviceCommand, CommandResultDTO>(
                                    new ControlDeviceCommand(tokens[0], tokens[1]));
                            break;

                        default:
                            _logger.LogWarning("No handler for intent {0}", intent);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AgentHostedService loop.");
                }

                // Küçük bir bekleme ile CPU tüketimini düşür
                await Task.Delay(500, stoppingToken);
            }

            _logger.LogInformation("AgentHostedService stopping.");
        }
    }
}
