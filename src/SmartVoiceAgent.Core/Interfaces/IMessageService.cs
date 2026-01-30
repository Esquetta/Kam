namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service for sending messages (email, SMS, etc.)
/// </summary>
public interface IMessageService
{
    /// <summary>
    /// Send a message to the specified recipient
    /// </summary>
    /// <param name="recipient">Recipient address (email, phone number, etc.)</param>
    /// <param name="message">Message content</param>
    /// <param name="subject">Optional subject (for email)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sent successfully</returns>
    Task<bool> SendMessageAsync(string recipient, string message, string? subject = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if this service can handle the given recipient format
    /// </summary>
    /// <param name="recipient">Recipient to check</param>
    /// <returns>True if this service can handle the recipient</returns>
    bool CanHandle(string recipient);
}

/// <summary>
/// Factory for creating the appropriate message service based on recipient
/// </summary>
public interface IMessageServiceFactory
{
    /// <summary>
    /// Get the appropriate message service for the recipient
    /// </summary>
    IMessageService GetService(string recipient);
}
