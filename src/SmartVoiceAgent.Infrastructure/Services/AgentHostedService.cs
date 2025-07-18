using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Infrastructure.Services
{
    /// <summary>
    ///  A hosted background service that continuously listens for voice commands,
    ///  detects intents and dispatches them to the appropriate command handlers.
    /// </summary>
    public class AgentHostedService : BackgroundService
    {
        private readonly IIntentDetectionService _intentDetector;
        private readonly IMediator mediator;
        private readonly AudioProcessingService audioProcessingService;
        private readonly ILanguageDetectionService languageDetectionService;
        private readonly ILogger<AgentHostedService> _logger;
        private readonly IApplicationScannerServiceFactory applicationScannerServiceFactory;
        private readonly IVoiceRecognitionFactory voiceRecognitionFactory;

        private readonly IVoiceRecognitionService _voiceRecognitionService = null;
        private readonly IApplicationScanner applicationScanner;

        public AgentHostedService(IIntentDetectionService intentDetector, ICommandBus commandBus, IQueryBus queryBus, ILogger<AgentHostedService> logger, IMediator mediator, AudioProcessingService audioProcessingService, ILanguageDetectionService languageDetectionService, IApplicationScannerServiceFactory applicationScannerServiceFactory, IVoiceRecognitionFactory voiceRecognitionFactory)
        {
            _intentDetector = intentDetector;
            _logger = logger;
            this.mediator = mediator;
            this.audioProcessingService = audioProcessingService;
            this.languageDetectionService = languageDetectionService;
            this.applicationScannerServiceFactory = applicationScannerServiceFactory;
            this.voiceRecognitionFactory = voiceRecognitionFactory;
            this._voiceRecognitionService = voiceRecognitionFactory.Create();
            this.applicationScanner = applicationScannerServiceFactory.Create();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AgentHostedService started.");
            Console.WriteLine("Ses kaydı başlatılıyor...");

            // Voice captured event
            _voiceRecognitionService.OnVoiceCaptured += async (sender, audioData) =>
            {
                try
                {
                    Console.WriteLine($"Voice data captured. Length: {audioData.Length}");

                    var sttResult = await audioProcessingService.ProcessAudioFromRecording(audioData, stoppingToken);

                    if (sttResult.IsSuccess)
                        Console.WriteLine($"Recognized Text: {sttResult.Text}");
                    else
                        Console.WriteLine($"Speech-to-Text Error: {sttResult.ErrorMessage}");
                    var language = await languageDetectionService.DetectLanguageAsync(sttResult.Text, stoppingToken);
                    var intent = await _intentDetector.DetectIntentAsync(sttResult.Text, language.Language, stoppingToken);

                    Console.WriteLine($"İntent:{intent.Intent}, Language:{language.Language}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while processing captured voice data: {ex.Message}");
                }
            };

            _voiceRecognitionService.OnError += (sender, ex) =>
            {
                Console.WriteLine($"Voice recognition error: {ex.Message}");
            };

            _voiceRecognitionService.OnListeningStarted += (sender, args) =>
            {
                Console.WriteLine("Listening started.");
            };

            _voiceRecognitionService.OnListeningStopped += (sender, args) =>
            {
                Console.WriteLine("Listening stopped.");
            };


            _voiceRecognitionService.StartListening();

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            _logger.LogInformation("AgentHostedService stopping.");
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _voiceRecognitionService.StopListening();
            _voiceRecognitionService.Dispose();
            _logger.LogInformation("Voice listening stopped.");
            return base.StopAsync(cancellationToken);
        }
    }
}
