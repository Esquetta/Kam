using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace SmartVoiceAgent.Infrastructure.Services;

public enum ProcessingProfile
{
    HighPerformance,
    Balanced,
    HighQuality
}

public class ScreenContextService : IScreenContextService
{
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IOcrService _ocrService;
    private readonly IObjectDetectionService? _objectDetectionService;
    private readonly IActiveWindowService? _activeWindowService;
    private readonly LoggerServiceBase _logger;
    private readonly ProcessingProfile _profile;

    // Per-screen caches
    private readonly ConcurrentDictionary<int, string> _lastScreenshotHashes = new();
    private readonly ConcurrentDictionary<int, ScreenContext> _lastScreenContexts = new();

    // Limit concurrency
    private readonly int _maxConcurrency;

    public ScreenContextService(
        IScreenCaptureService screenCaptureService,
        IOcrService ocrService,
        LoggerServiceBase logger,
        IObjectDetectionService? objectDetectionService = null,
        IActiveWindowService? activeWindowService = null,
        ProcessingProfile profile = ProcessingProfile.Balanced,
        int? maxConcurrency = null)
    {
        _screenCaptureService = screenCaptureService;
        _ocrService = ocrService;
        _logger = logger;
        _objectDetectionService = objectDetectionService;
        _activeWindowService = activeWindowService;
        _profile = profile;
        _maxConcurrency = maxConcurrency ?? Math.Max(1, Environment.ProcessorCount / 2);
    }

    /// <summary>
    /// Capture and analyze all monitors with advanced optimizations and processing profiles.
    /// Returns a list of ScreenContext (one per captured monitor).
    /// </summary>
    public async Task<List<ScreenContext>> CaptureAndAnalyzeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info($"Starting CaptureAndAnalyzeAsync (Profile={_profile}, MaxConcurrency={_maxConcurrency})");

            // 1) Capture all screens
            var frames = await _screenCaptureService.CaptureAllAsync(cancellationToken);

            if (frames == null || frames.Count == 0)
            {
                _logger.Warn("No frames returned from screen capture service");
                return new List<ScreenContext>();
            }

            // 2) Get monitors info for normalization
            var monitors = frames.Select(f => new
            {
                Frame = f,
                ScreenIndex = f.ScreenIndex,
                Width = f.Width,
                Height = f.Height
            }).ToList();

            // Compute global virtual screen bounds
            var virtualLeft = 0;
            var virtualTop = 0;
            var virtualRight = monitors.Max(m => m.Width);
            var virtualBottom = monitors.Max(m => m.Height);
            var virtualWidth = Math.Max(virtualRight - virtualLeft, 1);
            var virtualHeight = Math.Max(virtualBottom - virtualTop, 1);

            // 3) Get active window once
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

            // 4) Analyze frames with bounded parallelism
            var semaphore = new SemaphoreSlim(_maxConcurrency);
            var tasks = monitors.Select(async m =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await AnalyzeFrameWithOptimizationsAsync(
                        m.Frame,
                        activeWindow,
                        virtualWidth,
                        virtualHeight,
                        cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            var contexts = results.Where(r => r != null).Select(r => r!).ToList();

            _logger.Info($"CaptureAndAnalyzeAsync completed: analyzed {contexts.Count} / {frames.Count} frames.");
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

    private async Task<ScreenContext?> AnalyzeFrameWithOptimizationsAsync(
        ScreenCaptureFrame frame,
        ActiveWindowInfo? activeWindow,
        int globalCanvasWidth,
        int globalCanvasHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var screenIndex = frame.ScreenIndex;
            var timestamp = frame.Timestamp;
            var deviceName = string.IsNullOrEmpty(frame.DeviceName) ? $"Monitor{screenIndex}" : frame.DeviceName;
            var width = frame.Width;
            var height = frame.Height;

            if (frame.PngImage == null || frame.PngImage.Length == 0)
            {
                _logger.Warn($"Empty image for screen {screenIndex}");
                return null;
            }

            // Compute hash for duplicate detection
            var hash = GenerateScreenshotHash(frame.PngImage);

            // If identical frame and cached context available -> return cached
            if (_lastScreenshotHashes.TryGetValue(screenIndex, out var lastHash) && lastHash == hash)
            {
                if (_lastScreenContexts.TryGetValue(screenIndex, out var cachedContext))
                {
                    _logger.Debug($"Screen {screenIndex}: hash identical, returning cached ScreenContext.");
                    var updated = cachedContext with { Timestamp = timestamp, ScreenshotHash = hash };
                    _lastScreenContexts[screenIndex] = updated;
                    return updated;
                }
            }

            // Convert PNG to Bitmap
            using var ms = new MemoryStream(frame.PngImage, writable: false);
            using var originalBitmap = new Bitmap(ms);

            // Preprocess for OCR based on profile
            using var ocrBitmap = PreprocessForOcr(originalBitmap);

            // OCR first (fast path)
            List<OcrLine> ocrLines;
            try
            {
                var ocrResult = await _ocrService.ExtractTextAsync(ocrBitmap);
                ocrLines = ocrResult?.ToList() ?? new List<OcrLine>();
            }
            catch (Exception ex)
            {
                _logger.Error($"OCR failed for screen {screenIndex}: {ex.Message}");
                ocrLines = new List<OcrLine>();
            }

            // Start object detection in background
            Task<IEnumerable<ObjectDetectionItem>> detectionTask;
            if (_objectDetectionService != null)
            {
                var detectionBitmap = (_profile == ProcessingProfile.HighQuality)
                    ? (Bitmap)originalBitmap.Clone()
                    : ResizeBitmap(originalBitmap, Math.Max(640, width / 2), Math.Max(360, height / 2));
                detectionTask = _objectDetectionService.DetectObjectsAsync(detectionBitmap);
            }
            else
            {
                detectionTask = Task.FromResult(Enumerable.Empty<ObjectDetectionItem>());
            }

            // Wait for detection with timeout based on profile
            List<ObjectDetectionItem> objects = new();
            try
            {
                TimeSpan detectionWait = _profile switch
                {
                    ProcessingProfile.HighPerformance => TimeSpan.FromMilliseconds(150),
                    ProcessingProfile.Balanced => TimeSpan.FromMilliseconds(350),
                    ProcessingProfile.HighQuality => TimeSpan.FromMilliseconds(700),
                    _ => TimeSpan.FromMilliseconds(350)
                };

                var finished = await Task.WhenAny(detectionTask, Task.Delay(detectionWait, cancellationToken));
                if (finished == detectionTask)
                {
                    objects = (await detectionTask).ToList();
                }
                else
                {
                    // Detection still running - schedule to complete in background
                    _ = detectionTask.ContinueWith(t =>
                    {
                        try
                        {
                            if (t.Status == TaskStatus.RanToCompletion)
                            {
                                var detected = t.Result.ToList();
                                UpsertCacheWithDetection(screenIndex, hash, timestamp, detected);
                                _logger.Debug($"Async detection completed for screen {screenIndex}: {detected.Count} items");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Background detection failed for screen {screenIndex}: {ex.Message}");
                        }
                    }, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Object detection coordination failed for screen {screenIndex}: {ex.Message}");
            }

            // Build normalized rectangle
            var normalized = new NormalizedRectangle
            {
                X = 0,
                Y = 0,
                Width = 1,
                Height = 1
            };

            // Active window mapping: use only when matches screen index
            ActiveWindowInfo? matchedActiveWindow = null;
            if (activeWindow != null && activeWindow.ScreenIndex == screenIndex)
            {
                matchedActiveWindow = activeWindow;
            }

            // Assemble ScreenContext
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
                ActiveWindow = matchedActiveWindow
            };

            // Update caches
            _lastScreenshotHashes.AddOrUpdate(screenIndex, hash, (_, __) => hash);
            _lastScreenContexts.AddOrUpdate(screenIndex, ctx, (_, __) => ctx);

            _logger.Debug($"Analyzed screen {screenIndex}: OCR lines={ctx.OcrLines?.Count ?? 0}, Objects={ctx.Objects?.Count ?? 0}");
            return ctx;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn($"AnalyzeFrameWithOptimizationsAsync cancelled for screen {frame.ScreenIndex}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error analyzing frame {frame.ScreenIndex}: {ex.Message}");
            return null;
        }
    }

    private Bitmap PreprocessForOcr(Bitmap original)
    {
        switch (_profile)
        {
            case ProcessingProfile.HighPerformance:
                {
                    var scaled = ResizeBitmap(original, Math.Max(800, original.Width / 2), Math.Max(450, original.Height / 2));
                    var gray = ConvertToGrayscale(scaled);
                    scaled.Dispose();
                    return gray;
                }
            case ProcessingProfile.Balanced:
                {
                    var scaled = ResizeBitmap(original, Math.Max(1200, original.Width * 3 / 4), Math.Max(700, original.Height * 3 / 4));
                    var gray = ConvertToGrayscale(scaled);
                    scaled.Dispose();
                    return gray;
                }
            case ProcessingProfile.HighQuality:
                {
                    return ConvertToGrayscale(original);
                }
            default:
                return ConvertToGrayscale(original);
        }
    }

    private static Bitmap ConvertToGrayscale(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(clone))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            var cm = new ColorMatrix(new float[][]
            {
                new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
            });

            var ia = new ImageAttributes();
            ia.SetColorMatrix(cm);
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                       0, 0, source.Width, source.Height, GraphicsUnit.Pixel, ia);
        }

        using var converted = new Bitmap(clone.Width, clone.Height, PixelFormat.Format8bppIndexed);
        using (var g = Graphics.FromImage(converted))
        {
            g.DrawImage(clone, new Rectangle(0, 0, clone.Width, clone.Height));
        }
        clone.Dispose();
        return new Bitmap(converted);
    }

    private static Bitmap ResizeBitmap(Bitmap source, int targetWidth, int targetHeight)
    {
        var dst = new Bitmap(targetWidth, targetHeight, source.PixelFormat);
        using var g = Graphics.FromImage(dst);
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighSpeed;
        g.DrawImage(source, 0, 0, targetWidth, targetHeight);
        return dst;
    }

    private void UpsertCacheWithDetection(int screenIndex, string hash, DateTimeOffset timestamp, List<ObjectDetectionItem> detected)
    {
        if (_lastScreenContexts.TryGetValue(screenIndex, out var existing))
        {
            var updated = existing with
            {
                Objects = detected,
                Timestamp = timestamp,
                ScreenshotHash = hash
            };
            _lastScreenContexts[screenIndex] = updated;
        }
        else
        {
            var ctx = new ScreenContext
            {
                ScreenIndex = screenIndex,
                DeviceName = $"Monitor{screenIndex}",
                IsPrimary = IsPrimaryScreen(screenIndex),
                Width = existing?.Width ?? 0,
                Height = existing?.Height ?? 0,
                Timestamp = timestamp,
                ScreenshotHash = hash,
                OcrLines = existing?.OcrLines ?? new List<OcrLine>(),
                Objects = detected,
                NormalizedArea = existing?.NormalizedArea ?? new NormalizedRectangle { X = 0, Y = 0, Width = 1, Height = 1 },
                ActiveWindow = existing?.ActiveWindow
            };
            _lastScreenContexts[screenIndex] = ctx;
        }
    }

    private static string GenerateScreenshotHash(byte[] pngData)
    {
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(pngData);
        return Convert.ToHexString(h);
    }

    private bool IsPrimaryScreen(int screenIndex)
    {
        return screenIndex == 0;
    }
}