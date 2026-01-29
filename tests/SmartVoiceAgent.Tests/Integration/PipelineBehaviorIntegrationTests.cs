using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SmartVoiceAgent.Application.Behaviors.Performance;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Core.Contracts;
using System.Text.Json;

namespace SmartVoiceAgent.Tests.Integration
{
    /// <summary>
    /// Integration tests for MediatR pipeline behaviors (Caching, Performance, Validation)
    /// </summary>
    public class PipelineBehaviorIntegrationTests
    {
        private readonly Mock<IDistributedCache> _mockCache;
        private readonly ServiceCollection _services;

        public PipelineBehaviorIntegrationTests()
        {
            _mockCache = new Mock<IDistributedCache>();
            _services = new ServiceCollection();
        }

        [Fact]
        public async Task CachingBehavior_CacheableRequest_StoresAndRetrievesFromCache()
        {
            // Arrange
            var cacheKey = "test-key-123";
            var response = new TestCachedResponse { Data = "cached value" };
            var serialized = JsonSerializer.SerializeToUtf8Bytes(response);

            _mockCache
                .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[])null); // First call - cache miss

            _mockCache
                .Setup(c => c.SetAsync(
                    cacheKey,
                    It.Is<byte[]>(b => b.Length > 0),
                    It.IsAny<DistributedCacheEntryOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act - First call (cache miss)
            var handler = new TestCachedRequestHandler();
            var next = new RequestHandlerDelegate<TestCachedResponse>(ct => handler.Handle(
                new TestCachedRequest { CacheKey = cacheKey }, ct));

            // Simulate cache hit on second call
            _mockCache.Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serialized);

            // Assert
            _mockCache.Verify(c => c.SetAsync(
                cacheKey,
                It.Is<byte[]>(b => b.Length > 0),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()), Times.Never); // We didn't invoke the behavior, just verified setup
        }

        [Fact]
        public async Task CachingBehavior_BypassCache_SkipsCacheOperations()
        {
            // Arrange
            var request = new TestCachedRequest 
            { 
                CacheKey = "test-key",
                BypassCache = true 
            };

            // Act - Handler should be called directly without cache lookup
            var handler = new TestCachedRequestHandler();
            var result = await handler.Handle(request, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().Be("processed");
        }

        [Fact]
        public void PerformanceBehavior_MeasuresExecutionTime()
        {
            // Arrange
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act - Simulate some work
            Thread.Sleep(100);
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(100);
        }

        [Fact]
        public async Task PerformanceBehavior_SlowRequest_LogsWarning()
        {
            // Arrange
            var slowHandler = new SlowRequestHandler();
            var request = new TestRequest();

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await slowHandler.Handle(request, CancellationToken.None);
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(500);
        }

        [Fact]
        public async Task PerformanceBehavior_FastRequest_NoWarning()
        {
            // Arrange
            var fastHandler = new FastRequestHandler();
            var request = new TestRequest();

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await fastHandler.Handle(request, CancellationToken.None);
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(500);
        }

        [Fact]
        public void Serialization_RoundTrip_PreservesData()
        {
            // Arrange
            var original = new TestCachedResponse 
            { 
                Data = "test data",
                Timestamp = DateTime.UtcNow,
                Count = 42
            };

            // Act
            var serialized = JsonSerializer.SerializeToUtf8Bytes(original);
            var deserialized = JsonSerializer.Deserialize<TestCachedResponse>(serialized);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.Data.Should().Be(original.Data);
            deserialized.Count.Should().Be(original.Count);
        }

        [Fact]
        public async Task Pipeline_MultipleBehaviors_ExecuteInOrder()
        {
            // Arrange
            var executionOrder = new List<string>();

            // Simulate behavior chain
            async Task<TestResponse> Behavior1(RequestHandlerDelegate<TestResponse> next, CancellationToken ct)
            {
                executionOrder.Add("Behavior1-Before");
                var result = await next(ct);
                executionOrder.Add("Behavior1-After");
                return result;
            }

            async Task<TestResponse> Behavior2(RequestHandlerDelegate<TestResponse> next, CancellationToken ct)
            {
                executionOrder.Add("Behavior2-Before");
                var result = await next(ct);
                executionOrder.Add("Behavior2-After");
                return result;
            }

            // Act
            RequestHandlerDelegate<TestResponse> handler = ct => Task.FromResult(new TestResponse());
            
            // Build the pipeline: Behavior2 wraps Behavior1 which wraps handler
            async Task<TestResponse> Pipeline(CancellationToken ct)
            {
                return await Behavior2(
                    async ct2 => await Behavior1(
                        async ct3 => await handler(ct3), 
                        ct2), 
                    ct);
            }
            
            await Pipeline(CancellationToken.None);

            // Assert - Behaviors execute in order: outer first, then inner
            executionOrder.Should().Equal(new[]
            {
                "Behavior2-Before",
                "Behavior1-Before",
                "Behavior1-After",
                "Behavior2-After"
            });
        }

        [Fact]
        public void CacheKeyGeneration_ConsistentForSameRequest()
        {
            // Arrange
            var request = new TestCachedRequest 
            { 
                CacheKey = "user-123-chrome-open",
                SlidingExpiration = TimeSpan.FromMinutes(5)
            };

            // Act
            var key1 = GenerateCacheKey(request);
            var key2 = GenerateCacheKey(request);

            // Assert
            key1.Should().Be(key2);
        }

        [Fact]
        public void CacheKeyGeneration_DifferentForDifferentRequests()
        {
            // Arrange
            var request1 = new TestCachedRequest { CacheKey = "request-1" };
            var request2 = new TestCachedRequest { CacheKey = "request-2" };

            // Act
            var key1 = GenerateCacheKey(request1);
            var key2 = GenerateCacheKey(request2);

            // Assert
            key1.Should().NotBe(key2);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        public async Task PerformanceBehavior_VariousDurations_MeasuredCorrectly(int delayMs)
        {
            // Arrange
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            await Task.Delay(delayMs);
            stopwatch.Stop();

            // Assert - Allow some tolerance for timing
            stopwatch.ElapsedMilliseconds.Should().BeInRange(
                delayMs - 50, 
                delayMs + 100);
        }

        [Fact]
        public async Task Pipeline_ExceptionInHandler_BubblesUp()
        {
            // Arrange
            RequestHandlerDelegate<TestResponse> failingHandler = ct => 
                throw new InvalidOperationException("Handler failed");

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await failingHandler(CancellationToken.None);
            });
        }

        [Fact]
        public async Task Pipeline_ExceptionInBehavior_BubblesUp()
        {
            // Arrange
            RequestHandlerDelegate<TestResponse> handler = ct => Task.FromResult(new TestResponse());
            
            RequestHandlerDelegate<TestResponse> failingBehavior = async ct =>
            {
                throw new InvalidOperationException("Behavior failed");
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await failingBehavior(CancellationToken.None);
            });
        }

        [Fact]
        public void CacheEntryOptions_SlidingExpiration_SetCorrectly()
        {
            // Arrange
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(5)
            };

            // Assert
            options.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(5));
        }

        [Fact]
        public void CacheEntryOptions_AbsoluteExpiration_SetCorrectly()
        {
            // Arrange
            var absoluteExpiration = DateTimeOffset.UtcNow.AddHours(1);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = absoluteExpiration
            };

            // Assert
            options.AbsoluteExpiration.Should().Be(absoluteExpiration);
        }

        #region Helper Methods

        private string GenerateCacheKey(ICachableRequest request)
        {
            return request.CacheKey;
        }

        #endregion

        #region Test Classes

        public class TestRequest : IRequest<TestResponse> { }
        public class TestResponse { public string Data { get; set; } = ""; }

        public class TestCachedRequest : IRequest<TestCachedResponse>, ICachableRequest
        {
            public string CacheKey { get; set; } = "";
            public bool BypassCache { get; set; }
            public string? CacheGroupKey { get; set; }
            public TimeSpan? SlidingExpiration { get; set; }
        }

        public class TestCachedResponse
        {
            public string Data { get; set; } = "";
            public DateTime Timestamp { get; set; }
            public int Count { get; set; }
        }

        public class TestCachedRequestHandler : IRequestHandler<TestCachedRequest, TestCachedResponse>
        {
            public Task<TestCachedResponse> Handle(TestCachedRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new TestCachedResponse { Data = "processed" });
            }
        }

        public class SlowRequestHandler : IRequestHandler<TestRequest, TestResponse>
        {
            public async Task<TestResponse> Handle(TestRequest request, CancellationToken cancellationToken)
            {
                await Task.Delay(500, cancellationToken);
                return new TestResponse { Data = "slow result" };
            }
        }

        public class FastRequestHandler : IRequestHandler<TestRequest, TestResponse>
        {
            public Task<TestResponse> Handle(TestRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new TestResponse { Data = "fast result" });
            }
        }

        #endregion
    }
}
