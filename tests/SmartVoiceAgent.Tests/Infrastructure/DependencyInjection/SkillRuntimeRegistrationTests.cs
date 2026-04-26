using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.DependencyInjection;

namespace SmartVoiceAgent.Tests.Infrastructure.DependencyInjection;

public class SkillRuntimeRegistrationTests
{
    [Fact]
    public void AddInfrastructureServices_RegistersSkillRegistryAndExecutors()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddLogging();
        services.AddInfrastructureServices(configuration);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetService<ISkillRegistry>();

        registry.Should().NotBeNull();
        registry!.TryGet("apps.open", out var appSkill).Should().BeTrue();
        appSkill!.Enabled.Should().BeTrue();
        appSkill.Arguments.Should().Contain(argument =>
            argument.Name == "applicationName"
            && argument.Required
            && argument.Type == SmartVoiceAgent.Core.Models.Skills.SkillArgumentType.String);
        appSkill.TimeoutMilliseconds.Should().BeGreaterThan(0);
        provider.GetServices<ISkillExecutor>().Should().NotBeEmpty();
        provider.GetService<ISkillExecutionPipeline>().Should().NotBeNull();
        provider.GetService<ICommandRuntimeService>().Should().NotBeNull();
        provider.GetService<ISkillHealthService>().Should().NotBeNull();
        provider.GetService<ISkillEvalHarness>().Should().NotBeNull();
        provider.GetService<ISkillEvalCaseCatalog>().Should().NotBeNull();
    }
}
