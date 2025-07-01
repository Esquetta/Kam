
namespace SmartVoiceAgent.Core.Config;
public class IntentConfig
{
    public float MinimumConfidence { get; set; } = 0.3f;
    public bool EnableEntityExtraction { get; set; } = true;
}