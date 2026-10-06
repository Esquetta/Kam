using Core.CrossCuttingConcerns.Logging.Serilog;
using Core.CrossCuttingConcerns.Logging.Serilog.Logger;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.DependencyInjection;

namespace SmartVoiceAgent.Tests.Application.DependencyInjection;

public class PipelineLoggerRegistrationTests
{
    [Fact]
    public void AddApplicationServices_WithoutMongoDbConfiguration_ResolvesFileLogger()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddApplicationServices();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<LoggerServiceBase>().Should().BeOfType<LocalFileLogger>();
    }

    [Fact]
    public void CreatePipelineLogger_WithoutConfiguration_UsesFileLogger()
    {
        using var logger = (LocalFileLogger)ServiceRegistration.CreatePipelineLogger(null);

        logger.LogDirectory.Should().Be(LocalFileLogger.DefaultDirectory());
    }

    [Fact]
    public void CreatePipelineLogger_WithMongoDbConnectionString_UsesMongoDbLogger()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDbConfiguration:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoDbConfiguration:Database"] = "Kam",
                ["MongoDbConfiguration:Collection"] = "logs"
            })
            .Build();

        ServiceRegistration.CreatePipelineLogger(configuration).Should().BeOfType<MongoDbLogger>();
    }

    [Fact]
    public void LocalFileLogger_WritesMessagesToDailyFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kam-log-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = new LocalFileLogger(directory))
            {
                logger.Info("search started");
                logger.Error("search failed");
            }

            var file = Directory.GetFiles(directory, "pipeline-*.log").Should().ContainSingle().Subject;
            var text = File.ReadAllText(file);
            text.Should().Contain("[Information] search started");
            text.Should().Contain("[Error] search failed");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
