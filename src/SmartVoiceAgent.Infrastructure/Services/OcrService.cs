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
        _tessDataPath = ResolveTessDataPath();
        _logger = loggerServiceBase;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<OcrLine>> ExtractTextAsync(Bitmap image)
    {
        try
        {
            if (!File.Exists(Path.Combine(_tessDataPath, "eng.traineddata")))
            {
                _logger.Warn($"OCR tessdata not found at '{_tessDataPath}'. Returning an empty OCR result.");
                return [];
            }

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
            return [];
        }
    }

    private static string ResolveTessDataPath()
    {
        var environmentPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        var candidates = new[]
        {
            environmentPath,
            Path.Combine(AppContext.BaseDirectory, "tessdata"),
            Path.Combine(Environment.CurrentDirectory, "tessdata"),
            "./tessdata"
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(fullPath, "eng.traineddata")))
            {
                return fullPath;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "tessdata");
    }
}
