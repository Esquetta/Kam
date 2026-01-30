namespace SmartVoiceAgent.Mailing.Entities;

/// <summary>
/// SMTP Authentication Method
/// </summary>
public enum SmtpAuthMethod
{
    /// <summary>
    /// No authentication
    /// </summary>
    None,
    
    /// <summary>
    /// Plain username/password authentication
    /// </summary>
    Plain,
    
    /// <summary>
    /// Username with App Password (for Gmail, Yahoo, etc.)
    /// </summary>
    AppPassword,
    
    /// <summary>
    /// OAuth2 authentication
    /// </summary>
    OAuth2,
    
    /// <summary>
    /// API Key authentication (for SendGrid, Mailgun, etc.)
    /// </summary>
    ApiKey,
    
    /// <summary>
    /// NTLM authentication (for Exchange)
    /// </summary>
    Ntlm,
    
    /// <summary>
    /// Auto-detect based on provider
    /// </summary>
    Auto
}

/// <summary>
/// Predefined SMTP Providers
/// </summary>
public enum SmtpProvider
{
    Custom,
    Gmail,
    Outlook,
    Yahoo,
    Office365,
    SendGrid,
    Mailgun,
    AmazonSES,
    Mailchimp
}

/// <summary>
/// SMTP server configuration settings
/// </summary>
public class SmtpSettings
{
    /// <summary>
    /// Provider type (for auto-configuration)
    /// </summary>
    public SmtpProvider Provider { get; set; } = SmtpProvider.Custom;
    
    /// <summary>
    /// SMTP server host name or IP address
    /// </summary>
    public string Host { get; set; } = "smtp.gmail.com";
    
    /// <summary>
    /// SMTP server port (default: 587 for TLS, 465 for SSL, 25 for non-secure)
    /// </summary>
    public int Port { get; set; } = 587;
    
    /// <summary>
    /// Whether to use SSL/TLS encryption
    /// </summary>
    public bool EnableSsl { get; set; } = true;
    
    /// <summary>
    /// Use StartTLS (recommended over SSL on connect)
    /// </summary>
    public bool UseStartTls { get; set; } = true;
    
    /// <summary>
    /// Authentication method
    /// </summary>
    public SmtpAuthMethod AuthMethod { get; set; } = SmtpAuthMethod.Auto;
    
    /// <summary>
    /// Username for SMTP authentication
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// Password for SMTP authentication (regular password or app password)
    /// </summary>
    public string? Password { get; set; }
    
    /// <summary>
    /// App Password (specific for Gmail, Yahoo, etc.)
    /// </summary>
    public string? AppPassword { get; set; }
    
    /// <summary>
    /// OAuth2 Access Token
    /// </summary>
    public string? OAuth2Token { get; set; }
    
    /// <summary>
    /// OAuth2 Refresh Token (for token renewal)
    /// </summary>
    public string? OAuth2RefreshToken { get; set; }
    
    /// <summary>
    /// OAuth2 Client ID
    /// </summary>
    public string? OAuth2ClientId { get; set; }
    
    /// <summary>
    /// OAuth2 Client Secret
    /// </summary>
    public string? OAuth2ClientSecret { get; set; }
    
    /// <summary>
    /// API Key (for SendGrid, Mailgun, etc.)
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// Default from email address
    /// </summary>
    public string? FromAddress { get; set; }
    
    /// <summary>
    /// Default from display name
    /// </summary>
    public string? FromName { get; set; }
    
    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int Timeout { get; set; } = 30;
    
    /// <summary>
    /// Whether to verify server certificate
    /// </summary>
    public bool VerifyCertificate { get; set; } = true;
    
    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// Delay between retries in seconds
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;
    
    /// <summary>
    /// Whether to skip authentication (for local/internal SMTP)
    /// </summary>
    public bool SkipAuthentication { get; set; }
    
    /// <summary>
    /// Domain for NTLM authentication (Exchange)
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// Apply provider-specific default settings
    /// </summary>
    public void ApplyProviderDefaults()
    {
        switch (Provider)
        {
            case SmtpProvider.Gmail:
                Host = "smtp.gmail.com";
                Port = 587;
                EnableSsl = true;
                UseStartTls = true;
                if (AuthMethod == SmtpAuthMethod.Auto)
                    AuthMethod = SmtpAuthMethod.AppPassword;
                break;
                
            case SmtpProvider.Outlook:
            case SmtpProvider.Office365:
                Host = "smtp.office365.com";
                Port = 587;
                EnableSsl = true;
                UseStartTls = true;
                if (AuthMethod == SmtpAuthMethod.Auto)
                    AuthMethod = SmtpAuthMethod.AppPassword;
                break;
                
            case SmtpProvider.Yahoo:
                Host = "smtp.mail.yahoo.com";
                Port = 587;
                EnableSsl = true;
                UseStartTls = true;
                if (AuthMethod == SmtpAuthMethod.Auto)
                    AuthMethod = SmtpAuthMethod.AppPassword;
                break;
                
            case SmtpProvider.SendGrid:
                Host = "smtp.sendgrid.net";
                Port = 587;
                EnableSsl = true;
                UseStartTls = true;
                AuthMethod = SmtpAuthMethod.ApiKey;
                // SendGrid uses "apikey" as username and API key as password
                break;
                
            case SmtpProvider.Mailgun:
                Host = "smtp.mailgun.org";
                Port = 587;
                EnableSsl = true;
                UseStartTls = true;
                if (AuthMethod == SmtpAuthMethod.Auto)
                    AuthMethod = SmtpAuthMethod.Plain;
                break;
                
            case SmtpProvider.AmazonSES:
                Host = "email-smtp.us-east-1.amazonaws.com"; // Adjust region as needed
                Port = 587;
                EnableSsl = true;
                UseStartTls = true;
                if (AuthMethod == SmtpAuthMethod.Auto)
                    AuthMethod = SmtpAuthMethod.Plain;
                break;
        }
    }

    /// <summary>
    /// Get the effective password (handles App Password vs regular password)
    /// </summary>
    public string? GetEffectivePassword()
    {
        return AuthMethod switch
        {
            SmtpAuthMethod.AppPassword => AppPassword ?? Password,
            SmtpAuthMethod.ApiKey => ApiKey,
            _ => Password
        };
    }

    /// <summary>
    /// Get the effective username (handles API key auth)
    /// </summary>
    public string? GetEffectiveUsername()
    {
        if (AuthMethod == SmtpAuthMethod.ApiKey && Provider == SmtpProvider.SendGrid)
            return "apikey";
            
        return Username;
    }

    #region Factory Methods

    /// <summary>
    /// Create Gmail settings with App Password
    /// </summary>
    public static SmtpSettings Gmail(string email, string appPassword, string? displayName = null)
    {
        return new SmtpSettings
        {
            Provider = SmtpProvider.Gmail,
            Host = "smtp.gmail.com",
            Port = 587,
            EnableSsl = true,
            UseStartTls = true,
            AuthMethod = SmtpAuthMethod.AppPassword,
            Username = email,
            AppPassword = appPassword,
            FromAddress = email,
            FromName = displayName ?? "KAM Assistant"
        };
    }

    /// <summary>
    /// Create Gmail settings with OAuth2
    /// </summary>
    public static SmtpSettings GmailOAuth(string email, string accessToken, string? displayName = null)
    {
        return new SmtpSettings
        {
            Provider = SmtpProvider.Gmail,
            Host = "smtp.gmail.com",
            Port = 587,
            EnableSsl = true,
            UseStartTls = true,
            AuthMethod = SmtpAuthMethod.OAuth2,
            Username = email,
            OAuth2Token = accessToken,
            FromAddress = email,
            FromName = displayName ?? "KAM Assistant"
        };
    }

    /// <summary>
    /// Create Outlook/Office365 settings with App Password
    /// </summary>
    public static SmtpSettings Outlook(string email, string appPassword, string? displayName = null)
    {
        return new SmtpSettings
        {
            Provider = SmtpProvider.Outlook,
            Host = "smtp.office365.com",
            Port = 587,
            EnableSsl = true,
            UseStartTls = true,
            AuthMethod = SmtpAuthMethod.AppPassword,
            Username = email,
            AppPassword = appPassword,
            FromAddress = email,
            FromName = displayName ?? "KAM Assistant"
        };
    }

    /// <summary>
    /// Create Yahoo Mail settings with App Password
    /// </summary>
    public static SmtpSettings Yahoo(string email, string appPassword, string? displayName = null)
    {
        return new SmtpSettings
        {
            Provider = SmtpProvider.Yahoo,
            Host = "smtp.mail.yahoo.com",
            Port = 587,
            EnableSsl = true,
            UseStartTls = true,
            AuthMethod = SmtpAuthMethod.AppPassword,
            Username = email,
            AppPassword = appPassword,
            FromAddress = email,
            FromName = displayName ?? "KAM Assistant"
        };
    }

    /// <summary>
    /// Create SendGrid settings with API Key
    /// </summary>
    public static SmtpSettings SendGrid(string apiKey, string fromEmail, string? fromName = null)
    {
        return new SmtpSettings
        {
            Provider = SmtpProvider.SendGrid,
            Host = "smtp.sendgrid.net",
            Port = 587,
            EnableSsl = true,
            UseStartTls = true,
            AuthMethod = SmtpAuthMethod.ApiKey,
            Username = "apikey",
            ApiKey = apiKey,
            Password = apiKey, // SendGrid uses API key as password
            FromAddress = fromEmail,
            FromName = fromName ?? "KAM Assistant"
        };
    }

    /// <summary>
    /// Create Mailgun settings
    /// </summary>
    public static SmtpSettings Mailgun(string username, string password, string fromEmail, string? fromName = null)
    {
        return new SmtpSettings
        {
            Provider = SmtpProvider.Mailgun,
            Host = "smtp.mailgun.org",
            Port = 587,
            EnableSsl = true,
            UseStartTls = true,
            AuthMethod = SmtpAuthMethod.Plain,
            Username = username,
            Password = password,
            FromAddress = fromEmail,
            FromName = fromName ?? "KAM Assistant"
        };
    }

    /// <summary>
    /// Create Amazon SES settings
    /// </summary>
    public static SmtpSettings AmazonSES(string smtpUsername, string smtpPassword, string fromEmail, string? fromName = null)
    {
        return new SmtpSettings
        {
            Provider = SmtpProvider.AmazonSES,
            Host = "email-smtp.us-east-1.amazonaws.com",
            Port = 587,
            EnableSsl = true,
            UseStartTls = true,
            AuthMethod = SmtpAuthMethod.Plain,
            Username = smtpUsername,
            Password = smtpPassword,
            FromAddress = fromEmail,
            FromName = fromName ?? "KAM Assistant"
        };
    }

    /// <summary>
    /// Create custom SMTP settings
    /// </summary>
    public static SmtpSettings Custom(string host, int port, string username, string password, bool enableSsl = true)
    {
        return new SmtpSettings
        {
            Provider = SmtpProvider.Custom,
            Host = host,
            Port = port,
            EnableSsl = enableSsl,
            UseStartTls = enableSsl,
            AuthMethod = SmtpAuthMethod.Plain,
            Username = username,
            Password = password,
            FromAddress = username,
            FromName = "KAM Assistant"
        };
    }

    #endregion
}

/// <summary>
/// Options for email sending
/// </summary>
public class EmailSendingOptions
{
    /// <summary>
    /// Whether to track email opens
    /// </summary>
    public bool TrackOpens { get; set; } = false;
    
    /// <summary>
    /// Whether to track link clicks
    /// </summary>
    public bool TrackClicks { get; set; } = false;
    
    /// <summary>
    /// Callback URL for tracking events
    /// </summary>
    public string? TrackingCallbackUrl { get; set; }
    
    /// <summary>
    /// Whether to save a copy of sent emails
    /// </summary>
    public bool SaveCopies { get; set; } = false;
    
    /// <summary>
    /// Path to save email copies
    /// </summary>
    public string? SavePath { get; set; }
    
    /// <summary>
    /// Maximum attachment size in MB
    /// </summary>
    public int MaxAttachmentSizeMb { get; set; } = 25;
    
    /// <summary>
    /// Whether to compress large attachments
    /// </summary>
    public bool CompressAttachments { get; set; } = false;
    
    /// <summary>
    /// Default email priority
    /// </summary>
    public EmailPriority DefaultPriority { get; set; } = EmailPriority.Normal;
    
    /// <summary>
    /// Rate limit: emails per minute
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 60;
    
    /// <summary>
    /// Enable sandbox mode (for testing - doesn't actually send emails)
    /// </summary>
    public bool SandboxMode { get; set; } = false;
}
