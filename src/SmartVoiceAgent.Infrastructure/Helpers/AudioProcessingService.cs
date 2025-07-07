using Core.CrossCuttingConcerns.Logging.Serilog;
using NAudio.Wave;
using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Infrastructure.Helpers
{
    public class AudioProcessingService : IDisposable
    {
        private readonly LoggerServiceBase _logger;
        private readonly HuggingFaceSTTService _sttService;

        public AudioProcessingService(
            LoggerServiceBase logger,
            HuggingFaceSTTService sttService)
        {
            _logger = logger;
            _sttService = sttService;
        }

        public async Task<SpeechResult> ProcessAudioFromRecording(byte[] rawAudioData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.Debug($"Processing audio data:  {rawAudioData.Length} bytes");

                // Raw audio'yu WAV formatına çevir
                var wavData = ConvertToWav(rawAudioData);

                // Minimum ses kontrolü
                if (!HasSufficientAudio(wavData))
                {
                    return new SpeechResult { ErrorMessage = "Insufficient audio data for processing" };
                }

                // STT servisi ile işle
                var result = await _sttService.ConvertToTextAsync(wavData, cancellationToken);

                _logger.Info($"Audio processed successfully: {result.Text}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return new SpeechResult { ErrorMessage = $"Audio processing failed: {ex.Message}" };
            }
        }

        private byte[] ConvertToWav(byte[] rawAudioData)
        {
            try
            {
                using var memoryStream = new MemoryStream();

                // WAV formatı: 16kHz, 16-bit, mono (NAudio'dan geldiği için bu format)
                var waveFormat = new WaveFormat(16000, 16, 1);

                using var waveWriter = new WaveFileWriter(memoryStream, waveFormat);
                waveWriter.Write(rawAudioData, 0, rawAudioData.Length);
                waveWriter.Flush();

                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                throw;
            }
        }

        private bool HasSufficientAudio(byte[] audioData)
        {
            // WAV header (44 bytes) + en az 1 saniye ses (16000 samples * 2 bytes)
            const int minSizeBytes = 44 + (16000 * 2);
            return audioData.Length >= minSizeBytes;
        }

        public void Dispose()
        {
            _sttService?.Dispose();
        }
    }
}
