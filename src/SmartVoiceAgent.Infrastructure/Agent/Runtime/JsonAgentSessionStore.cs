using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Saves each agent thread as <c>%AppData%/Kam/sessions/&lt;id&gt;.json</c>.
/// The title, model and message count are written before the messages, so listing threads
/// reads only the start of each file.
/// </summary>
public sealed class JsonAgentSessionStore : IAgentSessionStore
{
    /// <summary>The longest title a user can give a thread.</summary>
    public const int MaxTitleLength = 120;

    private const int DerivedTitleLength = 48;
    private const int HeaderBufferBytes = 8 * 1024;

    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Title and model per session, read from file headers or set before the first save. Guarded by _gate.
    private readonly Dictionary<string, SessionMetadata> _metadata = new(StringComparer.Ordinal);

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
            return (await ReadDocumentAsync(path, cancellationToken))?.Messages ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var id = SafeId(sessionId);
        var path = PathFor(sessionId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var metadata = ReadMetadata(id, path);
            await WriteDocumentAsync(path, CreateDocument(id, metadata, messages.ToList(), DateTimeOffset.UtcNow), cancellationToken);
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = Path.GetFileNameWithoutExtension(file);
                summaries.Add(await ReadSummaryAsync(id, file, cancellationToken));
            }
        }
        finally
        {
            _gate.Release();
        }

        return summaries.OrderByDescending(summary => summary.UpdatedAt).ToList();
    }

    /// <inheritdoc />
    public async Task<AgentSessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var id = SafeId(sessionId);
        var path = PathFor(sessionId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                return await ReadSummaryAsync(id, path, cancellationToken);
            }

            return _metadata.TryGetValue(id, out var pending) && !pending.IsEmpty
                ? new AgentSessionSummary(
                    id,
                    pending.CustomTitle ?? "New chat",
                    DateTimeOffset.UtcNow,
                    0,
                    null,
                    pending.ModelId,
                    pending.CustomTitle is not null)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task RenameAsync(string sessionId, string? title, CancellationToken cancellationToken = default)
    {
        var customTitle = NormalizeTitle(title);
        return UpdateMetadataAsync(sessionId, metadata => metadata with { CustomTitle = customTitle }, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetModelAsync(string sessionId, string? modelId, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();
        return UpdateMetadataAsync(sessionId, metadata => metadata with { ModelId = model }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var id = SafeId(sessionId);
        var path = PathFor(sessionId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _metadata.Remove(id);
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

    private async Task UpdateMetadataAsync(
        string sessionId,
        Func<SessionMetadata, SessionMetadata> update,
        CancellationToken cancellationToken)
    {
        var id = SafeId(sessionId);
        var path = PathFor(sessionId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var metadata = update(ReadMetadata(id, path));
            _metadata[id] = metadata;

            // A thread with no messages yet is written by its first save, which picks this up.
            var document = await ReadDocumentAsync(path, cancellationToken);
            if (document is not null)
            {
                await WriteDocumentAsync(path, CreateDocument(id, metadata, document.Messages, document.UpdatedAt), cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private SessionMetadata ReadMetadata(string id, string path)
    {
        if (_metadata.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var header = File.Exists(path) ? ReadHeader(path) : null;
        var metadata = new SessionMetadata(header?.CustomTitle, header?.ModelId);
        _metadata[id] = metadata;
        return metadata;
    }

    private async Task<AgentSessionSummary> ReadSummaryAsync(string id, string path, CancellationToken cancellationToken)
    {
        var header = ReadHeader(path);
        if (header is null || header.MessageCount is null)
        {
            // Files written before the header held a message count, or cut short, are read in full once.
            var document = await ReadDocumentAsync(path, cancellationToken);
            var messages = document?.Messages ?? [];
            header = new SessionHeader(
                document?.CustomTitle,
                document?.ModelId,
                document?.UpdatedAt,
                messages.Count,
                DeriveTitle(messages));
        }

        _metadata.TryAdd(id, new SessionMetadata(header.CustomTitle, header.ModelId));
        var updatedAt = header.UpdatedAt is { } stamp && stamp != default
            ? stamp
            : new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

        return new AgentSessionSummary(
            id,
            header.CustomTitle ?? header.Title ?? "New chat",
            updatedAt,
            header.MessageCount ?? 0,
            null,
            header.ModelId,
            header.CustomTitle is not null);
    }

    private static async Task<SessionDocument?> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<SessionDocument>(
                stream,
                AIJsonUtilities.DefaultOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            // A corrupt file starts the thread over rather than breaking the chat.
            return null;
        }
    }

    private async Task WriteDocumentAsync(string path, SessionDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, AIJsonUtilities.DefaultOptions, cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static SessionDocument CreateDocument(
        string id,
        SessionMetadata metadata,
        List<ChatMessage> messages,
        DateTimeOffset updatedAt)
    {
        return new SessionDocument
        {
            Id = id,
            Title = metadata.CustomTitle ?? DeriveTitle(messages),
            CustomTitle = metadata.CustomTitle,
            ModelId = metadata.ModelId,
            UpdatedAt = updatedAt,
            MessageCount = messages.Count,
            Messages = messages
        };
    }

    /// <summary>
    /// Reads the properties written before <c>messages</c>, or null when the start of the file is not valid JSON.
    /// </summary>
    private static SessionHeader? ReadHeader(string path)
    {
        byte[] buffer;
        int length;
        try
        {
            using var stream = File.OpenRead(path);
            buffer = new byte[HeaderBufferBytes];
            length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }
        catch (IOException)
        {
            return null;
        }

        var span = buffer.AsSpan(0, length);
        if (span.StartsWith(Encoding.UTF8.Preamble))
        {
            span = span[Encoding.UTF8.Preamble.Length..];
        }

        var reader = new Utf8JsonReader(span, isFinalBlock: length < buffer.Length, state: default);
        string? title = null;
        string? customTitle = null;
        string? modelId = null;
        DateTimeOffset? updatedAt = null;
        int? messageCount = null;

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return null;
                }

                var name = reader.GetString();
                if (string.Equals(name, "messages", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (!reader.Read())
                {
                    return null;
                }

                switch (name?.ToLowerInvariant())
                {
                    case "title":
                        title = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        break;
                    case "customtitle":
                        customTitle = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        break;
                    case "modelid":
                        modelId = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        break;
                    case "updatedat":
                        updatedAt = reader.TokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out var stamp) ? stamp : null;
                        break;
                    case "messagecount":
                        messageCount = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var count) ? count : null;
                        break;
                    default:
                        if (!reader.TrySkip())
                        {
                            return null;
                        }

                        break;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return new SessionHeader(customTitle, modelId, updatedAt, messageCount, title);
    }

    private string PathFor(string sessionId)
    {
        return Path.Combine(_directory, SafeId(sessionId) + ".json");
    }

    private static string SafeId(string sessionId)
    {
        var safeId = new string(sessionId
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeId))
        {
            throw new ArgumentException("Session id must contain letters, digits, '-' or '_'.", nameof(sessionId));
        }

        return safeId;
    }

    private static string? NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalized = title.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= MaxTitleLength ? normalized : normalized[..MaxTitleLength].TrimEnd();
    }

    private static string DeriveTitle(IReadOnlyList<ChatMessage> messages)
    {
        var first = messages.FirstOrDefault(message => message.Role == ChatRole.User)?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(first))
        {
            return "New chat";
        }

        first = first.ReplaceLineEndings(" ");
        return first.Length <= DerivedTitleLength ? first : first[..DerivedTitleLength].TrimEnd() + "...";
    }

    private sealed record SessionMetadata(string? CustomTitle, string? ModelId)
    {
        public bool IsEmpty => CustomTitle is null && ModelId is null;
    }

    private sealed record SessionHeader(
        string? CustomTitle,
        string? ModelId,
        DateTimeOffset? UpdatedAt,
        int? MessageCount,
        string? Title);

    // Property order matters: everything before Messages is read when threads are listed.
    private sealed class SessionDocument
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string? CustomTitle { get; set; }

        public string? ModelId { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public int MessageCount { get; set; }

        public List<ChatMessage> Messages { get; set; } = [];
    }
}
