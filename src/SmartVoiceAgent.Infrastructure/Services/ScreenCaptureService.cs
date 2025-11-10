using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SmartVoiceAgent.Infrastructure.Services;

public class ScreenCaptureService : IScreenCaptureService
{
    private readonly LoggerServiceBase _logger;
#if DEBUG
    public bool EnableDebugPreview { get; set; } = true;
#else
    public bool EnableDebugPreview { get; set; } = false;  // Prod'da kapalı
#endif


    public ScreenCaptureService(LoggerServiceBase logger)
    {
        _logger = logger;
    }

    #region Win32 API Declarations

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hDestDC, int x, int y, int width, int height, IntPtr hSrcDC, int xSrc, int ySrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int SRCCOPY = 0x00CC0020;
    private const uint MONITORINFOF_PRIMARY = 0x00000001;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    #endregion

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScreenCaptureFrame>> CaptureAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info("Starting capture of all screens");

            var monitors = await GetMonitorsAsync();
            var frames = new List<ScreenCaptureFrame>();

            _logger.Info($"Found {monitors.Count} monitor(s)");

            // Her monitör için paralel capture
            var tasks = monitors.Select(async (monitor, index) =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var frame = await CaptureMonitorAsync(monitor, index, cancellationToken);
                    return frame;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error capturing monitor {index}: {ex.Message}");
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);
            frames.AddRange(results.Where(f => f != null));

            _logger.Info($"Successfully captured {frames.Count} screen(s)");
            return frames.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.Error($"Error during screen capture: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ScreenCaptureFrame> CaptureScreenAsync(int screenIndex, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info($"Starting capture of screen {screenIndex}");

            var monitors = await GetMonitorsAsync();

            if (screenIndex < 0 || screenIndex >= monitors.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(screenIndex),
                    $"Screen index {screenIndex} is out of range. Available screens: 0-{monitors.Count - 1}");
            }

            var monitor = monitors[screenIndex];
            var frame = await CaptureMonitorAsync(monitor, screenIndex, cancellationToken);

            _logger.Info($"Successfully captured screen {screenIndex}");
            return frame;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error capturing screen {screenIndex}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Primary screen'i yakalar
    /// </summary>
    public async Task<ScreenCaptureFrame> CapturePrimaryScreenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info("Starting capture of primary screen");

            var monitors = await GetMonitorsAsync();
            var primaryMonitor = monitors.FirstOrDefault(m => m.IsPrimary);

            if (primaryMonitor == null)
            {
                throw new InvalidOperationException("No primary screen found");
            }

            var screenIndex = monitors.IndexOf(primaryMonitor);
            var frame = await CaptureMonitorAsync(primaryMonitor, screenIndex, cancellationToken);

            _logger.Info("Successfully captured primary screen");
            return frame;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error capturing primary screen: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Belirli bir monitörü yakalar
    /// </summary>
    private async Task<ScreenCaptureFrame> CaptureMonitorAsync(MonitorInfo monitor, int screenIndex, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bounds = monitor.Bounds;
            _logger.Debug($"Capturing screen {screenIndex}: {bounds.Width}x{bounds.Height} at ({bounds.X}, {bounds.Y})");

            IntPtr desktopDC = IntPtr.Zero;
            IntPtr memoryDC = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;

            try
            {
                // Desktop DC'yi al
                desktopDC = GetDC(IntPtr.Zero);
                if (desktopDC == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to get desktop DC");

                // Memory DC oluştur
                memoryDC = CreateCompatibleDC(desktopDC);
                if (memoryDC == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create compatible DC");

                // Bitmap oluştur
                bitmap = CreateCompatibleBitmap(desktopDC, bounds.Width, bounds.Height);
                if (bitmap == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create compatible bitmap");

                // Bitmap'i seç
                var oldBitmap = SelectObject(memoryDC, bitmap);

                // Ekranı kopyala
                bool success = BitBlt(memoryDC, 0, 0, bounds.Width, bounds.Height,
                                    desktopDC, bounds.X, bounds.Y, SRCCOPY);

                if (!success)
                    throw new InvalidOperationException("Failed to capture screen");

                // GDI+ Bitmap'e dönüştür
                using var gdiplusBitmap = Image.FromHbitmap(bitmap);
                using var clonedBitmap = new Bitmap(gdiplusBitmap);

                // PNG formatında byte array'e çevir
                using var memoryStream = new MemoryStream();
                clonedBitmap.Save(memoryStream, ImageFormat.Png);
                var pngData = memoryStream.ToArray();

                byte[]? previewPng = null;

#if DEBUG
                if (EnableDebugPreview)
                {
                    previewPng = CreateScaledPreview(clonedBitmap);     
                    SaveAndOpenDebugPreview(previewPng, screenIndex);   
                }
#endif

                return new ScreenCaptureFrame
                {
                    PngImage = pngData,
                    Timestamp = DateTimeOffset.UtcNow,
                    PreviewPng = previewPng,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    ScreenIndex = screenIndex,
                    DeviceName = monitor.DeviceName
                };
            }
            finally
            {
                // Cleanup
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memoryDC != IntPtr.Zero) DeleteDC(memoryDC);
                if (desktopDC != IntPtr.Zero) ReleaseDC(IntPtr.Zero, desktopDC);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Monitör bilgilerini getirir
    /// </summary>
    private Task<List<MonitorInfo>> GetMonitorsAsync()
    {
        return Task.Run(() =>
        {
            var monitors = new List<MonitorInfo>();

            MonitorEnumDelegate callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);

                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    var monitor = new MonitorInfo
                    {
                        Handle = hMonitor,
                        Bounds = new Rectangle(
                            monitorInfo.rcMonitor.Left,
                            monitorInfo.rcMonitor.Top,
                            monitorInfo.rcMonitor.Width,
                            monitorInfo.rcMonitor.Height
                        ),
                        WorkingArea = new Rectangle(
                            monitorInfo.rcWork.Left,
                            monitorInfo.rcWork.Top,
                            monitorInfo.rcWork.Width,
                            monitorInfo.rcWork.Height
                        ),
                        IsPrimary = (monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        DeviceName = $"Monitor{monitors.Count + 1}"
                    };

                    monitors.Add(monitor);
                }

                return true;
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

            return monitors;
        });
    }

    /// <summary>
    /// Ekran bilgilerini getirir
    /// </summary>
    public async Task<IReadOnlyList<ScreenInfo>> GetScreenInfoAsync()
    {
        try
        {
            var monitors = await GetMonitorsAsync();
            var screenInfos = monitors.Select((monitor, index) => new ScreenInfo
            {
                Index = index,
                DeviceName = monitor.DeviceName,
                Bounds = monitor.Bounds,
                WorkingArea = monitor.WorkingArea,
                IsPrimary = monitor.IsPrimary,
                BitsPerPixel = 32 // Default olarak 32 bit
            }).ToList();

            return screenInfos.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.Error($"Error getting screen info: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Belirli bir alanı yakalar
    /// </summary>
    public async Task<ScreenCaptureFrame> CaptureRegionAsync(int screenIndex, Rectangle region, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info($"Capturing region {region} from screen {screenIndex}");

            var fullFrame = await CaptureScreenAsync(screenIndex, cancellationToken);

            using var fullBitmap = new Bitmap(new MemoryStream(fullFrame.PngImage));

            // Region'ı kontrol et
            var screenBounds = new Rectangle(0, 0, fullFrame.Width, fullFrame.Height);
            region = Rectangle.Intersect(region, screenBounds);

            if (region.IsEmpty)
            {
                throw new ArgumentException("Invalid region specified", nameof(region));
            }

            using var croppedBitmap = new Bitmap(region.Width, region.Height);
            using var graphics = Graphics.FromImage(croppedBitmap);

            graphics.DrawImage(fullBitmap, 0, 0, region, GraphicsUnit.Pixel);

            using var memoryStream = new MemoryStream();
            croppedBitmap.Save(memoryStream, ImageFormat.Png);

            return new ScreenCaptureFrame
            {
                PngImage = memoryStream.ToArray(),
                Timestamp = DateTimeOffset.UtcNow,
                Width = region.Width,
                Height = region.Height,
                ScreenIndex = screenIndex,
                DeviceName = fullFrame.DeviceName
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Error capturing region from screen {screenIndex}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Tüm ekranları tek bir görüntüde birleştirir
    /// </summary>
    public async Task<ScreenCaptureFrame> CaptureVirtualScreenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info("Capturing virtual screen (all monitors combined)");

            // Virtual screen bounds'ları al
            var virtualX = GetSystemMetrics(SM_XVIRTUALSCREEN);
            var virtualY = GetSystemMetrics(SM_YVIRTUALSCREEN);
            var virtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var virtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            var virtualBounds = new Rectangle(virtualX, virtualY, virtualWidth, virtualHeight);

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                IntPtr desktopDC = IntPtr.Zero;
                IntPtr memoryDC = IntPtr.Zero;
                IntPtr bitmap = IntPtr.Zero;

                try
                {
                    desktopDC = GetDC(IntPtr.Zero);
                    memoryDC = CreateCompatibleDC(desktopDC);
                    bitmap = CreateCompatibleBitmap(desktopDC, virtualWidth, virtualHeight);

                    SelectObject(memoryDC, bitmap);

                    bool success = BitBlt(memoryDC, 0, 0, virtualWidth, virtualHeight,
                                        desktopDC, virtualX, virtualY, SRCCOPY);

                    if (!success)
                        throw new InvalidOperationException("Failed to capture virtual screen");

                    using var gdiplusBitmap = Image.FromHbitmap(bitmap);
                    using var clonedBitmap = new Bitmap(gdiplusBitmap);
                    using var memoryStream = new MemoryStream();

                    clonedBitmap.Save(memoryStream, ImageFormat.Png);

                    return new ScreenCaptureFrame
                    {
                        PngImage = memoryStream.ToArray(),
                        Timestamp = DateTimeOffset.UtcNow,
                        Width = virtualWidth,
                        Height = virtualHeight,
                        ScreenIndex = -1,
                        DeviceName = "Virtual Screen"
                    };
                }
                finally
                {
                    if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                    if (memoryDC != IntPtr.Zero) DeleteDC(memoryDC);
                    if (desktopDC != IntPtr.Zero) ReleaseDC(IntPtr.Zero, desktopDC);
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error capturing virtual screen: {ex.Message}");
            throw;
        }
    }
    private byte[] CreateScaledPreview(Bitmap original, float scale = 0.25f)
    {
        int newW = (int)(original.Width * scale);
        int newH = (int)(original.Height * scale);

        using var bmp = new Bitmap(newW, newH);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(original, 0, 0, newW, newH);


        g.DrawImage(original, 0, 0, newW, newH);

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }


    private void SaveAndOpenDebugPreview(byte[] previewPng, int screenIndex)
    {
        try
        {
            var folder = "capture-debug";
            Directory.CreateDirectory(folder);

            var file = Path.Combine(folder,
                $"screen_{screenIndex}_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");

            File.WriteAllBytes(file, previewPng);

            _logger.Debug($"[ScreenCapture DEBUG] Saved: {file}");

            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save/open preview: {ex.Message}");
        }
    }
}

