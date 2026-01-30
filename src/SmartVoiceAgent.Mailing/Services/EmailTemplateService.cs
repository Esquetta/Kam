using HandlebarsDotNet;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Mailing.Entities;
using SmartVoiceAgent.Mailing.Interfaces;

namespace SmartVoiceAgent.Mailing.Services;

/// <summary>
/// Service for managing and rendering email templates
/// </summary>
public class EmailTemplateService : IEmailTemplateService
{
    private readonly Dictionary<string, EmailTemplate> _templates = new();
    private readonly Dictionary<string, HandlebarsTemplate<object, object>> _compiledTemplates = new();
    private readonly ILogger<EmailTemplateService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public EmailTemplateService(ILogger<EmailTemplateService> logger)
    {
        _logger = logger;
        
        // Register predefined templates
        RegisterPredefinedTemplates();
    }

    private void RegisterPredefinedTemplates()
    {
        RegisterTemplateAsync(EmailTemplates.Welcome).GetAwaiter().GetResult();
        RegisterTemplateAsync(EmailTemplates.Notification).GetAwaiter().GetResult();
        RegisterTemplateAsync(EmailTemplates.PasswordReset).GetAwaiter().GetResult();
        
        _logger.LogInformation("Registered {Count} predefined templates", _templates.Count);
    }

    public Task<EmailTemplate?> GetTemplateAsync(string name)
    {
        _templates.TryGetValue(name.ToLowerInvariant(), out var template);
        return Task.FromResult(template);
    }

    public Task<IEnumerable<EmailTemplate>> GetAllTemplatesAsync()
    {
        return Task.FromResult<IEnumerable<EmailTemplate>>(_templates.Values.ToList());
    }

    public async Task<EmailTemplate> SaveTemplateAsync(EmailTemplate template)
    {
        await _lock.WaitAsync();
        try
        {
            var key = template.Name.ToLowerInvariant();
            
            if (_templates.ContainsKey(key))
            {
                template.Version = _templates[key].Version + 1;
                template.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Updated template '{TemplateName}' to version {Version}", 
                    template.Name, template.Version);
            }
            else
            {
                _logger.LogInformation("Created new template '{TemplateName}'", template.Name);
            }

            _templates[key] = template;
            
            // Pre-compile the template
            CompileTemplate(template);
            
            return template;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<bool> DeleteTemplateAsync(string name)
    {
        var key = name.ToLowerInvariant();
        
        if (_templates.Remove(key))
        {
            _compiledTemplates.Remove(key + "_subject");
            _compiledTemplates.Remove(key + "_text");
            _compiledTemplates.Remove(key + "_html");
            
            _logger.LogInformation("Deleted template '{TemplateName}'", name);
            return Task.FromResult(true);
        }
        
        return Task.FromResult(false);
    }

    public Task<RenderedTemplate> RenderTemplateAsync(string templateName, Dictionary<string, object> data)
    {
        var key = templateName.ToLowerInvariant();
        
        if (!_templates.TryGetValue(key, out var template))
        {
            throw new KeyNotFoundException($"Template '{templateName}' not found");
        }

        // Ensure template is compiled
        CompileTemplate(template);

        var result = new RenderedTemplate
        {
            From = template.DefaultFrom,
            FromName = template.DefaultFromName
        };

        // Render subject
        if (_compiledTemplates.TryGetValue(key + "_subject", out var subjectTemplate))
        {
            result.Subject = subjectTemplate(data);
        }
        else
        {
            result.Subject = template.Subject;
        }

        // Render text body
        if (_compiledTemplates.TryGetValue(key + "_text", out var textTemplate))
        {
            result.BodyText = textTemplate(data);
        }
        else
        {
            result.BodyText = template.BodyText;
        }

        // Render HTML body
        if (_compiledTemplates.TryGetValue(key + "_html", out var htmlTemplate))
        {
            result.BodyHtml = htmlTemplate(data);
        }
        else
        {
            result.BodyHtml = template.BodyHtml;
        }

        // Apply layout if specified
        if (!string.IsNullOrEmpty(template.LayoutName))
        {
            result = ApplyLayout(result, template.LayoutName, data);
        }

        return Task.FromResult(result);
    }

    public Task RegisterTemplateAsync(EmailTemplate template)
    {
        return SaveTemplateAsync(template);
    }

    private void CompileTemplate(EmailTemplate template)
    {
        var key = template.Name.ToLowerInvariant();

        try
        {
            // Compile subject
            if (!string.IsNullOrEmpty(template.Subject))
            {
                var subjectCompiled = Handlebars.Compile(template.Subject);
                _compiledTemplates[key + "_subject"] = subjectCompiled;
            }

            // Compile text body
            if (!string.IsNullOrEmpty(template.BodyText))
            {
                var textCompiled = Handlebars.Compile(template.BodyText);
                _compiledTemplates[key + "_text"] = textCompiled;
            }

            // Compile HTML body
            if (!string.IsNullOrEmpty(template.BodyHtml))
            {
                var htmlCompiled = Handlebars.Compile(template.BodyHtml);
                _compiledTemplates[key + "_html"] = htmlCompiled;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compile template '{TemplateName}'", template.Name);
            throw;
        }
    }

    private RenderedTemplate ApplyLayout(RenderedTemplate content, string layoutName, Dictionary<string, object> data)
    {
        var layoutKey = layoutName.ToLowerInvariant();
        
        if (!_templates.TryGetValue(layoutKey, out var layout))
        {
            _logger.LogWarning("Layout '{LayoutName}' not found", layoutName);
            return content;
        }

        // Compile layout if needed
        CompileTemplate(layout);

        // Create layout data with content
        var layoutData = new Dictionary<string, object>(data)
        {
            ["Subject"] = content.Subject,
            ["BodyText"] = content.BodyText ?? "",
            ["BodyHtml"] = content.BodyHtml ?? ""
        };

        var result = new RenderedTemplate
        {
            Subject = content.Subject,
            From = content.From ?? layout.DefaultFrom,
            FromName = content.FromName ?? layout.DefaultFromName
        };

        // Render layout with content
        if (_compiledTemplates.TryGetValue(layoutKey + "_text", out var layoutText))
        {
            result.BodyText = layoutText(layoutData);
        }

        if (_compiledTemplates.TryGetValue(layoutKey + "_html", out var layoutHtml))
        {
            result.BodyHtml = layoutHtml(layoutData);
        }

        return result;
    }
}
