using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
using SmartVoiceAgent.Infrastructure.Skills.Policy;

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
        provider.GetService<ISkillConfirmationService>().Should().NotBeNull();
        provider.GetService<ISkillHealthService>().Should().NotBeNull();
        provider.GetService<ISkillEvalHarness>().Should().NotBeNull();
        provider.GetService<ISkillEvalCaseCatalog>().Should().NotBeNull();
        provider.GetService<ISkillImportService>().Should().NotBeNull();
        provider.GetService<ISkillPolicyManager>().Should().NotBeNull();
    }

    [Fact]
    public void AddSmartVoiceAgent_RegistersExecutorForEveryBuiltInSkill()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddLogging();
        services.AddApplicationServices();
        services.AddInfrastructureServices(configuration);
        services.AddSmartVoiceAgent(configuration);
        using var provider = services.BuildServiceProvider();

        var executors = provider.GetServices<ISkillExecutor>().ToArray();

        var missingExecutorSkillIds = BuiltInSkillManifestCatalog.CreateAll()
            .Where(manifest => !executors.Any(executor => executor.CanExecute(manifest.Id)))
            .Select(manifest => manifest.Id)
            .ToArray();

        missingExecutorSkillIds.Should().BeEmpty();
    }
}
