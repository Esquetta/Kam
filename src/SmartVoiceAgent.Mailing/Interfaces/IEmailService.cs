using SmartVoiceAgent.Mailing.Entities;

namespace SmartVoiceAgent.Mailing.Interfaces;

/// <summary>
/// Service for sending and managing emails
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Send an email message
    /// </summary>
    /// <param name="message">Email message to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Send result with status and message ID</returns>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send a simple email
    /// </summary>
    /// <param name="to">Recipient email</param>
    /// <param name="subject">Email subject</param>
    /// <param name="body">Email body</param>
    /// <param name="isHtml">Whether body is HTML</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Send result</returns>
    Task<EmailSendResult> SendAsync(string to, string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send email using a template
    /// </summary>
    /// <param name="to">Recipient email</param>
    /// <param name="templateName">Template name</param>
    /// <param name="data">Template data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Send result</returns>
    Task<EmailSendResult> SendTemplateAsync(string to, string templateName, Dictionary<string, object> data, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send email to multiple recipients
    /// </summary>
    /// <param name="recipients">List of recipients</param>
    /// <param name="subject">Email subject</param>
    /// <param name="body">Email body</param>
    /// <param name="isHtml">Whether body is HTML</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Send results for each recipient</returns>
    Task<IEnumerable<EmailSendResult>> SendBulkAsync(List<string> recipients, string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate an email address format
    /// </summary>
    /// <param name="email">Email address to validate</param>
    /// <returns>Validation result</returns>
    EmailValidationResult ValidateEmail(string email);
    
    /// <summary>
    /// Check if SMTP server is reachable
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connected successfully</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for managing email templates
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Get a template by name
    /// </summary>
    /// <param name="name">Template name</param>
    /// <returns>Template or null if not found</returns>
    Task<EmailTemplate?> GetTemplateAsync(string name);
    
    /// <summary>
    /// Get all templates
    /// </summary>
    /// <returns>List of templates</returns>
    Task<IEnumerable<EmailTemplate>> GetAllTemplatesAsync();
    
    /// <summary>
    /// Create or update a template
    /// </summary>
    /// <param name="template">Template to save</param>
    /// <returns>Saved template</returns>
    Task<EmailTemplate> SaveTemplateAsync(EmailTemplate template);
    
    /// <summary>
    /// Delete a template
    /// </summary>
    /// <param name="name">Template name</param>
    /// <returns>True if deleted</returns>
    Task<bool> DeleteTemplateAsync(string name);
    
    /// <summary>
    /// Render a template with data
    /// </summary>
    /// <param name="templateName">Template name</param>
    /// <param name="data">Template data</param>
    /// <returns>Rendered content</returns>
    Task<RenderedTemplate> RenderTemplateAsync(string templateName, Dictionary<string, object> data);
    
    /// <summary>
    /// Register a predefined template
    /// </summary>
    /// <param name="template">Template to register</param>
    Task RegisterTemplateAsync(EmailTemplate template);
}

/// <summary>
/// Service for email queue management
/// </summary>
public interface IEmailQueueService
{
    /// <summary>
    /// Add an email to the queue
    /// </summary>
    /// <param name="message">Email message</param>
    /// <returns>Queue item ID</returns>
    Task<Guid> EnqueueAsync(EmailMessage message);
    
    /// <summary>
    /// Process queued emails
    /// </summary>
    /// <param name="batchSize">Number of emails to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of emails processed</returns>
    Task<int> ProcessQueueAsync(int batchSize = 10, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get queue status
    /// </summary>
    /// <returns>Queue statistics</returns>
    Task<QueueStatus> GetQueueStatusAsync();
    
    /// <summary>
    /// Retry a failed email
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <returns>True if queued for retry</returns>
    Task<bool> RetryAsync(Guid messageId);
    
    /// <summary>
    /// Cancel a queued email
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <returns>True if cancelled</returns>
    Task<bool> CancelAsync(Guid messageId);
}

/// <summary>
/// Email send result
/// </summary>
public class EmailSendResult
{
    /// <summary>
    /// Whether the send was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Message ID
    /// </summary>
    public Guid MessageId { get; set; }
    
    /// <summary>
    /// Status message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Error details if failed
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// When the email was sent
    /// </summary>
    public DateTime? SentAt { get; set; }
    
    /// <summary>
    /// Create a success result
    /// </summary>
    public static EmailSendResult SuccessResult(Guid messageId, string message = "Email sent successfully")
    {
        return new EmailSendResult
        {
            Success = true,
            MessageId = messageId,
            Message = message,
            SentAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Create a failure result
    /// </summary>
    public static EmailSendResult FailureResult(Guid messageId, string error)
    {
        return new EmailSendResult
        {
            Success = false,
            MessageId = messageId,
            Message = "Failed to send email",
            Error = error
        };
    }
}

/// <summary>
/// Email validation result
/// </summary>
public class EmailValidationResult
{
    /// <summary>
    /// Whether the email is valid
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Normalized email address
    /// </summary>
    public string? NormalizedEmail { get; set; }
    
    /// <summary>
    /// Validation errors
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// Whether the domain has MX records
    /// </summary>
    public bool HasMxRecord { get; set; }
    
    /// <summary>
    /// Create a valid result
    /// </summary>
    public static EmailValidationResult Valid(string normalizedEmail)
    {
        return new EmailValidationResult
        {
            IsValid = true,
            NormalizedEmail = normalizedEmail
        };
    }
    
    /// <summary>
    /// Create an invalid result
    /// </summary>
    public static EmailValidationResult Invalid(params string[] errors)
    {
        return new EmailValidationResult
        {
            IsValid = false,
            Errors = errors.ToList()
        };
    }
}

/// <summary>
/// Rendered template result
/// </summary>
public class RenderedTemplate
{
    /// <summary>
    /// Rendered subject
    /// </summary>
    public string Subject { get; set; } = string.Empty;
    
    /// <summary>
    /// Rendered plain text body
    /// </summary>
    public string? BodyText { get; set; }
    
    /// <summary>
    /// Rendered HTML body
    /// </summary>
    public string? BodyHtml { get; set; }
    
    /// <summary>
    /// From address
    /// </summary>
    public string? From { get; set; }
    
    /// <summary>
    /// From name
    /// </summary>
    public string? FromName { get; set; }
}

/// <summary>
/// Queue status information
/// </summary>
public class QueueStatus
{
    /// <summary>
    /// Total messages in queue
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// Pending messages
    /// </summary>
    public int PendingCount { get; set; }
    
    /// <summary>
    /// Failed messages
    /// </summary>
    public int FailedCount { get; set; }
    
    /// <summary>
    /// Processing messages
    /// </summary>
    public int ProcessingCount { get; set; }
    
    /// <summary>
    /// Average processing time
    /// </summary>
    public TimeSpan AverageProcessingTime { get; set; }
    
    /// <summary>
    /// Last processed time
    /// </summary>
    public DateTime? LastProcessedAt { get; set; }
}
