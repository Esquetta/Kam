# Smart Voice Agent Benchmarks

This project contains performance benchmarks for the Smart Voice Agent application using [BenchmarkDotNet](https://benchmarkdotnet.org/).

## Running Benchmarks

### Quick Start

```bash
# Run all benchmarks
dotnet run --configuration Release

# Run specific benchmark category
dotnet run --configuration Release -- agent
dotnet run --configuration Release -- infrastructure
dotnet run --configuration Release -- memory
```

### Available Categories

| Category | Command | Description |
|----------|---------|-------------|
| All | `all` or `1` | Runs all benchmark suites |
| Agent | `agent` or `2` | AI Agent creation and initialization |
| Infrastructure | `infrastructure` or `3` | Context, Commands, Logging |
| Services | `services` or `4` | Music, UI components |
| Memory | `memory` or `5` | Memory allocation patterns |
| Serialization | `serialization` or `6` | JSON serialization performance |
| Mediator | `mediator` or `7` | MediatR pipeline |

## Benchmark Suites

### 1. AgentCreationBenchmark

Tests AI agent initialization performance:
- `CreateSystemAgent` - System agent creation
- `CreateTaskAgentAsync` - Task agent creation (async)
- `CreateResearchAgent` - Research agent creation
- `CreateCoordinatorAgent` - Coordinator agent creation
- `CreateAllAgents` - Batch creation of all agents

### 2. ConversationContextBenchmark

Tests context management operations:
- `StartConversation` - New conversation creation
- `UpdateContext` - Context update operations
- `GetRelevantContext` - Context retrieval
- `SetApplicationState` - Application state tracking
- `CleanupOldData` - Memory cleanup efficiency

### 3. CommandInputBenchmark

Tests command input service performance:
- `SubmitCommand` - Command submission throughput
- `ReadCommandAsync` - Command reading latency
- `SubmitMultipleCommands` - Batch submission
- `ProducerConsumerPattern` - Thread-safe producer/consumer

### 4. UiLogBenchmark

Tests UI logging performance:
- `LogInfo` - Information level logging
- `LogWarning` - Warning level logging
- `LogError` - Error level logging
- `LogMultipleMessages` - Batch logging
- `LogWithException` - Exception logging

### 5. MusicServiceBenchmark

Tests music service operations:
- `PlayMusicAsync` - Playback start
- `PauseMusicAsync` / `ResumeMusicAsync` - Control operations
- `SetVolumeAsync` - Volume changes
- `FullPlaybackCycle` - Complete playback workflow

### 6. MemoryAllocationBenchmark

Tests memory usage patterns:
- `ConversationContextAllocation` - Context memory overhead
- `CommandSubmissionAllocation` - Command channel overhead
- `LogEntryStringAllocation` - String allocation patterns
- `CleanupEfficiency` - Garbage collection efficiency

### 7. SerializationBenchmark

Tests JSON serialization:
- `SerializePlayMusicCommand` - Command serialization
- `DeserializePlayMusicCommand` - Command deserialization
- `SerializeAgentApplicationResponse` - Response serialization

### 8. MediatorPipelineBenchmark

Tests MediatR pipeline with behaviors:
- `Send_PlayMusicCommand` - Full pipeline execution

## Understanding Results

BenchmarkDotNet produces detailed statistics:

```
|           Method |     Mean |   Error |  StdDev |   Gen0 |   Gen1 | Allocated |
|----------------- |---------:|--------:|--------:|-------:|-------:|----------:|
| SubmitCommand    | 125.4 ns | 2.51 ns | 3.12 ns | 0.0124 |      - |     104 B |
```

- **Mean**: Average execution time
- **Error**: 99.9% confidence interval half-width
- **StdDev**: Standard deviation
- **Gen0/Gen1**: Garbage collections per 1000 operations
- **Allocated**: Memory allocated per operation

## Best Practices

1. **Always run in Release mode**: `dotnet run --configuration Release`
2. **Close other applications**: Minimize background interference
3. **Run multiple times**: Results may vary; look for consistent patterns
4. **Compare versions**: Use results to validate performance improvements

## Adding New Benchmarks

```csharp
[MemoryDiagnoser]
public class MyBenchmark
{
    [GlobalSetup]
    public void Setup() { /* initialization */ }

    [Benchmark]
    public void MyOperation() { /* code to benchmark */ }

    [GlobalCleanup]
    public void Cleanup() { /* cleanup */ }
}
```

Add to `Program.cs` to include in the benchmark suite.
