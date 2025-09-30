using SmartVoiceAgent.Core.Dtos;
using System.Drawing;

namespace SmartVoiceAgent.Core.Interfaces;
public interface IObjectDetectionService
{
    /// <summary>
    /// Detects objects in the given image and returns detection results.
    /// </summary>
    /// <param name="image">The image to analyze.</param>
    /// <returns>Collection of detected objects with their bounding boxes and confidence scores.</returns>
    Task<IEnumerable<ObjectDetectionItem>> DetectObjectsAsync(Bitmap image);
}