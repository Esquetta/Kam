using System.Text.Json;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Saves each agent thread as <c>%AppData%/Kam/sessions/&lt;id&gt;.json</c>.
/// </summary>
public sealed class JsonAgentSessionStore : IAgentSessionStore
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the store in the default location.</summary>
    public JsonAgentSessionStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kam",
            "sessions"))
    {
    }

    /// <summary>Creates the store in a specific folder.</summary>
    /// <param name="directory">Folder that holds one file per session.</param>
    public JsonAgentSessionStore(string directory)
    {
        _directory = directory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessage>> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(sessionId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<SessionDocument>(
                stream,
                AIJsonUtilities.DefaultOptions,
                cancellationToken);
            return document?.Messages ?? [];
        }
        catch (JsonException)
        {
            // A corrupt file starts the thread over rather than breaking the chat.
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var path = PathFor(sessionId);
        var document = new SessionDocument
        {
            Id = sessionId,
            Title = DeriveTitle(messages),
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages = messages.ToList()
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            var temporaryPath = path + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, document, AIJsonUtilities.DefaultOptions, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentSessionSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var summaries = new List<AgentSessionSummary>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var messages = await LoadAsync(id, cancellationToken);
            summaries.Add(new AgentSessionSummary(
                id,
                DeriveTitle(messages),
                File.GetLastWriteTimeUtc(file),
                messages.Count,
                null));
        }

        return summaries.OrderByDescending(summary => summary.UpdatedAt).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathFor(sessionId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private string PathFor(string sessionId)
    {
        var safeId = new string(sessionId
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeId))
        {
            throw new ArgumentException("Session id must contain letters, digits, '-' or '_'.", nameof(sessionId));
        }

        return Path.Combine(_directory, safeId + ".json");
    }

    private static string DeriveTitle(IReadOnlyList<ChatMessage> messages)
    {
        var first = messages.FirstOrDefault(message => message.Role == ChatRole.User)?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(first))
        {
            return "New chat";
        }

        first = first.ReplaceLineEndings(" ");
        return first.Length <= 48 ? first : first[..48].TrimEnd() + "...";
    }

    private sealed class SessionDocument
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public DateTimeOffset UpdatedAt { get; set; }

        public List<ChatMessage> Messages { get; set; } = [];
    }
}
