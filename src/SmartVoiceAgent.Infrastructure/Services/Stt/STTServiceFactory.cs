using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.Stt
{
    public class STTServiceFactory : ISTTServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<STTServiceFactory> _logger;

        public STTServiceFactory(IServiceProvider serviceProvider, ILogger<STTServiceFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public ISpeechToTextService CreateService(STTProvider provider)
        {
            return provider switch
            {
                STTProvider.HuggingFace => _serviceProvider.GetRequiredService<HuggingFaceSTTService>(),
                //STTProvider.OpenAI => _serviceProvider.GetRequiredService<OpenAIWhisperService>(),
                //STTProvider.Azure => _serviceProvider.GetRequiredService<AzureSTTService>(),
                //STTProvider.Google => _serviceProvider.GetRequiredService<GoogleSTTService>(),
                _ => throw new NotSupportedException($"STT provider {provider} is not supported")
            };
        }
    }
}
