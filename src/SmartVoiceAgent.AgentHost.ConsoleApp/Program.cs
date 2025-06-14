using Microsoft.Extensions.Hosting;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Infrastructure.DependencyInjection;


var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationServices();
        services.AddInfrastructureServices();
    })
    .Build();

await host.RunAsync();
