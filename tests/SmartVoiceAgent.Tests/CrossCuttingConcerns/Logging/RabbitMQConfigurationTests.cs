using Core.CrossCuttingConcerns.Logging.Serilog.ConfigurationModels;
using FluentAssertions;

namespace SmartVoiceAgent.Tests.CrossCuttingConcerns.Logging;

public class RabbitMQConfigurationTests
{
    [Fact]
    public void Defaults_AreNonNullForLoggerBinding()
    {
        var configuration = new RabbitMQConfiguration();

        configuration.Exchange.Should().BeEmpty();
        configuration.Username.Should().BeEmpty();
        configuration.Password.Should().BeEmpty();
        configuration.ExchangeType.Should().BeEmpty();
        configuration.RouteKey.Should().BeEmpty();
        configuration.Hostnames.Should().BeEmpty();
    }
}
