using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.Stt
{
    public class STTServiceFactory : ISTTServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public STTServiceFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ISpeechToTextService CreateService(STTProvider provider)
        {
            return provider switch
            {
                STTProvider.HuggingFace => _serviceProvider.GetRequiredService<HuggingFaceSTTService>(),
                STTProvider.OpenAI => _serviceProvider.GetRequiredService<WhisperSTTService>(),
                STTProvider.Ollama => _serviceProvider.GetRequiredService<OllamaSTTService>(),
                //STTProvider.Azure => _serviceProvider.GetRequiredService<AzureSTTService>(),
                //STTProvider.Google => _serviceProvider.GetRequiredService<GoogleSTTService>(),
                _ => throw new NotSupportedException($"STT provider {provider} is not supported")
            };
        }
    }
}
