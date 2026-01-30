namespace SmartVoiceAgent.Mailing.Entities;

/// <summary>
/// Represents an email template
/// </summary>
public class EmailTemplate
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Template name (unique identifier)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Template description
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Email subject template
    /// </summary>
    public string Subject { get; set; } = string.Empty;
    
    /// <summary>
    /// Plain text body template
    /// </summary>
    public string? BodyText { get; set; }
    
    /// <summary>
    /// HTML body template
    /// </summary>
    public string? BodyHtml { get; set; }
    
    /// <summary>
    /// Default from address
    /// </summary>
    public string? DefaultFrom { get; set; }
    
    /// <summary>
    /// Default from name
    /// </summary>
    public string? DefaultFromName { get; set; }
    
    /// <summary>
    /// Template category
    /// </summary>
    public string? Category { get; set; }
    
    /// <summary>
    /// Whether this template is active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When the template was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the template was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// Version number for template tracking
    /// </summary>
    public int Version { get; set; } = 1;
    
    /// <summary>
    /// Sample data for testing the template
    /// </summary>
    public Dictionary<string, object>? SampleData { get; set; }
    
    /// <summary>
    /// Layout template name (for nested templates)
    /// </summary>
    public string? LayoutName { get; set; }

    /// <summary>
    /// Create a simple text template
    /// </summary>
    public static EmailTemplate CreateText(string name, string subject, string bodyText)
    {
        return new EmailTemplate
        {
            Name = name,
            Subject = subject,
            BodyText = bodyText
        };
    }

    /// <summary>
    /// Create an HTML template
    /// </summary>
    public static EmailTemplate CreateHtml(string name, string subject, string bodyHtml, string? bodyText = null)
    {
        return new EmailTemplate
        {
            Name = name,
            Subject = subject,
            BodyHtml = bodyHtml,
            BodyText = bodyText
        };
    }

    /// <summary>
    /// Create a template with both text and HTML
    /// </summary>
    public static EmailTemplate Create(string name, string subject, string bodyText, string bodyHtml)
    {
        return new EmailTemplate
        {
            Name = name,
            Subject = subject,
            BodyText = bodyText,
            BodyHtml = bodyHtml
        };
    }
}

/// <summary>
/// Predefined email templates
/// </summary>
public static class EmailTemplates
{
    /// <summary>
    /// Welcome email template
    /// </summary>
    public static EmailTemplate Welcome => new()
    {
        Name = "welcome",
        Subject = "Welcome to {{AppName}}, {{UserName}}!",
        BodyText = @"Hi {{UserName}},

Welcome to {{AppName}}! We're excited to have you on board.

Get started by exploring our features:
{{#each Features}}
- {{this}}
{{/each}}

If you have any questions, feel free to reach out.

Best regards,
The {{AppName}} Team",
        BodyHtml = @"<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; line-height: 1.6; }
        .container { max-width: 600px; margin: 0 auto; padding: 20px; }
        .header { background-color: #4CAF50; color: white; padding: 20px; text-align: center; }
        .content { padding: 20px; background-color: #f9f9f9; }
        .footer { padding: 20px; text-align: center; color: #666; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Welcome to {{AppName}}!</h1>
        </div>
        <div class='content'>
            <p>Hi {{UserName}},</p>
            <p>Welcome to <strong>{{AppName}}</strong>! We're excited to have you on board.</p>
            <h3>Get started by exploring our features:</h3>
            <ul>
                {{#each Features}}
                <li>{{this}}</li>
                {{/each}}
            </ul>
            <p>If you have any questions, feel free to reach out.</p>
        </div>
        <div class='footer'>
            <p>Best regards,<br>The {{AppName}} Team</p>
        </div>
    </div>
</body>
</html>"
    };

    /// <summary>
    /// Notification email template
    /// </summary>
    public static EmailTemplate Notification => new()
    {
        Name = "notification",
        Subject = "{{Title}}",
        BodyText = @"Hi {{UserName}},

{{Message}}

{{#if ActionUrl}}
Take action: {{ActionUrl}}
{{/if}}

{{#if Footer}}
{{Footer}}
{{/if}}",
        BodyHtml = @"<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; line-height: 1.6; }
        .container { max-width: 600px; margin: 0 auto; padding: 20px; }
        .notification { background-color: #2196F3; color: white; padding: 15px; border-radius: 5px; }
        .content { padding: 20px; background-color: #f9f9f9; margin-top: 20px; }
        .button { display: inline-block; padding: 10px 20px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px; margin-top: 15px; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='notification'>
            <h2>{{Title}}</h2>
        </div>
        <div class='content'>
            <p>Hi {{UserName}},</p>
            <p>{{Message}}</p>
            {{#if ActionUrl}}
            <a href='{{ActionUrl}}' class='button'>Take Action</a>
            {{/if}}
            {{#if Footer}}
            <p style='margin-top: 30px; color: #666; font-size: 12px;'>{{Footer}}</p>
            {{/if}}
        </div>
    </div>
</body>
</html>"
    };

    /// <summary>
    /// Password reset template
    /// </summary>
    public static EmailTemplate PasswordReset => new()
    {
        Name = "password-reset",
        Subject = "Password Reset Request",
        BodyText = @"Hi {{UserName}},

We received a request to reset your password for {{AppName}}.

Click the link below to reset your password:
{{ResetUrl}}

This link will expire in {{ExpiryHours}} hours.

If you didn't request this, please ignore this email.

Best regards,
The {{AppName}} Team",
        BodyHtml = @"<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; line-height: 1.6; }
        .container { max-width: 600px; margin: 0 auto; padding: 20px; }
        .content { padding: 20px; background-color: #f9f9f9; }
        .button { display: inline-block; padding: 12px 24px; background-color: #f44336; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }
        .warning { color: #666; font-size: 12px; margin-top: 30px; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='content'>
            <h2>Password Reset Request</h2>
            <p>Hi {{UserName}},</p>
            <p>We received a request to reset your password for <strong>{{AppName}}</strong>.</p>
            <p>Click the button below to reset your password:</p>
            <a href='{{ResetUrl}}' class='button'>Reset Password</a>
            <p class='warning'>This link will expire in {{ExpiryHours}} hours.<br>
            If you didn't request this, please ignore this email.</p>
        </div>
    </div>
</body>
</html>"
    };
}
