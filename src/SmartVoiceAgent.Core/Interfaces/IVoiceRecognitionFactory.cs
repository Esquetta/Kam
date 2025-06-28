namespace SmartVoiceAgent.Core.Interfaces
{
    public interface IVoiceRecognitionFactory
    {
        IVoiceRecognitionService Create();
    }
}
