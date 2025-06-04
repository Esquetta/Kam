using Core.CrossCuttingConcerns.Logging.Serilog.ConfigurationModels;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Json;
using Serilog.Sinks.RabbitMQ;

namespace Core.CrossCuttingConcerns.Logging.Serilog.Logger;

public class RabbitMQLogger : LoggerServiceBase
{
    public RabbitMQLogger(IConfiguration configuration)
    {
        const string configurationSection = "SeriLogConfigurations:RabbitMQConfiguration";
        RabbitMQConfiguration rabbitMQConfiguration =
            configuration.GetSection(configurationSection).Get<RabbitMQConfiguration>()
            ?? throw new NullReferenceException($"\"{configurationSection}\" section cannot found in configuration.");

        RabbitMQClientConfiguration config =
            new()
            {
                Port = rabbitMQConfiguration.Port,
                DeliveryMode = RabbitMQDeliveryMode.Durable,
                Exchange = rabbitMQConfiguration.Exchange,
                Username = rabbitMQConfiguration.Username,
                Password = rabbitMQConfiguration.Password,
                ExchangeType = rabbitMQConfiguration.ExchangeType,
                RoutingKey = rabbitMQConfiguration.RouteKey,
            };
        rabbitMQConfiguration.Hostnames.ForEach(config.Hostnames.Add);

        Logger = new LoggerConfiguration().WriteTo
            .RabbitMQ(
                (clientConfiguration, sinkConfiguration) =>
                {
                    clientConfiguration.Port = config.Port;
                    clientConfiguration.DeliveryMode = config.DeliveryMode;
                    clientConfiguration.Exchange = config.Exchange;
                    clientConfiguration.Username = config.Username;
                    clientConfiguration.Password = config.Password;
                    clientConfiguration.ExchangeType = config.ExchangeType;
                    clientConfiguration.RoutingKey = config.RoutingKey;
                    config.Hostnames.ToList().ForEach(clientConfiguration.Hostnames.Add);
                    sinkConfiguration.TextFormatter = new JsonFormatter();
                }
            )
            .CreateLogger();
    }
}
