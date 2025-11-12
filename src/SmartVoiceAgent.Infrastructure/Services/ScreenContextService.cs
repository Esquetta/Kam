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

    // per-screen last hash to avoid repeated analysis
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
    /// Capture and analyze all monitors in parallel with optimizations.
    /// </summary>
    public async Task<List<ScreenContext>> CaptureAndAnalyzeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info("Starting multi-monitor capture and optimized analysis");

            // 1) Capture all screens (returns frames)
            var frames = await _screenCaptureService.CaptureAllAsync(cancellationToken);

            if (frames == null || frames.Count == 0)
            {
                _logger.Warn("No frames returned from screen capture service");
                return new List<ScreenContext>();
            }

            // 2) Get active window info once (meaningful primarily for primary monitor)
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

            // 3) Analyze each frame in parallel (but bounded)
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount); // limit concurrency
            var tasks = frames.Select(async frame =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await AnalyzeFrameWithOptimizationsAsync(frame, activeWindow, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks);

            // Filter out nulls and return
            var contexts = results.Where(r => r != null).Select(r => r!).ToList();

            _logger.Info($"Completed analysis for {contexts.Count} screen(s)");
            return contexts;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("CaptureAndAnalyzeAsync cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Unhandled error in CaptureAndAnalyzeAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Analyze a single frame: uses hash check to skip duplicates, runs OCR+Detection in parallel,
    /// creates ScreenContext and normalization data.
    /// </summary>
    private async Task<ScreenContext?> AnalyzeFrameWithOptimizationsAsync(
        ScreenCaptureFrame frame,
        ActiveWindowInfo? activeWindow,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var screenIndex = frame.ScreenIndex;
            var timestamp = frame.Timestamp;
            var deviceName = frame.DeviceName ?? $"Monitor{screenIndex}";
            var width = frame.Width;
            var height = frame.Height;

            // 0) Basic validation
            if (frame.PngImage == null || frame.PngImage.Length == 0)
            {
                _logger.Warn($"Empty image for screen {screenIndex}");
                return null;
            }

            // 1) Hash-based skip check (per-screen)
            var hash = GenerateScreenshotHash(frame.PngImage);
            if (ShouldSkipAnalysis(screenIndex, hash))
            {
                _logger.Debug($"Skipping analysis for screen {screenIndex} - identical frame detected");
                return new ScreenContext
                {
                    ScreenIndex = screenIndex,
                    DeviceName = deviceName,
                    IsPrimary = IsPrimaryScreen(screenIndex),
                    Width = width,
                    Height = height,
                    Timestamp = timestamp,
                    ScreenshotHash = hash,
                    NormalizedArea = new NormalizedRectangle { X = 0, Y = 0, Width = 1, Height = 1 },
                    // preserve previous OCR/objects? currently returning empty lists to indicate skip
                    OcrLines = new List<OcrLine>(),
                    Objects = new List<ObjectDetectionItem>(),
                    ActiveWindow = (screenIndex == 0) ? activeWindow : null
                };
            }

            // 2) Convert to Bitmap *without unnecessary copies* (MemoryStream over the same array)
            using var ms = new MemoryStream(frame.PngImage, writable: false);
            using var bitmap = new Bitmap(ms);

            // 3) Run OCR and detection in parallel (if object detector exists)
            var ocrTask = _ocrService.ExtractTextAsync(bitmap);
            Task<IEnumerable<ObjectDetectionItem>> objectTask;
            if (_objectDetectionService != null)
            {
                objectTask = _objectDetectionService.DetectObjectsAsync(bitmap);
            }
            else
            {
                objectTask = Task.FromResult(Enumerable.Empty<ObjectDetectionItem>());
            }

            await Task.WhenAll(ocrTask, objectTask);

            var ocrLines = (ocrTask.Status == TaskStatus.RanToCompletion) ? (await ocrTask).ToList() : new List<OcrLine>();
            var objects = (objectTask.Status == TaskStatus.RanToCompletion) ? (await objectTask).ToList() : new List<ObjectDetectionItem>();

            // 4) Build normalized area (local monitor coordinates)
            var normalized = new NormalizedRectangle
            {
                X = 0,
                Y = 0,
                Width = 1,
                Height = 1
            };

            // 5) Update per-screen last hash
            _lastScreenshotHashes.AddOrUpdate(screenIndex, hash, (_, __) => hash);

            // 6) Build and return ScreenContext
            var ctx = new ScreenContext
            {
                ScreenIndex = screenIndex,
                DeviceName = deviceName,
                IsPrimary = IsPrimaryScreen(screenIndex),
                Width = width,
                Height = height,
                Timestamp = timestamp,
                ScreenshotHash = hash,
                OcrLines = ocrLines,
                Objects = objects,
                NormalizedArea = normalized,
                ActiveWindow = (screenIndex == 0) ? activeWindow : null
            };

            _logger.Debug($"Analyzed screen {screenIndex}: OCR lines={ctx.OcrLines.Count}, Objects={ctx.Objects.Count}");
            return ctx;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("AnalyzeFrameWithOptimizationsAsync cancelled");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error analyzing frame for screen {frame.ScreenIndex}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Skip analysis if the given hash equals last saved hash for that screen.
    /// Returns true when analysis should be skipped.
    /// </summary>
    private bool ShouldSkipAnalysis(int screenIndex, string newHash)
    {
        if (_lastScreenshotHashes.TryGetValue(screenIndex, out var last) && last == newHash)
            return true;
        return false;
    }

    /// <summary>
    /// Utility to determine if screen index is primary.
    /// If you have MonitorInfo list available you may replace this with exact mapping.
    /// Here we consider 0 = primary by default (consistent with many capture APIs).
    /// </summary>
    private bool IsPrimaryScreen(int screenIndex) => screenIndex == 0;

    /// <summary>
    /// SHA256 hash for duplicate detection.
    /// </summary>
    private static string GenerateScreenshotHash(byte[] pngData)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(pngData);
        return Convert.ToHexString(bytes);
    }
}