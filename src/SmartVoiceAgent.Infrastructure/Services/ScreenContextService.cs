using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace SmartVoiceAgent.Infrastructure.Services;
public class ScreenContextService : IScreenContextService
{
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IOcrService _ocrService;

    public ScreenContextService(
        IScreenCaptureService screenCaptureService,
        IOcrService ocrService)
    {
        _screenCaptureService = screenCaptureService;
        _ocrService = ocrService;
    }

    public async Task<ScreenContext> CaptureAndAnalyzeAsync()
    {
        // 1. Capture screenshot
        using var bitmap = await _screenCaptureService.CaptureScreenAsync();

        // 2. Convert screenshot to PNG bytes
        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            bitmap.Save(ms, ImageFormat.Png);
            pngBytes = ms.ToArray();
        }

        var frame = new ScreenCaptureFrame
        {
            PngImage = pngBytes,
            Timestamp = DateTimeOffset.UtcNow,
            Width = bitmap.Width,
            Height = bitmap.Height
        };

        // 3. Run OCR
        var ocrLines = await _ocrService.ExtractTextAsync(bitmap);

        // 4. Compute hash of screenshot (for duplicate detection)
        string screenshotHash;
        using (var sha = SHA256.Create())
        {
            screenshotHash = Convert.ToHexString(sha.ComputeHash(pngBytes));
        }

        // 5. Build final ScreenContext
        var context = new ScreenContext
        {
            ActiveWindow = GetActiveWindowInfo(), // ileride implement edilecek
            OcrLines = ocrLines.ToList(),
            Objects = new List<ObjectDetectionItem>(), // ileride CV modelinden beslenecek
            ScreenshotHash = screenshotHash,
            Timestamp = frame.Timestamp
        };

        return context;
    }

    private ActiveWindowInfo GetActiveWindowInfo()
    {
        // TODO: Windows API (User32.dll) veya cross-platform çözüm
        // Şimdilik stub dönelim
        return new ActiveWindowInfo
        {
            Title = "Unknown",
            ProcessName = "Unknown",
            ExecutablePath = "Unknown"
        };
    }
}
