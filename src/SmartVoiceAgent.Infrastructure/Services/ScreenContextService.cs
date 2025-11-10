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

    /// <summary>
    /// Captures and analyzes ALL monitors.
    /// Returns a list of ScreenContext (one per monitor)
    /// </summary>
    public async Task<List<ScreenContext>> CaptureAndAnalyzeAsync()
    {
        try
        {
            _logger.Info("Starting multi-monitor screen capture and analysis");

            // 1. Capture all monitors
            var captures = await _screenCaptureService.CaptureAllAsync();

            _logger.Info($"Captured {captures.Count} screens. Starting analysis...");

            var results = new List<ScreenContext>();

            // 2. Get active window only once (makes sense only for primary monitor)
            ActiveWindowInfo? activeWindowInfo = null;
            if (_activeWindowService != null)
            {
                activeWindowInfo = await _activeWindowService.GetActiveWindowInfoAsync();
            }

            // 3. Analyze each monitor in parallel (better performance)
            var tasks = captures.Select(async frame =>
            {
                try
                {
                    return await AnalyzeSingleMonitorAsync(frame, activeWindowInfo);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error analyzing monitor {frame.ScreenIndex}: {ex.Message}");
                    return null;
                }
            });

            var contexts = await Task.WhenAll(tasks);

            results.AddRange(contexts.Where(c => c != null)!);

            _logger.Info($"Multi-monitor analysis completed. Total contexts: {results.Count}");

            return results;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error during multi-monitor capture and analysis: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Analyze a single monitor frame (OCR, Object Detection, Hash, etc)
    /// </summary>
    private async Task<ScreenContext> AnalyzeSingleMonitorAsync(
        ScreenCaptureFrame frame,
        ActiveWindowInfo? activeWindowInfo)
    {
        using var bitmap = ConvertToBitmap(frame.PngImage);

        // OCR
        var ocrLines = await _ocrService.ExtractTextAsync(bitmap);

        // Object detection
        IEnumerable<ObjectDetectionItem> detectedObjects = Enumerable.Empty<ObjectDetectionItem>();
        if (_objectDetectionService != null)
        {
            detectedObjects = await _objectDetectionService.DetectObjectsAsync(bitmap);
        }

        // Hash
        string screenshotHash = GenerateScreenshotHash(frame.PngImage);

        // Build context
        return new ScreenContext
        {
            ScreenIndex = frame.ScreenIndex,
            DeviceName = frame.DeviceName,
            Timestamp = frame.Timestamp,
            ScreenshotHash = screenshotHash,
            OcrLines = ocrLines.ToList(),
            Objects = detectedObjects.ToList(),

            // Only primary monitor gets active window info
            ActiveWindow = frame.ScreenIndex == 0 ? activeWindowInfo : null
        };
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
