
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SmartVoiceAgent.Infrastructure.Helpers;

/// <summary>
/// High-performance circular buffer for audio data with reduced allocations and lock contention
/// </summary>
public class CircularAudioBuffer
{
    private readonly byte[] _buffer;
    private int _writeIndex;
    private int _readIndex;
    private readonly int _capacity;
    private int _count;
    private readonly object _lock = new();

    public CircularAudioBuffer(int capacityInSeconds, int sampleRate = 16000, int channels = 1, int bitsPerSample = 16)
    {
        _capacity = Math.Max(1024, capacityInSeconds * sampleRate * channels * (bitsPerSample / 8)); // Minimum 1KB buffer
        _buffer = GC.AllocateUninitializedArray<byte>(_capacity);
    }

    /// <summary>
    /// Creates a buffer with exact byte capacity
    /// </summary>
    public CircularAudioBuffer(int capacityBytes)
    {
        _capacity = Math.Max(1024, capacityBytes); // Minimum 1KB buffer
        _buffer = GC.AllocateUninitializedArray<byte>(_capacity);
    }

    /// <summary>
    /// Current number of bytes in buffer (thread-safe read)
    /// </summary>
    public int Count => Interlocked.CompareExchange(ref _count, 0, 0);

    /// <summary>
    /// Buffer capacity in bytes
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Available space in buffer
    /// </summary>
    public int AvailableSpace => _capacity - Count;

    /// <summary>
    /// True if buffer is empty
    /// </summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// Writes data to the buffer using bulk copy operations
    /// </summary>
    public void Write(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        Write(data.AsSpan());
    }

    /// <summary>
    /// Writes data span to the buffer (zero-copy where possible)
    /// </summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || _capacity == 0) return;

        lock (_lock)
        {
            // Ensure indices are valid
            _writeIndex = Math.Max(0, Math.Min(_writeIndex, _capacity - 1));
            _readIndex = Math.Max(0, Math.Min(_readIndex, _capacity - 1));
            
            int dataLength = data.Length;
            
            // Handle data larger than capacity - only keep the last _capacity bytes
            if (dataLength > _capacity)
            {
                data = data.Slice(dataLength - _capacity);
                dataLength = _capacity;
            }
            
            // Calculate how much we can write before wrapping
            int spaceToEnd = _capacity - _writeIndex;
            int firstChunkLength = Math.Min(dataLength, spaceToEnd);
            int secondChunkLength = dataLength - firstChunkLength;

            // First chunk: write from current position to end of buffer
            if (firstChunkLength > 0)
            {
                data.Slice(0, firstChunkLength).CopyTo(_buffer.AsSpan(_writeIndex, firstChunkLength));
            }

            // Second chunk: wrap around and write remaining data
            if (secondChunkLength > 0)
            {
                data.Slice(firstChunkLength, secondChunkLength).CopyTo(_buffer.AsSpan(0, secondChunkLength));
                _writeIndex = secondChunkLength;
            }
            else
            {
                _writeIndex += firstChunkLength;
                if (_writeIndex >= _capacity) _writeIndex = 0;
            }

            // Update count and read index if buffer is full
            int newCount = _count + dataLength;
            if (newCount > _capacity)
            {
                int overflow = newCount - _capacity;
                _readIndex = (_readIndex + overflow) % _capacity;
                newCount = _capacity;
            }
            _count = newCount;
        }
    }

    /// <summary>
    /// Reads all data from the buffer into a new array
    /// </summary>
    public byte[] ReadAll()
    {
        lock (_lock)
        {
            if (_count == 0) return Array.Empty<byte>();

            var result = GC.AllocateUninitializedArray<byte>(_count);
            ReadAllCore(result);
            return result;
        }
    }

    /// <summary>
    /// Reads all data from buffer into a rented array from ArrayPool
    /// Caller must return the array to the pool when done
    /// </summary>
    public byte[] ReadAllPooled(out int length)
    {
        lock (_lock)
        {
            length = _count;
            if (_count == 0) return Array.Empty<byte>();

            var result = ArrayPool<byte>.Shared.Rent(_count);
            ReadAllCore(result);
            return result;
        }
    }

    /// <summary>
    /// Tries to read data into the provided span without allocating
    /// </summary>
    public bool TryRead(Span<byte> destination, out int bytesRead)
    {
        lock (_lock)
        {
            bytesRead = Math.Min(_count, destination.Length);
            if (bytesRead == 0) return false;

            // Calculate how much we can read before wrapping
            int spaceToEnd = _capacity - _readIndex;
            int firstChunkLength = Math.Min(bytesRead, spaceToEnd);
            int secondChunkLength = bytesRead - firstChunkLength;

            // First chunk
            _buffer.AsSpan(_readIndex, firstChunkLength).CopyTo(destination);

            // Second chunk (if wrapped)
            if (secondChunkLength > 0)
            {
                _buffer.AsSpan(0, secondChunkLength).CopyTo(destination.Slice(firstChunkLength));
            }

            // Update state
            _readIndex = (_readIndex + bytesRead) % _capacity;
            _count -= bytesRead;

            return true;
        }
    }

    /// <summary>
    /// Peeks at data without removing it from the buffer
    /// </summary>
    public bool TryPeek(Span<byte> destination, out int bytesRead)
    {
        lock (_lock)
        {
            bytesRead = Math.Min(_count, destination.Length);
            if (bytesRead == 0) return false;

            int spaceToEnd = _capacity - _readIndex;
            int firstChunkLength = Math.Min(bytesRead, spaceToEnd);
            int secondChunkLength = bytesRead - firstChunkLength;

            _buffer.AsSpan(_readIndex, firstChunkLength).CopyTo(destination);

            if (secondChunkLength > 0)
            {
                _buffer.AsSpan(0, secondChunkLength).CopyTo(destination.Slice(firstChunkLength));
            }

            return true;
        }
    }

    /// <summary>
    /// Skips specified number of bytes without reading them
    /// </summary>
    public bool Skip(int count)
    {
        lock (_lock)
        {
            int toSkip = Math.Min(count, _count);
            if (toSkip == 0) return false;

            _readIndex = (_readIndex + toSkip) % _capacity;
            _count -= toSkip;
            return true;
        }
    }

    /// <summary>
    /// Clears the buffer
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        lock (_lock)
        {
            _count = 0;
            _readIndex = 0;
            _writeIndex = 0;
        }
    }

    /// <summary>
    /// Core read logic - assumes lock is held
    /// </summary>
    private void ReadAllCore(Span<byte> destination)
    {
        int spaceToEnd = _capacity - _readIndex;
        int firstChunkLength = Math.Min(_count, spaceToEnd);
        int secondChunkLength = _count - firstChunkLength;

        // First chunk: read from current position to end of buffer
        _buffer.AsSpan(_readIndex, firstChunkLength).CopyTo(destination);

        // Second chunk: wrap around and read remaining data
        if (secondChunkLength > 0)
        {
            _buffer.AsSpan(0, secondChunkLength).CopyTo(destination.Slice(firstChunkLength));
        }

        // Reset state
        _readIndex = 0;
        _writeIndex = 0;
        _count = 0;
    }
}
