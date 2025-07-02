using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Core.Entities;

public class IntentPattern
{
    public CommandType Intent { get; set; }
    public string[] Keywords { get; set; }
    public float Weight { get; set; }

    public IntentPattern(CommandType intent, string[] keywords, float weight = 1.0f)
    {
        Intent = intent;
        Keywords = keywords;
        Weight = weight;
    }
}
