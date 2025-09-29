using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
namespace SmartVoiceAgent.Infrastructure.Services
{
    /// <summary>
    /// Provides modern screen capture functionality using Windows.Graphics.Capture API.
    /// Requires Windows 10+ (19041) and net9.0-windows10.0.19041.0.
    /// </summary>
    public class WindowsScreenCaptureService : IScreenCaptureService
    {
        /// <inheritdoc />
        public async Task<IReadOnlyList<ScreenCaptureFrame>> CaptureAllAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<ScreenCaptureFrame>();
            var screens = Screen.AllScreens;

            for (int i = 0; i < screens.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = await CaptureScreenAsync(i, cancellationToken);
                if (frame != null)
                {
                    results.Add(frame);
                }
            }

            return results;
        }

        /// <inheritdoc />
        public Task<ScreenCaptureFrame> CaptureScreenAsync(int screenIndex, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var screens = Screen.AllScreens;
                if (screenIndex < 0 || screenIndex >= screens.Length)
                    throw new ArgumentOutOfRangeException(nameof(screenIndex), "Invalid screen index.");

                var screen = screens[screenIndex];
                var bounds = screen.Bounds;

                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(
                        sourceX: bounds.X,
                        sourceY: bounds.Y,
                        destinationX: 0,
                        destinationY: 0,
                        blockRegionSize: bounds.Size,
                        copyPixelOperation: CopyPixelOperation.SourceCopy);
                }

                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);

                return new ScreenCaptureFrame
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    PngImage = ms.ToArray(),
                    ScreenIndex = screenIndex,
                    DeviceName = screen.DeviceName
                };
            }, cancellationToken);
        }
    }
}