using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Core.Interfaces
{
    public interface ISTTServiceFactory
    {
        ISpeechToTextService CreateService(STTProvider provider);
    }
}
