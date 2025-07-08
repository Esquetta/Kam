using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Infrastructure.Services
{
    /// <summary>
    ///  A hosted background service that continuously listens for voice commands,
    ///  detects intents and dispatches them to the appropriate command handlers.
    /// </summary>
    public class AgentHostedService: BackgroundService
    {
        private readonly IVoiceRecognitionService _voiceRecognition;
        private readonly IIntentDetectionService _intentDetector;
        private readonly IMediator mediator;
        private readonly AudioProcessingService audioProcessingService;
        private readonly ILogger<AgentHostedService> _logger;

        public AgentHostedService(IVoiceRecognitionService voiceRecognition, IIntentDetectionService intentDetector, ICommandBus commandBus, IQueryBus queryBus, ILogger<AgentHostedService> logger, IMediator mediator, AudioProcessingService audioProcessingService)
        {
            _voiceRecognition = voiceRecognition;
            _intentDetector = intentDetector;
            _logger = logger;
            this.mediator = mediator;
            this.audioProcessingService = audioProcessingService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AgentHostedService started.");
            //await mediator.Send(new OpenApplicationCommand("Pycharm"));


            Console.WriteLine("Ses kaydı başlatılıyor...");

            var voiceService = new WindowsVoiceRecognitionService();

            voiceService.OnVoiceCaptured += (sender, data) =>
            {
                Console.WriteLine($"Ses kaydı alındı. Byte uzunluğu: {data.Length}");
            };

            voiceService.OnRecordingStarted += (s, e) =>
            {
                Console.WriteLine("Kayıt başladı.");
            };

            voiceService.OnRecordingStopped += (s, e) =>
            {
                Console.WriteLine("Kayıt durdu.");
            };

            voiceService.OnError += (s, ex) =>
            {
                Console.WriteLine($"Hata oluştu: {ex.Message}");
            };

            // 5 saniyelik kayıt al
            var audioData = await voiceService.RecordForDurationAsync(TimeSpan.FromSeconds(10));

            Console.WriteLine($"Toplam kaydedilen veri: {audioData.Length} byte");

            

            // Temizlik
            voiceService.Dispose();

            var result=await audioProcessingService.ProcessAudioFromRecording(audioData, stoppingToken);

            Console.WriteLine($"Converted stt Result:{result.Text}");




            // Uygulama kapanmadan beklet
            Console.WriteLine("Çıkmak için bir tuşa bas...");
            Console.ReadKey();

            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    try
            //    {
            //        // 1. Dinle
            //        var voiceText = await _voiceRecognition.ListenAsync(stoppingToken);
            //        _logger.LogInformation("Heard: {0}", voiceText);

            //        // 2. Intent tespit et
            //        var intent = await _intentDetector.DetectIntentAsync(voiceText);
            //        _logger.LogInformation("Detected intent: {0}", intent);

            //        // 3. Komut yarat ve bus üzerinden gönder
            //        switch (intent)
            //        {
            //            case CommandType.OpenApplication:
            //                await mediator.Send(new OpenApplicationCommand(voiceText));
            //                break;

            //            case CommandType.PlayMusic:
            //                await mediator.Send(new PlayMusicCommand(voiceText));
            //                break;

            //            case CommandType.SearchWeb:
            //                await mediator.Send(new SearchWebCommand(voiceText));
            //                break;

            //            case CommandType.SendMessage:
            //                // Örnek: "Smith'e mesaj gönder Merhaba" → pars et
            //                var parts = voiceText.Split(" mesaj ");
            //                if (parts.Length == 2)
            //                    await mediator.Send(new SendMessageCommand(parts[0], parts[1]));
            //                break;

            //            case CommandType.ControlDevice:
            //                // Örnek: "ışıkları kapat"
            //                var tokens = voiceText.Split(' ', 2);
            //                if (tokens.Length == 2)
            //                    await mediator.Send(new ControlDeviceCommand(tokens[0], tokens[1]));
            //                break;

            //            default:
            //                _logger.LogWarning("No handler for intent {0}", intent);
            //                break;
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger.LogError(ex, "Error in AgentHostedService loop.");
            //    }

            //    // Küçük bir bekleme ile CPU tüketimini düşür
            //    await Task.Delay(500, stoppingToken);
            //}

            _logger.LogInformation("AgentHostedService stopping.");
        }
    }
}
