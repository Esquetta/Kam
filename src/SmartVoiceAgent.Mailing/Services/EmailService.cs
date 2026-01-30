using System.Net.Security;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using SmartVoiceAgent.Mailing.Entities;
using SmartVoiceAgent.Mailing.Interfaces;

namespace SmartVoiceAgent.Mailing.Services;

/// <summary>
/// Implementation of email service using MailKit
/// </summary>
public class EmailService : IEmailService, IDisposable
{
    private readonly SmtpSettings _settings;
    private readonly EmailSendingOptions _options;
    private readonly IEmailTemplateService? _templateService;
    private readonly ILogger<EmailService> _logger;
    private readonly SmtpClient _smtpClient;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private DateTime _lastSendTime = DateTime.MinValue;
    private int _emailsSentThisMinute = 0;

    public EmailService(
        IOptions<SmtpSettings> settings,
        IOptions<EmailSendingOptions> options,
        ILogger<EmailService> logger,
        IEmailTemplateService? templateService = null)
    {
        _settings = settings.Value;
        _options = options.Value;
        _templateService = templateService;
        _logger = logger;
        _smtpClient = new SmtpClient();
        
        // Apply provider defaults
        _settings.ApplyProviderDefaults();
        
        // Configure certificate validation
        if (!_settings.VerifyCertificate)
        {
            _smtpClient.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
        }
        
        // Log configuration (without sensitive data)
        _logger.LogInformation("📧 EmailService configured: Provider={Provider}, Host={Host}:{Port}, Auth={AuthMethod}, User={Username}",
            _settings.Provider, _settings.Host, _settings.Port, _settings.AuthMethod, _settings.Username);
        
        // Validate essential settings
        if (string.IsNullOrEmpty(_settings.Host))
            throw new InvalidOperationException("SMTP Host is not configured");
        
        if (!_settings.SkipAuthentication && string.IsNullOrEmpty(_settings.GetEffectivePassword()))
        {
            _logger.LogWarning("⚠️ No password/AppPassword configured for SMTP authentication");
        }
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Sandbox mode for testing
            if (_options.SandboxMode)
            {
                _logger.LogInformation("🧪 [SANDBOX] Email would be sent to {Recipients}. Subject: {Subject}",
                    string.Join(", ", message.To), message.Subject);
                return EmailSendResult.SuccessResult(message.Id, "[SANDBOX MODE] Email not actually sent");
            }

            // Validate email addresses
            foreach (var to in message.To)
            {
                var validation = ValidateEmail(to);
                if (!validation.IsValid)
                {
                    return EmailSendResult.FailureResult(message.Id, 
                        $"Invalid recipient email '{to}': {string.Join(", ", validation.Errors)}");
                }
            }

            // Rate limiting
            await EnforceRateLimitAsync(cancellationToken);

            // Create MIME message
            var mimeMessage = CreateMimeMessage(message);

            // Connect and authenticate
            await ConnectAsync(cancellationToken);

            // Send the email
            await _smtpClient.SendAsync(mimeMessage, cancellationToken);
            
            // Update statistics
            _emailsSentThisMinute++;
            message.SentAt = DateTime.UtcNow;
            message.Status = EmailStatus.Sent;

            _logger.LogInformation("✅ Email sent successfully to {Recipients}. Subject: {Subject}",
                string.Join(", ", message.To), message.Subject);

            return EmailSendResult.SuccessResult(message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send email to {Recipients}. Subject: {Subject}",
                string.Join(", ", message.To), message.Subject);
            
            message.Status = EmailStatus.Failed;
            message.ErrorMessage = ex.Message;
            
            return EmailSendResult.FailureResult(message.Id, ex.Message);
        }
    }

    public async Task<EmailSendResult> SendAsync(string to, string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default)
    {
        var message = new EmailMessage
        {
            Subject = subject,
            BodyText = isHtml ? null : body,
            BodyHtml = isHtml ? body : null
        };
        message.AddTo(to);

        return await SendAsync(message, cancellationToken);
    }

    public async Task<EmailSendResult> SendTemplateAsync(string to, string templateName, Dictionary<string, object> data, CancellationToken cancellationToken = default)
    {
        if (_templateService == null)
        {
            throw new InvalidOperationException("Template service not available");
        }

        var rendered = await _templateService.RenderTemplateAsync(templateName, data);
        
        var message = new EmailMessage
        {
            Subject = rendered.Subject,
            BodyText = rendered.BodyText,
            BodyHtml = rendered.BodyHtml,
            TemplateName = templateName,
            TemplateData = data
        };
        message.AddTo(to);

        if (!string.IsNullOrEmpty(rendered.From))
        {
            message.From = rendered.From;
        }
        if (!string.IsNullOrEmpty(rendered.FromName))
        {
            message.FromName = rendered.FromName;
        }

        return await SendAsync(message, cancellationToken);
    }

    public async Task<IEnumerable<EmailSendResult>> SendBulkAsync(List<string> recipients, string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default)
    {
        var results = new List<EmailSendResult>();
        
        foreach (var recipient in recipients)
        {
            var result = await SendAsync(recipient, subject, body, isHtml, cancellationToken);
            results.Add(result);
            
            // Small delay between bulk sends to avoid rate limiting
            await Task.Delay(100, cancellationToken);
        }

        return results;
    }

    public EmailValidationResult ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return EmailValidationResult.Invalid("Email address is empty");
        }

        // Check for common issues
        email = email.Trim();
        
        if (!email.Contains('@'))
        {
            return EmailValidationResult.Invalid("Email must contain @ symbol");
        }

        var parts = email.Split('@');
        if (parts.Length != 2)
        {
            return EmailValidationResult.Invalid("Email contains multiple @ symbols");
        }

        var localPart = parts[0];
        var domain = parts[1];

        if (string.IsNullOrWhiteSpace(localPart))
        {
            return EmailValidationResult.Invalid("Email has empty local part (before @)");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            return EmailValidationResult.Invalid("Email has empty domain (after @)");
        }

        if (!domain.Contains('.'))
        {
            return EmailValidationResult.Invalid("Email domain must contain a period");
        }

        // Try to validate using MimeKit
        try
        {
            var mailbox = new MailboxAddress("", email);
            return EmailValidationResult.Valid(email.ToLowerInvariant());
        }
        catch
        {
            return EmailValidationResult.Invalid("Invalid email format");
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_options.SandboxMode)
        {
            _logger.LogInformation("🧪 [SANDBOX] Connection test would be performed");
            return true;
        }

        try
        {
            await ConnectAsync(cancellationToken);
            
            var isConnected = _smtpClient.IsConnected;
            var isAuthenticated = _settings.SkipAuthentication || _smtpClient.IsAuthenticated;
            
            _logger.LogInformation("SMTP Connection Test: Connected={Connected}, Authenticated={Authenticated}", 
                isConnected, isAuthenticated);
            
            return isConnected && isAuthenticated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP connection test failed");
            return false;
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_smtpClient.IsConnected && _smtpClient.IsAuthenticated)
            return;

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_smtpClient.IsConnected && _smtpClient.IsAuthenticated)
                return;

            _logger.LogDebug("Connecting to SMTP server {Host}:{Port} (Provider: {Provider})", 
                _settings.Host, _settings.Port, _settings.Provider);

            // Determine secure socket options
            SecureSocketOptions secureSocketOptions;
            if (_settings.UseStartTls && _settings.Port == 587)
            {
                secureSocketOptions = SecureSocketOptions.StartTls;
            }
            else if (_settings.EnableSsl && _settings.Port == 465)
            {
                secureSocketOptions = SecureSocketOptions.SslOnConnect;
            }
            else if (_settings.EnableSsl)
            {
                secureSocketOptions = SecureSocketOptions.StartTlsWhenAvailable;
            }
            else
            {
                secureSocketOptions = SecureSocketOptions.None;
            }

            await _smtpClient.ConnectAsync(_settings.Host, _settings.Port, secureSocketOptions, cancellationToken);

            // Authenticate if not skipped
            if (!_settings.SkipAuthentication)
            {
                await AuthenticateAsync(cancellationToken);
            }

            _logger.LogDebug("SMTP connection established (Authenticated: {Authenticated})", 
                _smtpClient.IsAuthenticated);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        var username = _settings.GetEffectiveUsername();
        var password = _settings.GetEffectivePassword();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            if (_settings.AuthMethod != SmtpAuthMethod.None)
            {
                throw new InvalidOperationException(
                    $"Authentication required but username/password not configured. " +
                    $"Provider: {_settings.Provider}, AuthMethod: {_settings.AuthMethod}");
            }
            return;
        }

        switch (_settings.AuthMethod)
        {
            case SmtpAuthMethod.OAuth2:
                if (string.IsNullOrEmpty(_settings.OAuth2Token))
                {
                    throw new InvalidOperationException("OAuth2 token not provided");
                }
                // Note: OAuth2 implementation would require additional handling
                _logger.LogWarning("OAuth2 authentication not fully implemented, falling back to plain auth");
                await _smtpClient.AuthenticateAsync(username, password, cancellationToken);
                break;

            case SmtpAuthMethod.Ntlm:
                var ntlmCredentials = new System.Net.NetworkCredential(username, password, _settings.Domain);
                await _smtpClient.AuthenticateAsync(ntlmCredentials, cancellationToken);
                break;

            case SmtpAuthMethod.ApiKey:
            case SmtpAuthMethod.AppPassword:
            case SmtpAuthMethod.Plain:
            case SmtpAuthMethod.Auto:
            default:
                await _smtpClient.AuthenticateAsync(username, password, cancellationToken);
                break;
        }

        _logger.LogDebug("Authenticated as {Username} using {AuthMethod}", 
            username, _settings.AuthMethod);
    }

    private MimeMessage CreateMimeMessage(EmailMessage message)
    {
        var mimeMessage = new MimeMessage();

        // From
        var fromAddress = !string.IsNullOrEmpty(message.From)
            ? message.From
            : (_settings.FromAddress ?? throw new InvalidOperationException("From address not configured"));
        
        var fromName = message.FromName ?? _settings.FromName ?? fromAddress;
        mimeMessage.From.Add(new MailboxAddress(fromName, fromAddress));

        // To
        foreach (var to in message.To)
        {
            mimeMessage.To.Add(MailboxAddress.Parse(to));
        }

        // CC
        foreach (var cc in message.Cc)
        {
            mimeMessage.Cc.Add(MailboxAddress.Parse(cc));
        }

        // BCC
        foreach (var bcc in message.Bcc)
        {
            mimeMessage.Bcc.Add(MailboxAddress.Parse(bcc));
        }

        // Subject
        mimeMessage.Subject = message.Subject;

        // Priority
        mimeMessage.Priority = message.Priority switch
        {
            EmailPriority.Urgent => MessagePriority.Urgent,
            EmailPriority.High => MessagePriority.Urgent,
            EmailPriority.Low => MessagePriority.NonUrgent,
            _ => MessagePriority.Normal
        };

        // Headers
        foreach (var header in message.Headers)
        {
            mimeMessage.Headers.Add(header.Key, header.Value);
        }

        // Body
        var multipart = new Multipart("mixed");

        // Add body part
        var bodyPart = new MultipartAlternative();
        
        if (!string.IsNullOrEmpty(message.BodyText))
        {
            bodyPart.Add(new TextPart(TextFormat.Plain) { Text = message.BodyText });
        }
        
        if (!string.IsNullOrEmpty(message.BodyHtml))
        {
            bodyPart.Add(new TextPart(TextFormat.Html) { Text = message.BodyHtml });
        }
        
        // If no body specified, use text as default
        if (bodyPart.Count == 0)
        {
            bodyPart.Add(new TextPart(TextFormat.Plain) { Text = "" });
        }

        multipart.Add(bodyPart);

        // Add attachments
        foreach (var attachment in message.Attachments)
        {
            var mimeAttachment = CreateAttachment(attachment);
            multipart.Add(mimeAttachment);
        }

        mimeMessage.Body = multipart;

        return mimeMessage;
    }

    private MimeEntity CreateAttachment(EmailAttachment attachment)
    {
        byte[] data;
        
        if (attachment.Data != null)
        {
            data = attachment.Data;
        }
        else if (!string.IsNullOrEmpty(attachment.FilePath) && File.Exists(attachment.FilePath))
        {
            data = File.ReadAllBytes(attachment.FilePath);
        }
        else
        {
            throw new FileNotFoundException($"Attachment not found: {attachment.FilePath ?? attachment.FileName}");
        }

        var contentType = !string.IsNullOrEmpty(attachment.ContentType)
            ? ContentType.Parse(attachment.ContentType)
            : new ContentType("application", "octet-stream");

        var mimePart = new MimePart(contentType)
        {
            Content = new MimeContent(new MemoryStream(data)),
            ContentDisposition = new ContentDisposition(attachment.IsInline ? ContentDisposition.Inline : ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = attachment.FileName
        };

        if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
        {
            mimePart.ContentId = attachment.ContentId;
        }

        return mimePart;
    }

    private async Task EnforceRateLimitAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        
        // Reset counter if a minute has passed
        if ((now - _lastSendTime).TotalMinutes >= 1)
        {
            _emailsSentThisMinute = 0;
        }

        // Check rate limit
        if (_emailsSentThisMinute >= _options.RateLimitPerMinute)
        {
            var delayMs = (int)((_lastSendTime.AddMinutes(1) - now).TotalMilliseconds);
            if (delayMs > 0)
            {
                _logger.LogWarning("Rate limit reached. Waiting {DelayMs}ms before sending next email", delayMs);
                await Task.Delay(delayMs, cancellationToken);
                _emailsSentThisMinute = 0;
            }
        }

        _lastSendTime = now;
    }

    public void Dispose()
    {
        _sendLock.Dispose();
        
        if (_smtpClient.IsConnected)
        {
            _smtpClient.Disconnect(true);
        }
        
        _smtpClient.Dispose();
    }
}
