using AutoGen.Core;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Agent;
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
        private readonly IConfiguration configuration;

        private readonly IWebResearchService webResearchService;

        private SmartGroupChat AgentGroup;
        private readonly Functions functions;
        private readonly IServiceProvider serviceProvider;


        public AgentHostedService(IIntentDetectionService intentDetector, ILogger<AgentHostedService> logger, IMediator mediator, AudioProcessingService audioProcessingService, ILanguageDetectionService languageDetectionService, IApplicationScannerServiceFactory applicationScannerServiceFactory, IVoiceRecognitionFactory voiceRecognitionFactory, IConfiguration configuration, Functions functions, IWebResearchService webResearchService, IServiceProvider serviceProvider)
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
            this.configuration = configuration;
            this.functions = functions;
            this.webResearchService = webResearchService;

        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            this.AgentGroup = await GroupChatAgentFactory.CreateGroupChatAsync(apiKey: configuration.GetSection("AiAgent:Apikey").Get<string>(), model: configuration.GetSection("AiAgent:Model").Get<string>(), endpoint: configuration.GetSection("AiAgent:EndPoint").Get<string>(), functions: functions, configuration, _intentDetector);

            // Test 1: Intent Detection
            var intent = await _intentDetector.DetectIntentAsync("Yarına Ders adında bir görev oluşturmanı istiyorum. Saat 9'a.", "tr", stoppingToken);

            // Test 2: Agent Group Communication
            var testMessage = "Spotify'ı aç.";

            var result = AgentGroup.SendWithAnalyticsAsync(testMessage, "User", 10, stoppingToken);

            //    // Voice captured event
            //    _voiceRecognitionService.OnVoiceCaptured += async (sender, audioData) =>
            //    {
            //        try
            //        {
            //            Console.WriteLine($"Voice data captured. Length: {audioData.Length}");

            //            var sttResult = await audioProcessingService.ProcessAudioFromRecording(audioData, stoppingToken);

            //            if (sttResult.IsSuccess)
            //                Console.WriteLine($"Recognized Text: {sttResult.Text}");
            //            else
            //                Console.WriteLine($"Speech-to-Text Error: {sttResult.ErrorMessage}");
            //            var language = await languageDetectionService.DetectLanguageAsync(sttResult.Text, stoppingToken);
            //            var intent = await _intentDetector.DetectIntentAsync(sttResult.Text, language.Language, stoppingToken);

            //            Console.WriteLine($"İntent:{intent.Intent}, Language:{language.Language}.");

            //            await this.agent.SendAsync(sttResult.Text);


            //        }
            //        catch (Exception ex)
            //        {
            //            Console.WriteLine($"Error while processing captured voice data: {ex.Message}");
            //        }
            //    };

            //_voiceRecognitionService.OnError += (sender, ex) =>
            //{
            //    Console.WriteLine($"Voice recognition error: {ex.Message}");
            //};

            //_voiceRecognitionService.OnListeningStarted += (sender, args) =>
            //{
            //    Console.WriteLine("Listening started.");
            //};

            //_voiceRecognitionService.OnListeningStopped += (sender, args) =>
            //{
            //    Console.WriteLine("Listening stopped.");
            //};


            //_voiceRecognitionService.StartListening();

            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    await Task.Delay(1000, stoppingToken);
            //}

            //_logger.LogInformation("AgentHostedService stopping.");
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
