

namespace SmartVoiceAgent.Core.Config;

public class IntentPattern
{
    public string Intent { get; set; }
    public string[] Keywords { get; set; }
    public float Weight { get; set; }

    public IntentPattern(string intent, string[] keywords, float weight = 1.0f)
    {
        Intent = intent;
        Keywords = keywords;
        Weight = weight;
    }
}