using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Mailing.Interfaces;
using SmartVoiceAgent.Ui;

namespace SmartVoiceAgent.Tests.Ui;

public class AppServiceScopeTests
{
    [Fact]
    public void CreateApplicationServiceScope_ResolvesUiScopedSkillServicesWithScopeValidation()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            })
            .ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AIService:Provider"] = "OpenAI",
                    ["AIService:Endpoint"] = "https://api.openai.com/v1",
                    ["AIService:ApiKey"] = "test-key",
                    ["AIService:ModelId"] = "gpt-4o-mini",
                    ["Email:Provider"] = "Gmail",
                    ["Email:Username"] = "user@example.com",
                    ["Email:AppPassword"] = "app-password",
                    ["Email:FromAddress"] = "user@example.com",
                    ["Sms:Provider"] = "Twilio",
                    ["Sms:TwilioAccountSid"] = "AC123",
                    ["Sms:TwilioAuthToken"] = "twilio-token",
                    ["Sms:TwilioPhoneNumber"] = "+15551234567"
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddApplicationServices();
                services.AddInfrastructureServices(context.Configuration);
                services.AddSmartVoiceAgent(context.Configuration);
            })
            .Build();

        using var scope = App.CreateApplicationServiceScope(host.Services);

        scope.ServiceProvider.GetRequiredService<ISkillHealthService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ISkillEvalHarness>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ISkillTestService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ISkillExecutionPipeline>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IEmailService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ISmsService>().Should().NotBeNull();
    }
}
