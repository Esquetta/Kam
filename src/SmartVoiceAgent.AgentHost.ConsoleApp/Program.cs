using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartVoiceAgent.Application.Behaviors;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using System.Reflection;


var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationServices();
        services.AddInfrastructureServices();
    })
    .Build();

await host.RunAsync();
