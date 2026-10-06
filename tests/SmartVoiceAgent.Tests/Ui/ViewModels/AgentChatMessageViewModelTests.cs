using FluentAssertions;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class AgentChatMessageViewModelTests
{
    [Fact]
    public void AppendContent_WhileStreamingOnUiThread_RefreshesTextInBatches()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new HeldContext());
        try
        {
            var message = AgentChatMessageViewModel.Agent(string.Empty);
            message.IsStreaming = true;
            var refreshes = 0;
            message.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AgentChatMessageViewModel.Content))
                {
                    refreshes++;
                }
            };

            for (var token = 0; token < 100; token++)
            {
                message.AppendContent("x");
            }

            refreshes.Should().BeLessThan(10, "tokens arriving together share one refresh");

            message.IsStreaming = false;

            message.Content.Should().Be(new string('x', 100));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void AppendContent_WhenNotStreaming_ShowsTextAtOnce()
    {
        var message = AgentChatMessageViewModel.Agent("Hello");

        message.AppendContent(" world");

        message.Content.Should().Be("Hello world");
    }

    /// <summary>A UI context whose posted callbacks never run, so the test controls every refresh.</summary>
    private sealed class HeldContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }
}
