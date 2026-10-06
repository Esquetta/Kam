using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;

namespace SmartVoiceAgent.Tests.Infrastructure.DependencyInjection;

public sealed class CompositionRootTests
{
    /// <summary>
    /// Builds what the desktop app builds, with an AI key set as Settings would, and resolves every
    /// service. A constructor that throws for a default install (such as the MongoDB logger did when no
    /// log database was configured) fails here instead of in the user's first web search.
    /// </summary>
    [Fact]
    public void FullComposition_ResolvesEveryRegisteredService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AIService:Provider"] = "OpenRouter",
                ["AIService:Endpoint"] = "https://openrouter.ai/api/v1",
                ["AIService:ApiKey"] = "sk-test",
                ["AIService:ModelId"] = "openai/gpt-4.1-mini"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApplicationServices();
        services.AddInfrastructureServices(configuration);
        services.AddSmartVoiceAgent(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var failures = new List<string>();
        foreach (var serviceType in services
            .Where(descriptor => !descriptor.ServiceType.ContainsGenericParameters)
            .Select(descriptor => descriptor.ServiceType)
            .Distinct())
        {
            try
            {
                scope.ServiceProvider.GetServices(serviceType).ToList();
            }
            catch (Exception ex)
            {
                failures.Add($"{serviceType.FullName}: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }
}
