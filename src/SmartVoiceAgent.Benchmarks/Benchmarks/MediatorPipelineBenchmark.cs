using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Handlers.CommandHandlers;
using SmartVoiceAgent.Application.Behaviors.Logging;
using SmartVoiceAgent.Application.Behaviors.Validation;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;
using SmartVoiceAgent.Core.Config;
using Core.CrossCuttingConcerns.Logging.Serilog;

namespace SmartVoiceAgent.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class MediatorPipelineBenchmark
{
    private IMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Register MediatR
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<PlayMusicCommand>();
            cfg.RegisterServicesFromAssemblyContaining<PlayMusicCommandHandler>();
        });

        // Register pipelines
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));

        // Register services
        services.AddSingleton<LoggerServiceBase, DummyLogger>();
        services.AddSingleton<ICacheService, DummyCacheService>();
        
        // Configuration - use environment variables or empty config for benchmarks
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // MongoDB config removed - should be set via environment variables for benchmarks
                { "MongoDbConfiguration:Collection", "logs" },
            })
            .AddEnvironmentVariables()
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        var provider = services.BuildServiceProvider();
        _mediator = provider.GetRequiredService<IMediator>();
    }

    [Benchmark]
    public async Task Send_PlayMusicCommand()
    {
        var command = new PlayMusicCommand("Metallica");
        await _mediator.Send(command);
    }
}

// Dummy logger for benchmarks - does nothing
public class DummyLogger : LoggerServiceBase
{
    public DummyLogger() : base() { }
}

// Cache interface
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, int slidingExpirationSeconds = 60, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveGroupAsync(string groupKey, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(string key, CancellationToken cancellationToken = default);
}

// Dummy cache for benchmarks
public class DummyCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
    public Task SetAsync<T>(string key, T value, int slidingExpirationSeconds = 60, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveGroupAsync(string groupKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> AnyAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
