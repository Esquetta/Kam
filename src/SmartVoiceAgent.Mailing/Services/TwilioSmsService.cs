using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Mailing.Entities;
using SmartVoiceAgent.Mailing.Interfaces;

namespace SmartVoiceAgent.Mailing.Services;

/// <summary>
/// Twilio SMS service implementation
/// </summary>
public class TwilioSmsService : SmsServiceBase
{
    private readonly HttpClient _httpClient;
    private readonly string _authHeader;
    private const string TwilioApiBase = "https://api.twilio.com/2010-04-01";

    public TwilioSmsService(
        IOptions<SmsSettings> settings,
        IOptions<SmsSendingOptions> options,
        ILogger<TwilioSmsService> logger,
        HttpClient? httpClient = null) 
        : base(settings, options, logger)
    {
        _httpClient = httpClient ?? new HttpClient();
        
        // Validate settings
        if (string.IsNullOrEmpty(_settings.TwilioAccountSid))
            throw new InvalidOperationException("Twilio Account SID is not configured");
        
        if (string.IsNullOrEmpty(_settings.TwilioAuthToken))
            throw new InvalidOperationException("Twilio Auth Token is not configured");
        
        if (string.IsNullOrEmpty(_settings.TwilioPhoneNumber))
            throw new InvalidOperationException("Twilio Phone Number is not configured");
        
        // Create basic auth header
        var authString = $"{_settings.TwilioAccountSid}:{_settings.TwilioAuthToken}";
        _authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
        
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _authHeader);
    }

    public override string ProviderName => "Twilio";

    public override SmsFeatures SupportedFeatures => new()
    {
        SupportsDeliveryReports = true,
        SupportsUnicode = true,
        SupportsBulkSending = true,
        SupportsFlashMessages = false,
        SupportsScheduledMessages = false,
        SupportsTwoWay = true,
        SupportsAlphanumericSender = false,
        SupportsBalanceCheck = true,
        MaxMessageLength = 1600,
        SupportsTemplates = false
    };

    protected override async Task<SmsSendResult> SendInternalAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        var url = $"{TwilioApiBase}/Accounts/{_settings.TwilioAccountSid}/Messages.json";
        
        var from = message.From ?? _settings.TwilioPhoneNumber;
        
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = message.To,
            ["From"] = from,
            ["Body"] = message.Body
        });
        
        // Add status callback for delivery reports
        if (_settings.EnableDeliveryReports && !string.IsNullOrEmpty(_settings.WebhookUrl))
        {
            // Note: Would need to append a unique identifier to track this specific message
            // content.Add(new KeyValuePair<string, string>("StatusCallback", _settings.WebhookUrl));
        }
        
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            var result = JsonSerializer.Deserialize<TwilioMessageResponse>(responseBody);
            
            if (result != null)
            {
                // Calculate cost if available
                decimal? cost = null;
                if (!string.IsNullOrEmpty(result.Price) && decimal.TryParse(result.Price, out var price))
                {
                    cost = Math.Abs(price); // Price is negative for outbound
                }
                
                return new SmsSendResult
                {
                    Success = true,
                    MessageId = message.Id,
                    ProviderMessageId = result.Sid,
                    ProviderName = ProviderName,
                    Segments = result.NumSegments ?? 1,
                    Cost = cost,
                    Currency = result.PriceUnit,
                    Message = $"SMS sent successfully via Twilio. Status: {result.Status}",
                    SentAt = DateTime.UtcNow
                };
            }
        }
        
        // Handle error
        var error = JsonSerializer.Deserialize<TwilioErrorResponse>(responseBody);
        return SmsSendResult.CreateFailure(
            message.Id, 
            error?.Message ?? $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
            error?.Code?.ToString());
    }

    protected override async Task<bool> TestConnectionInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Try to fetch account info as a connection test
            var url = $"{TwilioApiBase}/Accounts/{_settings.TwilioAccountSid}.json";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var account = JsonSerializer.Deserialize<TwilioAccountResponse>(body);
                
                _logger.LogInformation("Connected to Twilio account: {AccountSid}, Status: {Status}",
                    account?.Sid, account?.Status);
                
                return true;
            }
            
            _logger.LogWarning("Twilio connection test failed: {Status} - {Reason}",
                (int)response.StatusCode, response.ReasonPhrase);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio connection test failed");
            return false;
        }
    }

    public override async Task<BalanceInfo?> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{TwilioApiBase}/Accounts/{_settings.TwilioAccountSid}.json";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var account = JsonSerializer.Deserialize<TwilioAccountResponse>(body);
                
                if (account?.SubresourceUris?.Balance != null)
                {
                    // Fetch balance
                    var balanceUrl = $"https://api.twilio.com{account.SubresourceUris.Balance}";
                    var balanceResponse = await _httpClient.GetAsync(balanceUrl, cancellationToken);
                    
                    if (balanceResponse.IsSuccessStatusCode)
                    {
                        var balanceBody = await balanceResponse.Content.ReadAsStringAsync(cancellationToken);
                        var balance = JsonSerializer.Deserialize<TwilioBalanceResponse>(balanceBody);
                        
                        return new BalanceInfo
                        {
                            Balance = balance?.Balance ?? 0,
                            Currency = balance?.Currency ?? "USD"
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Twilio balance");
        }
        
        return null;
    }

    public override async Task<DeliveryStatus> GetDeliveryStatusAsync(Guid messageId, string? providerMessageId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(providerMessageId))
        {
            return DeliveryStatus.Unknown;
        }
        
        try
        {
            var url = $"{TwilioApiBase}/Accounts/{_settings.TwilioAccountSid}/Messages/{providerMessageId}.json";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var message = JsonSerializer.Deserialize<TwilioMessageResponse>(body);
                
                return message?.Status?.ToLower() switch
                {
                    "delivered" => DeliveryStatus.Delivered,
                    "sent" or "queued" or "sending" => DeliveryStatus.Pending,
                    "failed" => DeliveryStatus.Failed,
                    "undelivered" => DeliveryStatus.Failed,
                    _ => DeliveryStatus.Unknown
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get delivery status for {MessageId}", providerMessageId);
        }
        
        return DeliveryStatus.Unknown;
    }

    #region Twilio API Response Models

    private class TwilioMessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }
        
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        
        [JsonPropertyName("num_segments")]
        public int? NumSegments { get; set; }
        
        [JsonPropertyName("price")]
        public string? Price { get; set; }
        
        [JsonPropertyName("price_unit")]
        public string? PriceUnit { get; set; }
        
        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; set; }
        
        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }

    private class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
        
        [JsonPropertyName("message")]
        public string? Message { get; set; }
        
        [JsonPropertyName("more_info")]
        public string? MoreInfo { get; set; }
        
        [JsonPropertyName("status")]
        public int? Status { get; set; }
    }

    private class TwilioAccountResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }
        
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        
        [JsonPropertyName("subresource_uris")]
        public SubresourceUris? SubresourceUris { get; set; }
    }

    private class SubresourceUris
    {
        [JsonPropertyName("balance")]
        public string? Balance { get; set; }
    }

    private class TwilioBalanceResponse
    {
        [JsonPropertyName("balance")]
        public decimal Balance { get; set; }
        
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }
    }

    #endregion
}
