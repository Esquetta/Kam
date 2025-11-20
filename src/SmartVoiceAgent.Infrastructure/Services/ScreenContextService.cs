using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Dtos.Screen;
using SmartVoiceAgent.Core.Interfaces;
using System.Collections.Concurrent;
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

    private readonly ConcurrentDictionary<int, string> _lastScreenshotHashes = new();

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
    /// Captures and analyzes all monitors using balanced CPU/IO mode
    /// </summary>
    public async Task<List<ScreenContext>> CaptureAndAnalyzeAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Balanced-mode: Screenshot capture & analysis started.");

        var frames = await _screenCaptureService.CaptureAllAsync(cancellationToken);
        if (frames == null || frames.Count == 0)
        {
            _logger.Warn("ScreenCaptureService returned zero frames.");
            return new();
        }

        // Active window is fetched once
        ActiveWindowInfo? activeWindowInfo = null;
        if (_activeWindowService != null)
        {
            try
            {
                activeWindowInfo = await _activeWindowService.GetActiveWindowInfoAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Active window retrieval failed: {ex.Message}");
            }
        }

        var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

        var tasks = frames.Select(async frame =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await AnalyzeFrameAsync(frame, activeWindowInfo, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        var contexts = results.Where(r => r != null).ToList()!;

        _logger.Info($"Balanced-mode: Completed analysis for {contexts.Count} monitors.");

        return contexts;
    }

    /// <summary>
    /// Performs OCR + Object Detection + Active Window normalization for a single monitor
    /// </summary>
    private async Task<ScreenContext?> AnalyzeFrameAsync(
        ScreenCaptureFrame frame,
        ActiveWindowInfo? activeWindowInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            if (frame.PngImage is null || frame.PngImage.Length == 0)
                return null;

            int index = frame.ScreenIndex;
            string hash = GenerateScreenshotHash(frame.PngImage);

            // Skip unchanged
            if (ShouldSkip(index, hash))
            {
                _logger.Debug($"Balanced-mode: Screen {index} unchanged → skipping heavy analysis.");

                return CreateSkippedContext(frame, hash, activeWindowInfo);
            }

            using var ms = new MemoryStream(frame.PngImage, false);
            using var bmp = new Bitmap(ms);

            // Run OCR and Object detection in parallel
            var ocrTask = _ocrService.ExtractTextAsync(bmp);
            var objTask = _objectDetectionService != null
                ? _objectDetectionService.DetectObjectsAsync(bmp)
                : Task.FromResult<IEnumerable<ObjectDetectionItem>>(Array.Empty<ObjectDetectionItem>());

            await Task.WhenAll(ocrTask, objTask);

            var ocrLines = (await ocrTask).ToList();
            var objects = (await objTask).ToList();

            _lastScreenshotHashes[index] = hash;

            return CreateFullContext(frame, hash, activeWindowInfo, ocrLines, objects);
        }
        catch (Exception ex)
        {
            _logger.Error($"Screen {frame.ScreenIndex} analysis error: {ex.Message}");
            return null;
        }
    }

    private ScreenContext CreateSkippedContext(
        ScreenCaptureFrame frame,
        string hash,
        ActiveWindowInfo? activeWindowInfo)
    {
        return new ScreenContext
        {
            ScreenIndex = frame.ScreenIndex,
            DeviceName = frame.DeviceName ?? $"Monitor{frame.ScreenIndex}",
            Width = frame.Width,
            Height = frame.Height,
            ScreenshotHash = hash,
            Timestamp = frame.Timestamp,
            IsPrimary = (frame.ScreenIndex == 0),
            NormalizedArea = new NormalizedRectangle { X = 0, Y = 0, Width = 1, Height = 1 },
            ActiveWindow = (frame.ScreenIndex == 0) ? NormalizeWindow(activeWindowInfo, frame) : null,
            OcrLines = new(),
            Objects = new()
        };
    }

    private ScreenContext CreateFullContext(
        ScreenCaptureFrame frame,
        string hash,
        ActiveWindowInfo? activeWindow,
        List<OcrLine> ocr,
        List<ObjectDetectionItem> objects)
    {
        return new ScreenContext
        {
            ScreenIndex = frame.ScreenIndex,
            DeviceName = frame.DeviceName ?? $"Monitor{frame.ScreenIndex}",
            Width = frame.Width,
            Height = frame.Height,
            ScreenshotHash = hash,
            Timestamp = frame.Timestamp,
            IsPrimary = (frame.ScreenIndex == 0),
            OcrLines = ocr,
            Objects = objects,
            ActiveWindow = NormalizeWindow(activeWindow, frame),
            NormalizedArea = new NormalizedRectangle { X = 0, Y = 0, Width = 1, Height = 1 }
        };
    }

    private ActiveWindowInfo? NormalizeWindow(ActiveWindowInfo? win, ScreenCaptureFrame frame)
    {
        if (win == null || win.ScreenIndex != frame.ScreenIndex)
            return null;

        return win with
        {
            NormalizedBounds = NormalizedRectangle.FromAbsolute(
                win.WindowBounds,
                frame.Width,
                frame.Height)
        };
    }

    private bool ShouldSkip(int screenIndex, string hash)
        => _lastScreenshotHashes.TryGetValue(screenIndex, out var last) && last == hash;

    private static string GenerateScreenshotHash(byte[] data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data));
    }
}
