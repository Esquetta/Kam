namespace SmartVoiceAgent.Core.Models.AI;

public sealed class ModelProviderProfile
{
    public string Id { get; set; } = string.Empty;

    public ModelProviderType Provider { get; set; } = ModelProviderType.OpenRouter;

    public string DisplayName { get; set; } = string.Empty;

    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public List<ModelProviderRole> Roles { get; set; } = [ModelProviderRole.Planner];

    public float Temperature { get; set; } = 0.2f;

    public int MaxTokens { get; set; } = 1200;

    public bool Enabled { get; set; }

    public string MaskedApiKey => MaskSecret(ApiKey);

    public ModelProviderValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Id))
        {
            errors.Add("Profile id is required.");
        }

        if (string.IsNullOrWhiteSpace(Endpoint) || !Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            errors.Add("A valid endpoint is required.");
        }

        if (Enabled && string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add("API key is required for enabled profiles.");
        }

        if (string.IsNullOrWhiteSpace(ModelId))
        {
            errors.Add("Model id is required.");
        }

        if (Roles.Count == 0)
        {
            errors.Add("At least one model role is required.");
        }

        if (MaxTokens <= 0)
        {
            errors.Add("Max tokens must be greater than zero.");
        }

        if (Temperature is < 0 or > 2)
        {
            errors.Add("Temperature must be between 0 and 2.");
        }

        return errors.Count == 0
            ? ModelProviderValidationResult.Success()
            : ModelProviderValidationResult.Failure(errors);
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= 8)
        {
            return new string('*', value.Length);
        }

        return $"{value[..4]}{new string('*', value.Length - 8)}{value[^4..]}";
    }
}
