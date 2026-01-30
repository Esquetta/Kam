using AgentFrameworkToolkit.Tools;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Application.Notifications;
using SmartVoiceAgent.Mailing.Entities;
using SmartVoiceAgent.Mailing.Interfaces;
using System.ComponentModel;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools;

/// <summary>
/// Communication tools for agents - Email and SMS functionality
/// </summary>
public class CommunicationAgentTools
{
    private readonly IEmailService? _emailService;
    private readonly ISmsService? _smsService;
    private readonly IMediator? _mediator;
    private readonly ILogger<CommunicationAgentTools>? _logger;

    public CommunicationAgentTools(
        IServiceProvider serviceProvider,
        IMediator? mediator = null,
        ILogger<CommunicationAgentTools>? logger = null)
    {
        // Try to resolve mailing services - they might not be configured
        _emailService = serviceProvider.GetService<IEmailService>();
        _smsService = serviceProvider.GetService<ISmsService>();
        _mediator = mediator;
        _logger = logger;

        if (_emailService == null)
        {
            _logger?.LogWarning("📧 IEmailService not registered. Email functionality will be unavailable.");
        }
        if (_smsService == null)
        {
            _logger?.LogWarning("📱 ISmsService not registered. SMS functionality will be unavailable.");
        }
    }

    #region Email Tools

    /// <summary>
    /// Send an email to a recipient
    /// </summary>
    [AITool("send_email_async", "Sends an email to a specified recipient. Use when user wants to send an email message.")]
    public async Task<string> SendEmailAsync(
        [Description("Recipient email address")]
        string to,
        [Description("Email subject")]
        string subject,
        [Description("Email body content (can be plain text or HTML)")]
        string body,
        [Description("Whether body is HTML format (default: false)")]
        bool isHtml = false)
    {
        if (_emailService == null)
        {
            return "❌ Email service is not configured. Please configure SMTP settings in Integrations tab.";
        }

        try
        {
            _logger?.LogInformation("📧 Agent sending email to: {To}, Subject: {Subject}", to, subject);

            var result = await _emailService.SendAsync(to, subject, body, isHtml);

            if (result.Success)
            {
                // Publish notification if mediator is available
                if (_mediator != null)
                {
                    await _mediator.Publish(new MessageSentNotification(to, body));
                }

                _logger?.LogInformation("✅ Email sent successfully to {To}", to);
                return $"✅ Email sent successfully to {to}. Subject: {subject}";
            }
            else
            {
                _logger?.LogWarning("❌ Failed to send email to {To}: {Error}", to, result.Error);
                return $"❌ Failed to send email: {result.Message}. Error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Exception sending email to {To}", to);
            return $"❌ Error sending email: {ex.Message}";
        }
    }

    /// <summary>
    /// Send an email using a template
    /// </summary>
    [AITool("send_email_template_async", "Sends a templated email. Available templates: welcome, notification, password-reset.")]
    public async Task<string> SendEmailTemplateAsync(
        [Description("Recipient email address")]
        string to,
        [Description("Template name (welcome, notification, password-reset)")]
        string templateName,
        [Description("Template data as JSON object (e.g., {\"UserName\": \"John\", \"AppName\": \"KAM\"})")]
        string templateDataJson)
    {
        if (_emailService == null)
        {
            return "❌ Email service is not configured. Please configure SMTP settings in Integrations tab.";
        }

        try
        {
            _logger?.LogInformation("📧 Agent sending templated email to: {To}, Template: {Template}", to, templateName);

            // Parse template data
            var templateData = ParseTemplateData(templateDataJson);
            
            var result = await _emailService.SendTemplateAsync(to, templateName, templateData);

            if (result.Success)
            {
                _logger?.LogInformation("✅ Templated email sent successfully to {To}", to);
                return $"✅ Templated email ({templateName}) sent successfully to {to}";
            }
            else
            {
                _logger?.LogWarning("❌ Failed to send templated email to {To}: {Error}", to, result.Error);
                return $"❌ Failed to send email: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Exception sending templated email to {To}", to);
            return $"❌ Error sending email: {ex.Message}";
        }
    }

    /// <summary>
    /// Validate an email address format
    /// </summary>
    [AITool("validate_email_async", "Validates an email address format.")]
    public string ValidateEmail(
        [Description("Email address to validate")]
        string email)
    {
        if (_emailService == null)
        {
            // Basic validation without service
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return $"❌ '{email}' appears to be invalid.";
            }
            return $"✅ '{email}' appears to be valid (basic check). Configure Email service for full validation.";
        }

        var result = _emailService.ValidateEmail(email);
        
        if (result.IsValid)
        {
            return $"✅ '{email}' is a valid email address.";
        }
        else
        {
            var errors = string.Join(", ", result.Errors);
            return $"❌ '{email}' is invalid: {errors}";
        }
    }

    #endregion

    #region SMS Tools

    /// <summary>
    /// Send an SMS message
    /// </summary>
    [AITool("send_sms_async", "Sends an SMS message to a phone number. Phone number must include country code (e.g., +90 for Turkey, +1 for USA).")]
    public async Task<string> SendSmsAsync(
        [Description("Recipient phone number with country code (e.g., +905551234567, +14155552671)")]
        string to,
        [Description("SMS message body")]
        string message)
    {
        if (_smsService == null)
        {
            return "❌ SMS service is not configured. Please configure Twilio settings in Integrations tab.";
        }

        try
        {
            _logger?.LogInformation("📱 Agent sending SMS to: {To}", to);

            var result = await _smsService.SendAsync(to, message);

            if (result.Success)
            {
                _logger?.LogInformation("✅ SMS sent successfully to {To}", to);
                return $"✅ SMS sent successfully to {to}. Segments: {result.Segments}";
            }
            else
            {
                _logger?.LogWarning("❌ Failed to send SMS to {To}: {Error}", to, result.Error);
                return $"❌ Failed to send SMS: {result.Message}. Error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Exception sending SMS to {To}", to);
            return $"❌ Error sending SMS: {ex.Message}";
        }
    }

    /// <summary>
    /// Validate a phone number
    /// </summary>
    [AITool("validate_phone_async", "Validates a phone number format (E.164).")]
    public string ValidatePhoneNumber(
        [Description("Phone number to validate (with country code)")]
        string phoneNumber)
    {
        if (_smsService == null)
        {
            // Use the validation helper directly
            var result = SmsValidationHelper.ValidatePhoneNumber(phoneNumber);
            
            if (result.IsValid)
            {
                return $"✅ '{phoneNumber}' is a valid phone number.";
            }
            else
            {
                var errors = string.Join(", ", result.Errors);
                return $"❌ '{phoneNumber}' is invalid: {errors}";
            }
        }

        var validation = _smsService.ValidatePhoneNumber(phoneNumber);
        
        if (validation.IsValid)
        {
            return $"✅ '{phoneNumber}' is a valid phone number.";
        }
        else
        {
            var errors = string.Join(", ", validation.Errors);
            return $"❌ '{phoneNumber}' is invalid: {errors}";
        }
    }

    /// <summary>
    /// Check SMS service connection status
    /// </summary>
    [AITool("check_sms_connection_async", "Checks if SMS service is connected and working.")]
    public async Task<string> CheckSmsConnectionAsync()
    {
        if (_smsService == null)
        {
            return "❌ SMS service is not configured.";
        }

        try
        {
            var isConnected = await _smsService.TestConnectionAsync();
            
            if (isConnected)
            {
                return $"✅ SMS service ({_smsService.ProviderName}) is connected and working.";
            }
            else
            {
                return $"❌ SMS service ({_smsService.ProviderName}) connection failed.";
            }
        }
        catch (Exception ex)
        {
            return $"❌ Error checking SMS connection: {ex.Message}";
        }
    }

    #endregion

    #region Helper Methods

    private Dictionary<string, object> ParseTemplateData(string json)
    {
        try
        {
            // Simple JSON parsing - in production, use System.Text.Json
            var result = new Dictionary<string, object>();
            
            if (string.IsNullOrWhiteSpace(json))
                return result;

            // Remove outer braces
            json = json.Trim();
            if (json.StartsWith("{") && json.EndsWith("}"))
            {
                json = json.Substring(1, json.Length - 2);
            }

            // Split by comma (simple parsing, doesn't handle nested objects)
            var pairs = json.Split(',');
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split(':');
                if (keyValue.Length == 2)
                {
                    var key = keyValue[0].Trim().Trim('"');
                    var value = keyValue[1].Trim().Trim('"');
                    result[key] = value;
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    #endregion
}
