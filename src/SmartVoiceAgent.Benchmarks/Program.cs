using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace SmartVoiceAgent.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Smart Voice Agent Benchmarks");
        Console.WriteLine("============================");
        Console.WriteLine();
        Console.WriteLine("Available benchmark categories:");
        Console.WriteLine("  1. All benchmarks");
        Console.WriteLine("  2. Agent & AI (Agent creation, initialization)");
        Console.WriteLine("  3. Infrastructure (Context, Commands, Logging)");
        Console.WriteLine("  4. Services (Music, UI components)");
        Console.WriteLine("  5. Memory & Allocation");
        Console.WriteLine("  6. Serialization");
        Console.WriteLine("  7. Mediator Pipeline");
        Console.WriteLine();

        // Parse command line or interactive selection
        var selection = args.Length > 0 ? args[0] : "1";

        IConfig config = DefaultConfig.Instance
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));

        switch (selection)
        {
            case "1" or "all":
                Console.WriteLine("Running all benchmarks...");
                BenchmarkRunner.Run(typeof(Program).Assembly, config);
                break;

            case "2" or "agent":
                Console.WriteLine("Running Agent benchmarks...");
                BenchmarkRunner.Run<AgentCreationBenchmark>(config);
                break;

            case "3" or "infrastructure":
                Console.WriteLine("Running Infrastructure benchmarks...");
                BenchmarkRunner.Run<ConversationContextBenchmark>(config);
                BenchmarkRunner.Run<CommandInputBenchmark>(config);
                BenchmarkRunner.Run<UiLogBenchmark>(config);
                break;

            case "4" or "services":
                Console.WriteLine("Running Services benchmarks...");
                BenchmarkRunner.Run<MusicServiceBenchmark>(config);
                break;

            case "5" or "memory":
                Console.WriteLine("Running Memory benchmarks...");
                BenchmarkRunner.Run<MemoryAllocationBenchmark>(config);
                break;

            case "6" or "serialization":
                Console.WriteLine("Running Serialization benchmarks...");
                BenchmarkRunner.Run<SerializationBenchmark>(config);
                break;

            case "7" or "mediator":
                Console.WriteLine("Running Mediator Pipeline benchmarks...");
                BenchmarkRunner.Run<MediatorPipelineBenchmark>(config);
                break;

            default:
                Console.WriteLine("Running default benchmark (Mediator Pipeline)...");
                BenchmarkRunner.Run<MediatorPipelineBenchmark>(config);
                break;
        }
    }
}
