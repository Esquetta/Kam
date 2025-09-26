using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos;
public record OcrLine(
    int LineNumber,
    string Text,
    double Confidence,
    Rectangle BoundingBox
);
