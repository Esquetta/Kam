using SmartVoiceAgent.Core.Dtos;
using System.Drawing;

namespace SmartVoiceAgent.Core.Interfaces;
/// <summary>
/// Provides methods for performing OCR on images.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Extracts text lines from the given bitmap image.
    /// </summary>
    /// <param name="image">The image to process.</param>
    /// <returns>Collection of OCR line results.</returns>
    Task<IEnumerable<OcrLine>> ExtractTextAsync(Bitmap image);
}