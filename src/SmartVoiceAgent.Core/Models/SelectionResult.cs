namespace SmartVoiceAgent.Core.Models;
/// <summary>
/// AI seçim sonucu modeli
/// </summary>
public class SelectionResult
{
    public List<int> SelectedIndices { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
}