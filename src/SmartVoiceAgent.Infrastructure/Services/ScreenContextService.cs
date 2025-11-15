using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Dtos;
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

    public async Task<List<ScreenContext>> CaptureAndAnalyzeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info("Starting multi-monitor balanced-mode capture + analysis");

            var frames = await _screenCaptureService.CaptureAllAsync(cancellationToken);
            if (frames == null || frames.Count == 0)
            {
                _logger.Warn("No frames received from ScreenCaptureService");
                return new List<ScreenContext>();
            }

            // 1. Active window once
            ActiveWindowInfo? activeWindow = null;
            if (_activeWindowService != null)
            {
                try
                {
                    activeWindow = await _activeWindowService.GetActiveWindowInfoAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to get active window info: {ex.Message}");
                }
            }

            // 2. Parallel per-monitor analysis
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
            var tasks = frames.Select(async frame =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await AnalyzeFrameAsync(frame, activeWindow, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);

            var contexts = results.Where(x => x != null).Select(x => x!).ToList();
            _logger.Info($"Balanced-mode analysis finished for {contexts.Count} screens.");

            return contexts;
        }
        catch
        {
            throw;
        }
    }

    private async Task<ScreenContext?> AnalyzeFrameAsync(
        ScreenCaptureFrame frame,
        ActiveWindowInfo? activeWindow,
        CancellationToken cancellationToken)
    {
        try
        {
            if (frame.PngImage == null || frame.PngImage.Length == 0)
                return null;

            int screenIndex = frame.ScreenIndex;
            string deviceName = frame.DeviceName ?? $"Monitor{screenIndex}";
            int width = frame.Width;
            int height = frame.Height;

            // Hash-based skip
            string hash = GenerateScreenshotHash(frame.PngImage);
            if (ShouldSkip(screenIndex, hash))
            {
                _logger.Debug($"Skipping unchanged frame on screen {screenIndex}");

                return new ScreenContext
                {
                    ScreenIndex = screenIndex,
                    DeviceName = deviceName,
                    Width = width,
                    Height = height,
                    IsPrimary = (screenIndex == 0),
                    ScreenshotHash = hash,
                    Timestamp = frame.Timestamp,
                    NormalizedArea = new NormalizedRectangle { X = 0, Y = 0, Width = 1, Height = 1 },
                    ActiveWindow = (screenIndex == 0) ? activeWindow : null,
                    OcrLines = new(),
                    Objects = new()
                };
            }

            using var ms = new MemoryStream(frame.PngImage, false);
            using var bitmap = new Bitmap(ms);

            // Parallel analysis
            var ocrTask = _ocrService.ExtractTextAsync(bitmap);
            var objectsTask = _objectDetectionService != null
                ? _objectDetectionService.DetectObjectsAsync(bitmap)
                : Task.FromResult<IEnumerable<ObjectDetectionItem>>(Enumerable.Empty<ObjectDetectionItem>());

            await Task.WhenAll(ocrTask, objectsTask);

            var ocrLines = (await ocrTask).ToList();
            var objects = (await objectsTask).ToList();

            // Normalize active window for correct monitor
            ActiveWindowInfo? windowForThisScreen = null;
            if (activeWindow != null && activeWindow.ScreenIndex == screenIndex)
            {
                windowForThisScreen = activeWindow with
                {
                    NormalizedBounds = NormalizedRectangle.FromAbsolute(activeWindow.WindowBounds, width, height)
                };
            }

            _lastScreenshotHashes.AddOrUpdate(screenIndex, hash, (_, __) => hash);

            return new ScreenContext
            {
                ScreenIndex = screenIndex,
                DeviceName = deviceName,
                IsPrimary = (screenIndex == 0),
                Width = width,
                Height = height,
                Timestamp = frame.Timestamp,
                ScreenshotHash = hash,
                OcrLines = ocrLines,
                Objects = objects,
                ActiveWindow = windowForThisScreen,
                NormalizedArea = new NormalizedRectangle { X = 0, Y = 0, Width = 1, Height = 1 }
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Frame analysis error on screen {frame.ScreenIndex}: {ex.Message}");
            return null;
        }
    }

    private bool ShouldSkip(int screenIndex, string hash)
    {
        return _lastScreenshotHashes.TryGetValue(screenIndex, out var last)
               && last == hash;
    }

    private static string GenerateScreenshotHash(byte[] png)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(png));
    }
}
