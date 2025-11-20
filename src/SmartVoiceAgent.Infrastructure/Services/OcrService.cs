using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Dtos.Screen;
using SmartVoiceAgent.Core.Interfaces;
using System.Drawing;
using Tesseract;

public class OcrService : IOcrService
{
    private readonly string _tessDataPath;
    private readonly LoggerServiceBase _logger;

    public OcrService(LoggerServiceBase loggerServiceBase)
    {
        _tessDataPath = @"./tessdata";
        _logger = loggerServiceBase;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<OcrLine>> ExtractTextAsync(Bitmap image)
    {
        try
        {
            return await Task.Run(() =>
            {
                var results = new List<OcrLine>();

                using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
                using var pix = PixConverter.ToPix(image);
                using var page = engine.Process(pix);

                using var iterator = page.GetIterator();
                iterator.Begin();

                int lineIndex = 0;
                do
                {
                    if (iterator.IsAtBeginningOf(PageIteratorLevel.TextLine))
                    {
                        if (iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect))
                        {
                            var text = iterator.GetText(PageIteratorLevel.TextLine)?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                // satır bazlı confidence al
                                double confidence = iterator.GetConfidence(PageIteratorLevel.TextLine);

                                results.Add(new OcrLine(
                                    LineNumber: lineIndex++,
                                    Text: text,
                                    Confidence: confidence,
                                    BoundingBox: new Rectangle(rect.X1, rect.Y1, rect.Width, rect.Height)
                                ));
                            }
                        }
                    }
                }
                while (iterator.Next(PageIteratorLevel.TextLine));

                return results;
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Error while performing OCR. {ex.Message}");
            throw;
        }
    }
}
