﻿namespace SmartVoiceAgent.Core.Dtos;
public record ScreenCaptureFrame
{
    public byte[] PngImage { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int ScreenIndex { get; init; } 
    public string DeviceName { get; init; }
}