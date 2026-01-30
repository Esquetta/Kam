using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Mailing.Entities;
using SmartVoiceAgent.Mailing.Interfaces;

namespace SmartVoiceAgent.Mailing.Services;

/// <summary>
/// Factory for creating SMS services based on configuration
/// </summary>
public class SmsServiceFactory : ISmsServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SmsSettings _settings;
    private readonly ILogger<SmsServiceFactory> _logger;
    private readonly Dictionary<SmsProvider, ISmsService> _services = new();

    public SmsServiceFactory(
        IServiceProvider serviceProvider,
        IOptions<SmsSettings> settings,
        ILogger<SmsServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public ISmsService GetService()
    {
        return GetService(_settings.Provider);
    }

    public ISmsService GetService(SmsProvider provider)
    {
        // Return cached instance if available
        if (_services.TryGetValue(provider, out var cachedService))
        {
            return cachedService;
        }

        // Create new instance
        var service = CreateService(provider);
        _services[provider] = service;
        return service;
    }

    public IEnumerable<ISmsService> GetAllServices()
    {
        // Return all configured services
        var providers = new[] { _settings.Provider };
        return providers.Select(GetService);
    }

    private ISmsService CreateService(SmsProvider provider)
    {
        _logger.LogInformation("Creating SMS service for provider: {Provider}", provider);

        switch (provider)
        {
            case SmsProvider.Twilio:
                return CreateTwilioService();
                
            case SmsProvider.Vonage:
                return CreateVonageService();
                
            case SmsProvider.AwsSns:
                return CreateAwsSnsService();
                
            case SmsProvider.MessageBird:
                return CreateMessageBirdService();
                
            case SmsProvider.Plivo:
                return CreatePlivoService();
                
            case SmsProvider.Custom:
                return CreateCustomService();
                
            default:
                throw new NotSupportedException($"SMS provider '{provider}' is not supported");
        }
    }

    private ISmsService CreateTwilioService()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<TwilioSmsService>>();
        var settings = _serviceProvider.GetRequiredService<IOptions<SmsSettings>>();
        var options = _serviceProvider.GetRequiredService<IOptions<SmsSendingOptions>>();
        
        return new TwilioSmsService(settings, options, logger);
    }

    private ISmsService CreateVonageService()
    {
        // For now, throw not implemented - can be added later
        throw new NotImplementedException(
            "Vonage SMS service is not yet implemented. " +
            "Please use Twilio or another supported provider, " +
            "or implement VonageSmsService class.");
    }

    private ISmsService CreateAwsSnsService()
    {
        throw new NotImplementedException(
            "AWS SNS SMS service is not yet implemented. " +
            "Please use Twilio or another supported provider, " +
            "or implement AwsSnsSmsService class.");
    }

    private ISmsService CreateMessageBirdService()
    {
        throw new NotImplementedException(
            "MessageBird SMS service is not yet implemented. " +
            "Please use Twilio or another supported provider, " +
            "or implement MessageBirdSmsService class.");
    }

    private ISmsService CreatePlivoService()
    {
        throw new NotImplementedException(
            "Plivo SMS service is not yet implemented. " +
            "Please use Twilio or another supported provider, " +
            "or implement PlivoSmsService class.");
    }

    private ISmsService CreateCustomService()
    {
        throw new NotImplementedException(
            "Custom SMS service is not yet implemented. " +
            "Please use Twilio or another supported provider, " +
            "or implement your own ISmsService and register it.");
    }
}

/// <summary>
/// Extension to create SMS services with factory pattern
/// </summary>
public static class SmsServiceFactoryExtensions
{
    /// <summary>
    /// Add SMS services with factory pattern
    /// </summary>
    public static IServiceCollection AddSmsServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure settings
        services.Configure<SmsSettings>(configuration.GetSection("Sms"));
        services.Configure<SmsSendingOptions>(options =>
        {
            configuration.GetSection("Sms:Options").Bind(options);
        });

        // Register factory and service
        services.AddSingleton<ISmsServiceFactory, SmsServiceFactory>();
        services.AddScoped<ISmsService>(sp => 
        {
            var factory = sp.GetRequiredService<ISmsServiceFactory>();
            return factory.GetService();
        });

        // Add HttpClient for SMS services
        services.AddHttpClient();

        return services;
    }

    /// <summary>
    /// Add Twilio SMS services
    /// </summary>
    public static IServiceCollection AddTwilioSms(
        this IServiceCollection services,
        string accountSid,
        string authToken,
        string fromNumber,
        Action<SmsSendingOptions>? configureOptions = null)
    {
        services.Configure<SmsSettings>(settings =>
        {
            settings.Provider = SmsProvider.Twilio;
            settings.TwilioAccountSid = accountSid;
            settings.TwilioAuthToken = authToken;
            settings.TwilioPhoneNumber = fromNumber;
            settings.DefaultSender = fromNumber;
        });

        services.Configure<SmsSendingOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        services.AddSingleton<ISmsServiceFactory, SmsServiceFactory>();
        services.AddScoped<ISmsService>(sp =>
        {
            var factory = sp.GetRequiredService<ISmsServiceFactory>();
            return factory.GetService();
        });

        services.AddHttpClient();

        return services;
    }

    /// <summary>
    /// Add SMS services in sandbox mode (for testing)
    /// </summary>
    public static IServiceCollection AddSmsServicesSandbox(
        this IServiceCollection services)
    {
        services.Configure<SmsSettings>(settings =>
        {
            settings.Provider = SmsProvider.Twilio;
            settings.SandboxMode = true;
        });

        services.Configure<SmsSendingOptions>(options =>
        {
            options.SandboxMode = true;
        });

        services.AddSingleton<ISmsServiceFactory, SmsServiceFactory>();
        services.AddScoped<ISmsService>(sp =>
        {
            var factory = sp.GetRequiredService<ISmsServiceFactory>();
            return factory.GetService();
        });

        return services;
    }
}
