using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Tools;

public sealed class TransientFileAccessTests : IDisposable
{
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "kam-transient-file-tests", Guid.NewGuid().ToString("N"));

    public TransientFileAccessTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task RetriesWhileAnotherProcessHoldsTheFile()
    {
        var calls = 0;
        var waits = new List<TimeSpan>();

        await TransientFileAccess.RunAsync(
            () =>
            {
                calls++;
                return calls < 3 ? throw new IOException("in use", SharingViolationHResult) : Task.CompletedTask;
            },
            wait =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            });

        calls.Should().Be(3);
        waits.Should().Equal(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task OtherErrorsAreNotRetried()
    {
        var calls = 0;

        var act = () => TransientFileAccess.RunAsync(
            () =>
            {
                calls++;
                throw new FileNotFoundException("missing");
            },
            _ => Task.CompletedTask);

        await act.Should().ThrowAsync<FileNotFoundException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GivesUpWhenTheLockOutlastsTheRetries()
    {
        var calls = 0;

        var act = () => TransientFileAccess.RunAsync(
            () =>
            {
                calls++;
                throw new IOException("in use", SharingViolationHResult);
            },
            _ => Task.CompletedTask);

        await act.Should().ThrowAsync<IOException>();
        calls.Should().Be(5);
    }

    [Fact]
    public async Task MoveFileAsync_WaitsForABriefLockToEnd()
    {
        var source = Path.Combine(_root, "a.txt");
        var destination = Path.Combine(_root, "a.md");
        await File.WriteAllTextAsync(source, "alpha");

        // Holds the file the way a virus scanner does right after a write, then lets go.
        var holder = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(120);
            await holder.DisposeAsync();
        });

        var result = await new FileAgentTools(_root).MoveFileAsync(source, destination);
        await release;

        result.Should().StartWith("File moved");
        File.Exists(destination).Should().BeTrue();
        File.Exists(source).Should().BeFalse();
    }
}
