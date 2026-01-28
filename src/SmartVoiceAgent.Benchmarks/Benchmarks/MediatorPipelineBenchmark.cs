using BenchmarkDotNet.Attributes;
using Core.CrossCuttingConcerns.Logging.Serilog;
using Core.CrossCuttingConcerns.Logging.Serilog.Logger;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.Behaviors.Logging;
using SmartVoiceAgent.Application.Behaviors.Performance;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Benchmarks;

[MemoryDiagnoser]
public class MediatorPipelineBenchmark
{
    private IMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PlayMusicCommand).Assembly);
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            
        });

        services.AddScoped<IMusicService, FakeMusicService>();
        services.AddDistributedMemoryCache();
        services.AddSingleton(new CacheSettings { SlidingExpiration = 1 });
        

        var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        { "MongoDbConfiguration:ConnectionString",  "mongodb+srv://sa:5PeqEeVgPHMjRJfB@cluster0.swru2ne.mongodb.net/?retryWrites=true&w=majority" },
        { "MongoDbConfiguration:Collection",  "logs" },
    })
    .Build();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddSingleton<LoggerServiceBase, MongoDbLogger>();
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

public class FakeMusicService : IMusicService
{
    public Task PlayMusicAsync(string filePath, bool loop = false, CancellationToken cancellationToken = default) 
        => Task.CompletedTask;

    public Task PauseMusicAsync(CancellationToken cancellationToken = default) 
        => Task.CompletedTask;

    public Task ResumeMusicAsync(CancellationToken cancellationToken = default) 
        => Task.CompletedTask;

    public Task StopMusicAsync(CancellationToken cancellationToken = default) 
        => Task.CompletedTask;

    public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default) 
        => Task.CompletedTask;
}
