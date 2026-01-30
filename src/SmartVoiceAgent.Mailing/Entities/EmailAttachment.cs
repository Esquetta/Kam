namespace SmartVoiceAgent.Mailing.Entities;

/// <summary>
/// Represents an email attachment
/// </summary>
public class EmailAttachment
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// File name to display in email
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// MIME content type
    /// </summary>
    public string? ContentType { get; set; }
    
    /// <summary>
    /// File path (if loaded from disk)
    /// </summary>
    public string? FilePath { get; set; }
    
    /// <summary>
    /// File data (if loaded in memory)
    /// </summary>
    public byte[]? Data { get; set; }
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size => Data?.Length ?? 0;
    
    /// <summary>
    /// Content ID for inline attachments
    /// </summary>
    public string? ContentId { get; set; }
    
    /// <summary>
    /// Whether this is an inline attachment (for HTML images)
    /// </summary>
    public bool IsInline { get; set; }
    
    /// <summary>
    /// Creates an attachment from a file path
    /// </summary>
    public static EmailAttachment FromFile(string filePath, string? displayName = null)
    {
        return new EmailAttachment
        {
            FilePath = filePath,
            FileName = displayName ?? Path.GetFileName(filePath),
            ContentType = GetMimeType(filePath)
        };
    }
    
    /// <summary>
    /// Creates an attachment from byte array
    /// </summary>
    public static EmailAttachment FromBytes(byte[] data, string fileName, string? contentType = null)
    {
        return new EmailAttachment
        {
            Data = data,
            FileName = fileName,
            ContentType = contentType ?? GetMimeType(fileName)
        };
    }
    
    /// <summary>
    /// Creates an inline attachment for HTML emails
    /// </summary>
    public static EmailAttachment Inline(byte[] data, string contentId, string contentType)
    {
        return new EmailAttachment
        {
            Data = data,
            FileName = contentId,
            ContentId = contentId,
            ContentType = contentType,
            IsInline = true
        };
    }
    
    /// <summary>
    /// Get MIME type from file extension
    /// </summary>
    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            _ => "application/octet-stream"
        };
    }
}
