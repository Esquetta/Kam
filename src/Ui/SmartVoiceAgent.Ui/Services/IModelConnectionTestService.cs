using System.Threading;
using System.Threading.Tasks;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public interface IModelConnectionTestService
{
    Task<ModelConnectionTestResult> TestAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed record ModelConnectionTestResult(
    bool Success,
    int LiveModelCount,
    string Message)
{
    public static ModelConnectionTestResult Passed(int liveModelCount)
    {
        return new ModelConnectionTestResult(
            true,
            liveModelCount,
            $"{liveModelCount} live models returned.");
    }

    public static ModelConnectionTestResult Failed(string message)
    {
        return new ModelConnectionTestResult(false, 0, message);
    }
}
