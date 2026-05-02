using System;
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
    string Message,
    ModelProviderType Provider,
    string ModelId,
    string FailureCategory,
    DateTimeOffset TestedAt)
{
    public bool Succeeded => Success;

    public static ModelConnectionTestResult Passed(int liveModelCount)
    {
        return new ModelConnectionTestResult(
            true,
            liveModelCount,
            $"{liveModelCount} live models returned.",
            ModelProviderType.OpenAICompatible,
            string.Empty,
            string.Empty,
            DateTimeOffset.UtcNow);
    }

    public static ModelConnectionTestResult Passed(
        ModelProviderType provider,
        string modelId,
        int liveModelCount,
        DateTimeOffset testedAt)
    {
        return new ModelConnectionTestResult(
            true,
            liveModelCount,
            $"{liveModelCount} live models returned.",
            provider,
            modelId,
            string.Empty,
            testedAt);
    }

    public static ModelConnectionTestResult Failed(string message)
    {
        return new ModelConnectionTestResult(
            false,
            0,
            message,
            ModelProviderType.OpenAICompatible,
            string.Empty,
            "Connection",
            DateTimeOffset.UtcNow);
    }

    public static ModelConnectionTestResult Failed(
        ModelProviderType provider,
        string modelId,
        string message,
        string failureCategory,
        DateTimeOffset testedAt)
    {
        return new ModelConnectionTestResult(
            false,
            0,
            message,
            provider,
            modelId,
            string.IsNullOrWhiteSpace(failureCategory) ? "Connection" : failureCategory,
            testedAt);
    }
}
