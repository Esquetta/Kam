namespace SmartVoiceAgent.Core.Models.AI;

public sealed record ModelProviderValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ModelProviderValidationResult Success() => new(true, Array.Empty<string>());

    public static ModelProviderValidationResult Failure(IEnumerable<string> errors) => new(false, errors.ToArray());
}
