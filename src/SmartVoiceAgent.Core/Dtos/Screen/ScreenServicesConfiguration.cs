namespace SmartVoiceAgent.Core.Dtos;
public class ScreenServicesConfiguration
{
    public bool EnableObjectDetection { get; set; } = false;
    public string OcrLanguage { get; set; } = "eng";
    public string TessDataPath { get; set; } = "./tessdata";
    public int ScreenCaptureQuality { get; set; } = 95;
    public bool ParallelProcessing { get; set; } = true;
}
