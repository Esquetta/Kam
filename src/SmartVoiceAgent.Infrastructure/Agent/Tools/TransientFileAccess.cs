namespace SmartVoiceAgent.Infrastructure.Agent.Tools;

/// <summary>
/// Retries a file operation that failed because another process briefly held the file.
/// On Windows a virus scanner or the search indexer opens new files for a moment after they are
/// written; moving such a file 300 ms after writing it failed about one time in four on a test
/// machine, and one short retry was always enough.
/// </summary>
public static class TransientFileAccess
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400)
    ];

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying a few times while the file is locked by another process.
    /// Any other error, or a lock that outlasts the retries, is thrown as is.
    /// </summary>
    /// <param name="operation">The file operation.</param>
    /// <param name="delay">Waits between attempts; replaced in tests.</param>
    public static async Task RunAsync(Func<Task> operation, Func<TimeSpan, Task>? delay = null)
    {
        delay ??= wait => Task.Delay(wait);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (IOException ex) when (IsLockedByAnotherProcess(ex) && attempt < Delays.Length)
            {
                await delay(Delays[attempt]);
            }
        }
    }

    /// <summary>
    /// Runs a synchronous file operation with the same retries as <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="operation">The file operation.</param>
    public static Task RunAsync(Action operation)
    {
        return RunAsync(() =>
        {
            operation();
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// True for the Windows sharing and lock violations: another process has the file open.
    /// </summary>
    /// <param name="exception">The error.</param>
    public static bool IsLockedByAnotherProcess(IOException exception)
    {
        return (exception.HResult & 0xFFFF) is SharingViolation or LockViolation;
    }
}
