namespace SmartVoiceAgent.Mailing.Entities;

/// <summary>
/// Represents an email message to be sent
/// </summary>
public class EmailMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Sender email address
    /// </summary>
    public string From { get; set; } = string.Empty;
    
    /// <summary>
    /// Sender display name
    /// </summary>
    public string? FromName { get; set; }
    
    /// <summary>
    /// Recipient email addresses (To)
    /// </summary>
    public List<string> To { get; set; } = new();
    
    /// <summary>
    /// CC recipients
    /// </summary>
    public List<string> Cc { get; set; } = new();
    
    /// <summary>
    /// BCC recipients
    /// </summary>
    public List<string> Bcc { get; set; } = new();
    
    /// <summary>
    /// Email subject
    /// </summary>
    public string Subject { get; set; } = string.Empty;
    
    /// <summary>
    /// Plain text body
    /// </summary>
    public string? BodyText { get; set; }
    
    /// <summary>
    /// HTML body
    /// </summary>
    public string? BodyHtml { get; set; }
    
    /// <summary>
    /// Email priority
    /// </summary>
    public EmailPriority Priority { get; set; } = EmailPriority.Normal;
    
    /// <summary>
    /// List of attachments
    /// </summary>
    public List<EmailAttachment> Attachments { get; set; } = new();
    
    /// <summary>
    /// Custom headers
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();
    
    /// <summary>
    /// Template name if using a template
    /// </summary>
    public string? TemplateName { get; set; }
    
    /// <summary>
    /// Template data for substitution
    /// </summary>
    public Dictionary<string, object>? TemplateData { get; set; }
    
    /// <summary>
    /// When the email was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the email was sent
    /// </summary>
    public DateTime? SentAt { get; set; }
    
    /// <summary>
    /// Send status
    /// </summary>
    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    
    /// <summary>
    /// Error message if sending failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; }
    
    /// <summary>
    /// Tags for categorization
    /// </summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>
    /// User ID who initiated the send (for audit)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Add a recipient to the To list
    /// </summary>
    public EmailMessage AddTo(string email, string? name = null)
    {
        To.Add(string.IsNullOrEmpty(name) ? email : $"{name} <{email}>");
        return this;
    }

    /// <summary>
    /// Add a CC recipient
    /// </summary>
    public EmailMessage AddCc(string email, string? name = null)
    {
        Cc.Add(string.IsNullOrEmpty(name) ? email : $"{name} <{email}>");
        return this;
    }

    /// <summary>
    /// Add a BCC recipient
    /// </summary>
    public EmailMessage AddBcc(string email, string? name = null)
    {
        Bcc.Add(string.IsNullOrEmpty(name) ? email : $"{name} <{email}>");
        return this;
    }

    /// <summary>
    /// Add an attachment
    /// </summary>
    public EmailMessage AddAttachment(string filePath, string? contentType = null)
    {
        Attachments.Add(new EmailAttachment
        {
            FilePath = filePath,
            ContentType = contentType,
            FileName = Path.GetFileName(filePath)
        });
        return this;
    }

    /// <summary>
    /// Add an attachment from bytes
    /// </summary>
    public EmailMessage AddAttachment(byte[] data, string fileName, string contentType)
    {
        Attachments.Add(new EmailAttachment
        {
            Data = data,
            FileName = fileName,
            ContentType = contentType
        });
        return this;
    }

    /// <summary>
    /// Set the email body (both text and HTML)
    /// </summary>
    public EmailMessage SetBody(string text, string? html = null)
    {
        BodyText = text;
        BodyHtml = html;
        return this;
    }

    /// <summary>
    /// Use a template for this email
    /// </summary>
    public EmailMessage UseTemplate(string templateName, Dictionary<string, object> data)
    {
        TemplateName = templateName;
        TemplateData = data;
        return this;
    }

    /// <summary>
    /// Add a tag for categorization
    /// </summary>
    public EmailMessage AddTag(string tag)
    {
        Tags.Add(tag);
        return this;
    }
}

/// <summary>
/// Email priority levels
/// </summary>
public enum EmailPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

/// <summary>
/// Email sending status
/// </summary>
public enum EmailStatus
{
    Pending,
    Queued,
    Sending,
    Sent,
    Delivered,
    Failed,
    Cancelled,
    Bounced
}
