using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos;
public record ObjectDetectionItem
{
    public string Label { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public Rectangle BoundingBox { get; init; }
}
