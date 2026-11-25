using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.CliAgents;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class CliAgentsViewModelTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-cli-agents-viewmodel-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Refresh_ShowsVersionsSignInAndOnlyLetsCodexBeSwitchedOn()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var detector = new FixedDetector(
            new CliAgentStatus(CliAgentKind.Codex, true, "0.161.0", CliAgentSignIn.Subscription),
            new CliAgentStatus(CliAgentKind.ClaudeCode, true, "2.1.296", CliAgentSignIn.Subscription, "max"));
        var viewModel = new CliAgentsViewModel(settings, detector);

        await viewModel.RefreshAsync();

        var codex = viewModel.Agents.Single(row => row.Kind == CliAgentKind.Codex);
        codex.VersionText.Should().Be("v0.161.0");
        codex.StatusText.Should().Be("Signed in with ChatGPT");
        codex.Badge.IsSuccess.Should().BeTrue();
        codex.CanToggle.Should().BeTrue();
        codex.IsEnabled.Should().BeFalse("delegating spends the user's plan, so it starts off");

        var claude = viewModel.Agents.Single(row => row.Kind == CliAgentKind.ClaudeCode);
        claude.StatusText.Should().Be("Signed in with a Claude plan");
        claude.CanToggle.Should().BeFalse();
        claude.Note.Should().Contain("Anthropic's terms");
        claude.IsEnabled = true;
        claude.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task CodexSwitch_SavesTheSetting()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new CliAgentsViewModel(settings, new FixedDetector(
            new CliAgentStatus(CliAgentKind.Codex, true, "0.161.0", CliAgentSignIn.Subscription)));
        await viewModel.RefreshAsync();

        viewModel.Agents.Single(row => row.Kind == CliAgentKind.Codex).IsEnabled = true;

        settings.CodexCliEnabled.Should().BeTrue();
        using var reloaded = new JsonSettingsService(_settingsDirectory);
        reloaded.CodexCliEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, CliAgentSignIn.Unknown, "Not installed", "npm install -g @openai/codex")]
    [InlineData(true, CliAgentSignIn.SignedOut, "Not signed in", "codex login")]
    [InlineData(true, CliAgentSignIn.ApiKey, "Signed in with an API key", "API account")]
    public async Task CodexRow_ExplainsWhatToDoNext(bool installed, CliAgentSignIn signIn, string status, string note)
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new CliAgentsViewModel(settings, new FixedDetector(
            new CliAgentStatus(CliAgentKind.Codex, installed, installed ? "0.161.0" : null, signIn)));

        await viewModel.RefreshAsync();

        var codex = viewModel.Agents.Single(row => row.Kind == CliAgentKind.Codex);
        codex.StatusText.Should().Be(status);
        codex.Note.Should().Contain(note);
        codex.CanToggle.Should().Be(installed);
    }

    [Fact]
    public void WithoutDetector_RowsShowNotChecked()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);

        var viewModel = new IntegrationsViewModel(settings);

        viewModel.CliAgents.Agents.Should().HaveCount(2);
        viewModel.CliAgents.Agents.Should().OnlyContain(row => row.StatusText == "Not checked");
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }

    private sealed class FixedDetector(params CliAgentStatus[] statuses) : ICliAgentDetector
    {
        public Task<IReadOnlyList<CliAgentStatus>> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CliAgentStatus>>(statuses);
    }
}
