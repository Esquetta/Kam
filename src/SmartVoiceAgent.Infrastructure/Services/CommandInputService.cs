using SmartVoiceAgent.Core.Interfaces;
using System.Threading.Channels;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Channel-based implementation of command input service
/// </summary>
public class CommandInputService : ICommandInputService
{
    private readonly Channel<string> _commandChannel;

    public event EventHandler<CommandResultEventArgs>? OnResult;

    public CommandInputService()
    {
        // Create unbounded channel for command queue
        _commandChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void SubmitCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        _commandChannel.Writer.TryWrite(command);
    }

    public async Task<string> ReadCommandAsync(CancellationToken cancellationToken = default)
    {
        await _commandChannel.Reader.WaitToReadAsync(cancellationToken);
        return await _commandChannel.Reader.ReadAsync(cancellationToken);
    }

    public void PublishResult(string command, string result, bool success = true)
    {
        OnResult?.Invoke(this, new CommandResultEventArgs
        {
            Command = command,
            Result = result,
            Success = success
        });
    }
}
