using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Mailing.Entities;
using SmartVoiceAgent.Mailing.Interfaces;
using SmartVoiceAgent.Mailing.Services;

namespace SmartVoiceAgent.Mailing.Extensions;

/// <summary>
/// Extension methods for registering mailing services
/// </summary>
public static class ServiceCollectionExtensions
{
    #region Configuration-based Registration
    
    /// <summary>
    /// Add mailing services to the DI container with configuration
    /// </summary>
    public static IServiceCollection AddMailingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure SMTP settings from configuration
        services.Configure<SmtpSettings>(settings =>
        {
            configuration.GetSection("Email").Bind(settings);
            settings.ApplyProviderDefaults();
        });
        
        services.Configure<EmailSendingOptions>(options =>
        {
            configuration.GetSection("Email:Options").Bind(options);
        });

        // Register services
        services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
        services.AddScoped<IEmailService, EmailService>();

        return services;
    }

    /// <summary>
    /// Add mailing services with custom configuration
    /// </summary>
    public static IServiceCollection AddMailingServices(
        this IServiceCollection services,
        Action<SmtpSettings> configureSettings,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        services.Configure<SmtpSettings>(settings =>
        {
            configureSettings(settings);
            settings.ApplyProviderDefaults();
        });
        
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<EmailSendingOptions>(_ => { });
        }

        // Register services
        services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
        services.AddScoped<IEmailService, EmailService>();

        return services;
    }
    
    #endregion

    #region Provider-specific Registration

    /// <summary>
    /// Add Gmail services with App Password
    /// </summary>
    /// <remarks>
    /// To get an App Password:
    /// 1. Enable 2-Factor Authentication on your Google account
    /// 2. Go to Google Account → Security → App Passwords
    /// 3. Generate a new app password for "Mail"
    /// </remarks>
    public static IServiceCollection AddGmailServices(
        this IServiceCollection services,
        string email,
        string appPassword,
        string? displayName = null,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.Gmail;
                settings.Host = "smtp.gmail.com";
                settings.Port = 587;
                settings.EnableSsl = true;
                settings.UseStartTls = true;
                settings.AuthMethod = SmtpAuthMethod.AppPassword;
                settings.Username = email;
                settings.AppPassword = appPassword;
                settings.FromAddress = email;
                settings.FromName = displayName ?? "KAM Assistant";
            },
            configureOptions);
    }

    /// <summary>
    /// Add Gmail services with OAuth2
    /// </summary>
    public static IServiceCollection AddGmailOAuthServices(
        this IServiceCollection services,
        string email,
        string accessToken,
        string? displayName = null,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.Gmail;
                settings.Host = "smtp.gmail.com";
                settings.Port = 587;
                settings.EnableSsl = true;
                settings.UseStartTls = true;
                settings.AuthMethod = SmtpAuthMethod.OAuth2;
                settings.Username = email;
                settings.OAuth2Token = accessToken;
                settings.FromAddress = email;
                settings.FromName = displayName ?? "KAM Assistant";
            },
            configureOptions);
    }

    /// <summary>
    /// Add Outlook/Hotmail/Live services with App Password
    /// </summary>
    /// <remarks>
    /// To get an App Password for Outlook:
    /// 1. Go to Microsoft Account → Security
    /// 2. Enable Two-step verification
    /// 3. Go to App passwords → Create a new app password
    /// </remarks>
    public static IServiceCollection AddOutlookServices(
        this IServiceCollection services,
        string email,
        string appPassword,
        string? displayName = null,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.Outlook;
                settings.Host = "smtp.office365.com";
                settings.Port = 587;
                settings.EnableSsl = true;
                settings.UseStartTls = true;
                settings.AuthMethod = SmtpAuthMethod.AppPassword;
                settings.Username = email;
                settings.AppPassword = appPassword;
                settings.FromAddress = email;
                settings.FromName = displayName ?? "KAM Assistant";
            },
            configureOptions);
    }

    /// <summary>
    /// Add Office 365 services with App Password
    /// </summary>
    public static IServiceCollection AddOffice365Services(
        this IServiceCollection services,
        string email,
        string appPassword,
        string? displayName = null,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        // Office 365 uses the same settings as Outlook
        return services.AddOutlookServices(email, appPassword, displayName, configureOptions);
    }

    /// <summary>
    /// Add Yahoo Mail services with App Password
    /// </summary>
    /// <remarks>
    /// To get an App Password for Yahoo:
    /// 1. Go to Yahoo Account → Account Security
    /// 2. Enable Two-step verification
    /// 3. Generate app password for "Other app"
    /// </remarks>
    public static IServiceCollection AddYahooServices(
        this IServiceCollection services,
        string email,
        string appPassword,
        string? displayName = null,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.Yahoo;
                settings.Host = "smtp.mail.yahoo.com";
                settings.Port = 587;
                settings.EnableSsl = true;
                settings.UseStartTls = true;
                settings.AuthMethod = SmtpAuthMethod.AppPassword;
                settings.Username = email;
                settings.AppPassword = appPassword;
                settings.FromAddress = email;
                settings.FromName = displayName ?? "KAM Assistant";
            },
            configureOptions);
    }

    /// <summary>
    /// Add SendGrid services with API Key
    /// </summary>
    /// <remarks>
    /// Get your API Key from: https://app.sendgrid.com/settings/api_keys
    /// </remarks>
    public static IServiceCollection AddSendGridServices(
        this IServiceCollection services,
        string apiKey,
        string fromEmail,
        string? fromName = null,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.SendGrid;
                settings.Host = "smtp.sendgrid.net";
                settings.Port = 587;
                settings.EnableSsl = true;
                settings.UseStartTls = true;
                settings.AuthMethod = SmtpAuthMethod.ApiKey;
                settings.Username = "apikey";
                settings.ApiKey = apiKey;
                settings.Password = apiKey; // SendGrid uses API key as password
                settings.FromAddress = fromEmail;
                settings.FromName = fromName ?? "KAM Assistant";
            },
            configureOptions);
    }

    /// <summary>
    /// Add Mailgun services
    /// </summary>
    /// <remarks>
    /// Get your SMTP credentials from: https://app.mailgun.com/app/account/security/api_keys
    /// </remarks>
    public static IServiceCollection AddMailgunServices(
        this IServiceCollection services,
        string smtpUsername,
        string smtpPassword,
        string fromEmail,
        string? fromName = null,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.Mailgun;
                settings.Host = "smtp.mailgun.org";
                settings.Port = 587;
                settings.EnableSsl = true;
                settings.UseStartTls = true;
                settings.AuthMethod = SmtpAuthMethod.Plain;
                settings.Username = smtpUsername;
                settings.Password = smtpPassword;
                settings.FromAddress = fromEmail;
                settings.FromName = fromName ?? "KAM Assistant";
            },
            configureOptions);
    }

    /// <summary>
    /// Add Amazon SES services
    /// </summary>
    /// <remarks>
    /// Get your SMTP credentials from AWS SES Console → SMTP Settings
    /// </remarks>
    public static IServiceCollection AddAmazonSESServices(
        this IServiceCollection services,
        string smtpUsername,
        string smtpPassword,
        string fromEmail,
        string? fromName = null,
        string region = "us-east-1",
        Action<EmailSendingOptions>? configureOptions = null)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.AmazonSES;
                settings.Host = $"email-smtp.{region}.amazonaws.com";
                settings.Port = 587;
                settings.EnableSsl = true;
                settings.UseStartTls = true;
                settings.AuthMethod = SmtpAuthMethod.Plain;
                settings.Username = smtpUsername;
                settings.Password = smtpPassword;
                settings.FromAddress = fromEmail;
                settings.FromName = fromName ?? "KAM Assistant";
            },
            configureOptions);
    }

    /// <summary>
    /// Add custom SMTP settings
    /// </summary>
    public static IServiceCollection AddCustomSmtpServices(
        this IServiceCollection services,
        string host,
        int port,
        string username,
        string password,
        string fromEmail,
        string? fromName = null,
        bool enableSsl = true,
        Action<EmailSendingOptions>? configureOptions = null)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.Custom;
                settings.Host = host;
                settings.Port = port;
                settings.EnableSsl = enableSsl;
                settings.UseStartTls = enableSsl;
                settings.AuthMethod = SmtpAuthMethod.Plain;
                settings.Username = username;
                settings.Password = password;
                settings.FromAddress = fromEmail;
                settings.FromName = fromName ?? "KAM Assistant";
            },
            configureOptions);
    }

    #endregion

    #region Sandbox Mode

    /// <summary>
    /// Add mailing services in sandbox mode (for testing - doesn't send actual emails)
    /// </summary>
    public static IServiceCollection AddMailingServicesSandbox(
        this IServiceCollection services)
    {
        return services.AddMailingServices(
            settings =>
            {
                settings.Provider = SmtpProvider.Custom;
                settings.Host = "localhost";
                settings.Port = 25;
                settings.SkipAuthentication = true;
            },
            options =>
            {
                options.SandboxMode = true;
            });
    }

    #endregion
}
