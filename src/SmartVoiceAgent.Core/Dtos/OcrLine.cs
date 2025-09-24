using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos;
public record OcrLine
{
    public string Text { get; init; }
    public double Confidence { get; init; }
    public Rectangle BoundingBox { get; init; }
}