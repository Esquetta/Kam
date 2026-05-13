using System.Text.Json;
using System.Text.Json.Serialization;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Session;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class JsonApplicationSessionContextStore : IApplicationSessionContextStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public JsonApplicationSessionContextStore(string? directory = null)
    {
        var root = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartVoiceAgent");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "session-context.json");
    }

    public ApplicationSessionContext Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new ApplicationSessionContext();
            }

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<ApplicationSessionContext>(json) ?? new ApplicationSessionContext();
            }
            catch (JsonException)
            {
                return new ApplicationSessionContext();
            }
            catch (IOException)
            {
                return new ApplicationSessionContext();
            }
        }
    }

    public void Save(ApplicationSessionContext context)
    {
        lock (_lock)
        {
            context.LastUpdatedAt = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(context, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            });
            File.WriteAllText(_path, json);
        }
    }
}
