using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Helpers;
using System.Buffers;

namespace SmartVoiceAgent.Tests.Infrastructure.Helpers
{
    public class CircularAudioBufferTests
    {
        [Fact]
        public void Write_ReadAll_SimpleData()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(1024);
            var data = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            buffer.Write(data);
            var result = buffer.ReadAll();

            // Assert
            result.Should().Equal(data);
        }

        [Fact]
        public void Write_ReadAll_WithWrapAround()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(10);
            var data1 = new byte[] { 1, 2, 3, 4, 5 };
            var data2 = new byte[] { 6, 7, 8, 9, 10 };
            var data3 = new byte[] { 11, 12 }; // Will cause wrap

            // Act
            buffer.Write(data1);
            buffer.Write(data2);
            var read1 = buffer.ReadAll(); // Read clears buffer
            buffer.Write(data3); // Now write after clear
            var read2 = buffer.ReadAll();

            // Assert
            read1.Should().Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            read2.Should().Equal(data3);
        }

        [Fact]
        public void Write_BufferFull_OverwritesOldest()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(5);
            var data1 = new byte[] { 1, 2, 3, 4, 5 };
            var data2 = new byte[] { 6, 7, 8 }; // Will overwrite first 3

            // Act
            buffer.Write(data1);
            buffer.Write(data2);
            var result = buffer.ReadAll();

            // Assert - should have 4,5,6,7,8 (last 3 overwritten oldest 3)
            result.Should().Equal(new byte[] { 4, 5, 6, 7, 8 });
        }

        [Fact]
        public void ReadAll_EmptyBuffer_ReturnsEmpty()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);

            // Act
            var result = buffer.ReadAll();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void Clear_EmptiesBuffer()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            buffer.Write(new byte[] { 1, 2, 3 });

            // Act
            buffer.Clear();
            var result = buffer.ReadAll();

            // Assert
            result.Should().BeEmpty();
            buffer.Count.Should().Be(0);
        }

        [Fact]
        public void Count_ReflectsWrittenData()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            var data = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            buffer.Write(data);

            // Assert
            buffer.Count.Should().Be(data.Length);
        }

        [Fact]
        public void Count_AfterReadAll_ResetsToZero()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            buffer.Write(new byte[] { 1, 2, 3, 4, 5 });

            // Act
            buffer.ReadAll();

            // Assert
            buffer.Count.Should().Be(0);
        }

        [Fact]
        public void TryRead_BufferWithData_ReturnsTrueAndFillsSpan()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            var data = new byte[] { 1, 2, 3, 4, 5 };
            buffer.Write(data);
            var destination = new byte[5];

            // Act
            var result = buffer.TryRead(destination.AsSpan(), out int bytesRead);

            // Assert
            result.Should().BeTrue();
            bytesRead.Should().Be(5);
            destination.Should().Equal(data);
        }

        [Fact]
        public void TryRead_EmptyBuffer_ReturnsFalse()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            var destination = new byte[5];

            // Act
            var result = buffer.TryRead(destination.AsSpan(), out int bytesRead);

            // Assert
            result.Should().BeFalse();
            bytesRead.Should().Be(0);
        }

        [Fact]
        public void TryRead_SpanSmallerThanData_ReadsPartial()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            var data = new byte[] { 1, 2, 3, 4, 5 };
            buffer.Write(data);
            var destination = new byte[3]; // Smaller than data

            // Act
            var result = buffer.TryRead(destination.AsSpan(), out int bytesRead);

            // Assert
            result.Should().BeTrue();
            bytesRead.Should().Be(3);
            destination.Should().Equal(new byte[] { 1, 2, 3 });
            buffer.Count.Should().Be(2); // 2 bytes remaining
        }

        [Fact]
        public void ReadAllPooled_ReturnsAllData()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            var data = new byte[] { 1, 2, 3, 4, 5 };
            buffer.Write(data);

            // Act
            var result = buffer.ReadAllPooled(out int length);

            // Assert
            result.Should().NotBeNull();
            length.Should().Be(5);
            result.AsSpan(0, length).ToArray().Should().Equal(data);
            ArrayPool<byte>.Shared.Return(result);
        }

        [Fact]
        public void ReadAllPooled_EmptyBuffer_ReturnsEmpty()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);

            // Act
            var result = buffer.ReadAllPooled(out int length);

            // Assert
            result.Should().BeEmpty();
            length.Should().Be(0);
        }

        [Fact]
        public void WriteSpan_ReadBack()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            var data = new byte[] { 10, 20, 30, 40, 50 };

            // Act
            buffer.Write(data.AsSpan());
            var result = buffer.ReadAll();

            // Assert
            result.Should().Equal(data);
        }

        [Fact]
        public void Capacity_ReturnsCorrectValue()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(1024);

            // Assert
            buffer.Capacity.Should().Be(1024);
        }

        [Fact]
        public void IsEmpty_EmptyBuffer_ReturnsTrue()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);

            // Assert
            buffer.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void IsEmpty_AfterWrite_ReturnsFalse()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            buffer.Write(new byte[] { 1, 2, 3 });

            // Assert
            buffer.IsEmpty.Should().BeFalse();
        }

        [Fact]
        public void AvailableSpace_DecreasesAfterWrite()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            
            // Act & Assert
            buffer.AvailableSpace.Should().Be(100);
            buffer.Write(new byte[] { 1, 2, 3 });
            buffer.AvailableSpace.Should().Be(97);
        }

        [Fact]
        public void Skip_RemovesDataWithoutReading()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            buffer.Write(new byte[] { 1, 2, 3, 4, 5 });

            // Act
            var result = buffer.Skip(3);

            // Assert
            result.Should().BeTrue();
            buffer.Count.Should().Be(2);
            buffer.ReadAll().Should().Equal(new byte[] { 4, 5 });
        }

        [Fact]
        public void TryPeek_ViewsDataWithoutRemoving()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            buffer.Write(new byte[] { 1, 2, 3, 4, 5 });
            var destination = new byte[3];

            // Act
            var result = buffer.TryPeek(destination.AsSpan(), out int bytesRead);

            // Assert
            result.Should().BeTrue();
            bytesRead.Should().Be(3);
            destination.Should().Equal(new byte[] { 1, 2, 3 });
            buffer.Count.Should().Be(5); // Data still in buffer
        }

        [Fact]
        public void Concurrent_WriteAndRead_NoExceptions()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(1000);
            var exceptions = new List<Exception>();
            var tasks = new List<Task>();

            // Act - concurrent writers
            for (int i = 0; i < 5; i++)
            {
                int threadId = i;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (int j = 0; j < 100; j++)
                        {
                            buffer.Write(new byte[] { (byte)threadId, (byte)j });
                            Thread.Sleep(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions) { exceptions.Add(ex); }
                    }
                }));
            }

            // concurrent readers
            for (int i = 0; i < 2; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (int j = 0; j < 200; j++)
                        {
                            var data = buffer.ReadAll(); // Reads and clears
                            Thread.Sleep(2);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions) { exceptions.Add(ex); }
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Assert
            exceptions.Should().BeEmpty();
        }

        [Fact]
        public void Write_LargeData_OnlyKeepsRecent()
        {
            // Arrange
            var buffer = new CircularAudioBuffer(100);
            var largeData = new byte[200];
            new Random(42).NextBytes(largeData);

            // Act
            buffer.Write(largeData);

            // Assert - should only have 100 bytes (most recent)
            buffer.Count.Should().Be(100);
            buffer.IsEmpty.Should().BeFalse();
        }

        [Theory]
        [InlineData(1, 16000, 1, 16)] // 1 second at 16kHz, mono, 16-bit
        [InlineData(5, 16000, 1, 16)] // 5 seconds
        [InlineData(1, 44100, 2, 16)] // Stereo
        public void Constructor_WithAudioParams_CalculatesCorrectCapacity(int seconds, int sampleRate, int channels, int bitsPerSample)
        {
            // Arrange
            var expectedBytes = seconds * sampleRate * channels * (bitsPerSample / 8);

            // Act
            var buffer = new CircularAudioBuffer(seconds, sampleRate, channels, bitsPerSample);

            // Assert
            buffer.Capacity.Should().Be(expectedBytes);
        }
    }
}
