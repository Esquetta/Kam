using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;
using SmartVoiceAgent.Infrastructure.Skills.Execution;
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
        provider.GetService<ISkillExecutionHistoryService>().Should().BeOfType<JsonSkillExecutionHistoryService>();
        provider.GetService<ISkillPlannerTraceStore>().Should().NotBeNull();
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

    [Fact]
    public void AddSmartVoiceAgent_AnthropicConfiguration_ResolvesChatClient()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AIService:Provider"] = "Anthropic",
                ["AIService:Endpoint"] = "https://api.anthropic.com",
                ["AIService:ApiKey"] = "sk-ant-test",
                ["AIService:ModelId"] = "claude-sonnet-4-6",
                ["AIService:DefaultMaxTokens"] = "1200"
            })
            .Build();

        services.AddLogging();
        services.AddSmartVoiceAgent(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IChatClient>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddSmartVoiceAgent_CodingAgentMode_ScopesFileAndShellSkillsToWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-coding-di-{Guid.NewGuid():N}");
        var outsideWorkspace = Path.Combine(Path.GetTempPath(), $"kam-coding-di-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outsideWorkspace);
        var outsideFile = Path.Combine(outsideWorkspace, "secret.txt");
        var policyFile = Path.Combine(workspace, "skill-policies.json");
        await File.WriteAllTextAsync(outsideFile, "outside");
        var policyStore = new JsonSkillPolicyStore(policyFile);
        policyStore.SaveState(new SkillPolicyState
        {
            SkillId = "shell.run",
            Enabled = true,
            ReviewRequired = false,
            RuntimeOptions = new Dictionary<string, string>
            {
                [SkillRuntimePolicyOptions.ShellBlockedPatterns] = "custom-blocked-token"
            }
        });

        try
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CodingAgent:IsEnabled"] = "true",
                    ["CodingAgent:WorkspaceRoot"] = workspace,
                    ["CodingAgent:RequireShellAllowList"] = "true"
                })
                .Build();

            services.AddLogging();
            services.AddApplicationServices();
            services.AddInfrastructureServices(configuration);
            services.AddSmartVoiceAgent(configuration);
            services.AddSingleton<ISkillPolicyStore>(policyStore);
            using var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<ISkillRegistry>();
            registry.TryGet("shell.run", out var shellSkill).Should().BeTrue();
            shellSkill!.RuntimeOptions.Should().Contain(
                SkillRuntimePolicyOptions.ShellAllowedWorkingDirectories,
                Path.GetFullPath(workspace));
            shellSkill.RuntimeOptions.Should().Contain(
                SkillRuntimePolicyOptions.ShellRequireAllowedCommands,
                "true");
            shellSkill.RuntimeOptions.Should().Contain(
                SkillRuntimePolicyOptions.ShellBlockedPatterns,
                "custom-blocked-token");

            var fileExecutor = provider.GetServices<ISkillExecutor>()
                .OfType<FileSkillExecutor>()
                .Single();
            var result = await fileExecutor.ExecuteAsync(
                SmartVoiceAgent.Core.Models.Skills.SkillPlan.FromObject(
                    "file.read",
                    new { filePath = outsideFile }));

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("reddedildi");
        }
        finally
        {
            TryDeleteDirectory(workspace);
            TryDeleteDirectory(outsideWorkspace);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Cleanup must not hide assertion failures.
        }
    }
}
