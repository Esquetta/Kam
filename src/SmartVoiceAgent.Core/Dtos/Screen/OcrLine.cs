using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos.Screen;
public record OcrLine(
    int LineNumber,
    string Text,
    double Confidence,
    Rectangle BoundingBox
);
