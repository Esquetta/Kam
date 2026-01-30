using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.Message
{
    /// <summary>
    /// Email message service implementation using SMTP
    /// </summary>
    public class EmailMessageService : IMessageService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailMessageService>? _logger;
        private readonly SmtpClient _smtpClient;

        public EmailMessageService(IConfiguration configuration, ILogger<EmailMessageService>? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
            
            // Configure SMTP client from settings
            var smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            var enableSsl = bool.Parse(_configuration["Email:EnableSsl"] ?? "true");
            
            _smtpClient = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            
            // Set credentials if provided
            var username = _configuration["Email:Username"];
            var password = _configuration["Email:Password"];
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                _smtpClient.Credentials = new NetworkCredential(username, password);
            }
        }

        public bool CanHandle(string recipient)
        {
            // Check if recipient is a valid email address
            if (string.IsNullOrWhiteSpace(recipient))
                return false;
                
            try
            {
                var addr = new MailAddress(recipient);
                return addr.Address == recipient;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SendMessageAsync(string recipient, string message, string? subject = null, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger?.LogInformation("📧 Sending email to: {Recipient}", recipient);
                
                var fromAddress = _configuration["Email:FromAddress"] ?? "kam@neuralcore.ai";
                var fromName = _configuration["Email:FromName"] ?? "KAM Neural Core";
                
                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromAddress, fromName),
                    Subject = subject ?? "Message from KAM",
                    Body = message,
                    IsBodyHtml = false
                };
                
                mailMessage.To.Add(recipient);
                
                await _smtpClient.SendMailAsync(mailMessage, cancellationToken);
                
                _logger?.LogInformation("✅ Email sent successfully to: {Recipient}", recipient);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Failed to send email to: {Recipient}", recipient);
                return false;
            }
        }
    }
}
