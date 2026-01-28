using BenchmarkDotNet.Attributes;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Dtos;
using System.Text.Json;

namespace SmartVoiceAgent.Benchmarks;

/// <summary>
/// Benchmarks for JSON serialization performance
/// </summary>
[MemoryDiagnoser]
public class SerializationBenchmark
{
    private JsonSerializerOptions _options = null!;
    private PlayMusicCommand _musicCommand = null!;
    private AgentApplicationResponse _appResponse = null!;
    private string _musicCommandJson = null!;
    private string _appResponseJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _musicCommand = new PlayMusicCommand("Metallica - Enter Sandman");
        _appResponse = new AgentApplicationResponse(
            Success: true,
            Message: "Application opened successfully",
            ApplicationName: "Spotify",
            ExecutablePath: "C:\\Program Files\\Spotify\\Spotify.exe",
            IsInstalled: true,
            IsRunning: true
        );

        _musicCommandJson = JsonSerializer.Serialize(_musicCommand, _options);
        _appResponseJson = JsonSerializer.Serialize(_appResponse, _options);
    }

    [Benchmark]
    public string SerializePlayMusicCommand()
    {
        return JsonSerializer.Serialize(_musicCommand, _options);
    }

    [Benchmark]
    public string SerializeAgentApplicationResponse()
    {
        return JsonSerializer.Serialize(_appResponse, _options);
    }

    [Benchmark]
    public PlayMusicCommand? DeserializePlayMusicCommand()
    {
        return JsonSerializer.Deserialize<PlayMusicCommand>(_musicCommandJson, _options);
    }

    [Benchmark]
    public AgentApplicationResponse? DeserializeAgentApplicationResponse()
    {
        return JsonSerializer.Deserialize<AgentApplicationResponse>(_appResponseJson, _options);
    }

    [Benchmark]
    public void SerializeMultipleCommands()
    {
        for (int i = 0; i < 100; i++)
        {
            var cmd = new PlayMusicCommand($"Song {i}");
            _ = JsonSerializer.Serialize(cmd, _options);
        }
    }

    [Benchmark]
    public void DeserializeMultipleCommands()
    {
        for (int i = 0; i < 100; i++)
        {
            _ = JsonSerializer.Deserialize<PlayMusicCommand>(_musicCommandJson, _options);
        }
    }
}
