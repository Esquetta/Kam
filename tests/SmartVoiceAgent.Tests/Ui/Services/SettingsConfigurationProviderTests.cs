using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class SettingsConfigurationProviderTests
{
    [Fact]
    public void Reload_UpdatesConfigurationAndReportsChangedKeys()
    {
        var source = new SettingsConfigurationSource(new Dictionary<string, string?>
        {
            ["AIService:ModelId"] = "model-a",
            ["WebResearch:SearchApiKey"] = "key-1",
            ["McpOptions:TodoistApiKey"] = "todo"
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AIService:ModelId"] = "from-appsettings" })
            .Add(source)
            .Build();
        var reloads = 0;
        configuration.GetReloadToken().RegisterChangeCallback(_ => reloads++, null);

        var changed = source.Provider.Reload(new Dictionary<string, string?>
        {
            ["AIService:ModelId"] = "model-b",
            ["WebResearch:SearchApiKey"] = "key-1",
            ["WebResearch:SearchEngineId"] = "cx"
        });

        changed.Should().Equal("AIService:ModelId", "McpOptions:TodoistApiKey", "WebResearch:SearchEngineId");
        configuration["AIService:ModelId"].Should().Be("model-b");
        configuration["WebResearch:SearchEngineId"].Should().Be("cx");
        configuration["McpOptions:TodoistApiKey"].Should().BeNull();
        reloads.Should().Be(1);
    }

    [Fact]
    public void Reload_WithSameValues_ChangesNothing()
    {
        var values = new Dictionary<string, string?> { ["AIService:ModelId"] = "model-a" };
        var source = new SettingsConfigurationSource(values);
        var configuration = new ConfigurationBuilder().Add(source).Build();
        var reloads = 0;
        configuration.GetReloadToken().RegisterChangeCallback(_ => reloads++, null);

        var changed = source.Provider.Reload(new Dictionary<string, string?> { ["aiservice:modelid"] = "model-a" });

        changed.Should().BeEmpty();
        reloads.Should().Be(0);
    }
}
