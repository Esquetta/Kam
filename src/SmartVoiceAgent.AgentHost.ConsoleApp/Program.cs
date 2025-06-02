using Microsoft.Extensions.Hosting;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Infrastructure.DependencyInjection;

var builder = Host.CreateDefaultBuilder(args).ConfigureServices((hostContext, services) =>
{
    services.AddApplicationServices();
    services.AddInfrastructureServices();
});

var host = builder.Build();
await host.RunAsync();