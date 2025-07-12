

namespace SmartVoiceAgent.Infrastructure.Helpers;

public class CircularAudioBuffer
{
    private readonly byte[] _buffer;
    private int _writeIndex = 0;
    private int _readIndex = 0;
    private readonly int _capacity;
    private readonly object _lock = new object();
    private int _count = 0;

    public CircularAudioBuffer(int capacityInSeconds, int sampleRate = 16000, int channels = 1, int bitsPerSample = 16)
    {
        _capacity = capacityInSeconds * sampleRate * channels * (bitsPerSample / 8);
        _buffer = new byte[_capacity];
    }

    public void Write(byte[] data)
    {
        lock (_lock)
        {
            foreach (byte b in data)
            {
                _buffer[_writeIndex] = b;
                _writeIndex = (_writeIndex + 1) % _capacity;

                if (_count < _capacity)
                {
                    _count++;
                }
                else
                {
                    // Buffer dolu, eski veriyi üzerine yaz
                    _readIndex = (_readIndex + 1) % _capacity;
                }
            }
        }
    }

    public byte[] ReadAll()
    {
        lock (_lock)
        {
            if (_count == 0) return new byte[0];

            var result = new byte[_count];
            var readPos = _readIndex;

            for (int i = 0; i < _count; i++)
            {
                result[i] = _buffer[readPos];
                readPos = (readPos + 1) % _capacity;
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _count = 0;
            _readIndex = 0;
            _writeIndex = 0;
        }
    }

    public int Count => _count;
}

