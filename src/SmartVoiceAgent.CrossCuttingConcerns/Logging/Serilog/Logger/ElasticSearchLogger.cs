using Core.CrossCuttingConcerns.Logging.Serilog.ConfigurationModels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Serilog.Sinks;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Core.CrossCuttingConcerns.Logging.Serilog.Logger;

public class ElasticSearchLogger : LoggerServiceBase
{
    public ElasticSearchLogger(IConfiguration configuration)
    {
        const string configurationSection = "SeriLogConfigurations:ElasticSearchConfiguration";
        ElasticSearchConfiguration logConfiguration =
            configuration.GetSection(configurationSection).Get<ElasticSearchConfiguration>()
            ?? throw new NullReferenceException($"\"{configurationSection}\" section cannot found in configuration.");

        Logger = new LoggerConfiguration().WriteTo
            .Elasticsearch(
                [new Uri(logConfiguration.ConnectionString)],
                options => { options.BootstrapMethod = BootstrapMethod.Failure; }
            )
            .CreateLogger();
    }
}
