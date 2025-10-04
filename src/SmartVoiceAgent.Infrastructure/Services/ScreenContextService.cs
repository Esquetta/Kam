using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Drawing;
using System.Security.Cryptography;

namespace SmartVoiceAgent.Infrastructure.Services;

public class ScreenContextService : IScreenContextService
{
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IOcrService _ocrService;
    private readonly IObjectDetectionService? _objectDetectionService;
    private readonly IActiveWindowService? _activeWindowService;
    private readonly LoggerServiceBase _logger;

    public ScreenContextService(
        IScreenCaptureService screenCaptureService,
        IOcrService ocrService,
        LoggerServiceBase logger,
        IObjectDetectionService? objectDetectionService = null,
        IActiveWindowService? activeWindowService = null)
    {
        _screenCaptureService = screenCaptureService;
        _ocrService = ocrService;
        _logger = logger;
        _objectDetectionService = objectDetectionService;
        _activeWindowService = activeWindowService;
    }

    public async Task<ScreenContext> CaptureAndAnalyzeAsync()
    {
        try
        {
            _logger.Info("Starting screen capture and analysis");

            // 1. Capture primary screen
            var captureFrame = await _screenCaptureService.CapturePrimaryScreenAsync();

            // 2. Convert PNG bytes to Bitmap
            using var bitmap = ConvertToBitmap(captureFrame.PngImage);

            // 3. Run OCR
            var ocrLines = await _ocrService.ExtractTextAsync(bitmap);

            // 4. Run object detection (if enabled)
            IEnumerable<ObjectDetectionItem> objects = Enumerable.Empty<ObjectDetectionItem>();
            if (_objectDetectionService != null)
            {
                objects = await _objectDetectionService.DetectObjectsAsync(bitmap);
            }

            // 5. Get active window info
            ActiveWindowInfo? activeWindow = null;
            if (_activeWindowService != null)
            {
                activeWindow = await _activeWindowService.GetActiveWindowInfoAsync();
            }

            // 6. Compute hash of screenshot (for duplicate detection)
            string screenshotHash = GenerateScreenshotHash(captureFrame.PngImage);

            // 7. Build final ScreenContext
            var context = new ScreenContext
            {
                ActiveWindow = activeWindow,
                OcrLines = ocrLines.ToList(),
                Objects = objects.ToList(),
                ScreenshotHash = screenshotHash,
                Timestamp = captureFrame.Timestamp
            };

            _logger.Info($"Screen analysis completed. OCR lines: {context.OcrLines.Count}, Objects: {context.Objects.Count}");

            return context;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error during screen capture and analysis: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Converts PNG byte array to Bitmap
    /// </summary>
    private static Bitmap ConvertToBitmap(byte[] pngData)
    {
        using var stream = new MemoryStream(pngData);
        return new Bitmap(stream);
    }

    /// <summary>
    /// Generates SHA256 hash of screenshot
    /// </summary>
    private static string GenerateScreenshotHash(byte[] pngData)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(pngData);
        return Convert.ToHexString(hashBytes);
    }
}