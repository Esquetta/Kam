using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class WebPageSkillExecutor : ISkillExecutor
{
    private const int DefaultTimeoutMilliseconds = 10000;
    private const int MaxTimeoutMilliseconds = 15000;
    private const int DefaultMaxLength = 6000;
    private const int MaxLength = 30000;

    private readonly HttpClient _httpClient;

    public WebPageSkillExecutor(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals("web.fetch", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("web.read_page", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(plan.SkillId))
        {
            return SkillResult.Failed($"Unsupported web page skill: {plan.SkillId}");
        }

        var url = SkillPlanArgumentReader.GetString(plan, "url");
        var validation = ValidateUrl(
            url,
            SkillPlanArgumentReader.GetBool(plan, "allowPrivateNetwork"));
        if (validation is not null)
        {
            return SkillResult.Failed(
                validation,
                SkillExecutionStatus.ValidationFailed,
                "invalid_url");
        }

        var timeoutMilliseconds = Math.Clamp(
            SkillPlanArgumentReader.GetInt(plan, "timeoutMilliseconds", DefaultTimeoutMilliseconds),
            1000,
            MaxTimeoutMilliseconds);
        var maxLength = Math.Clamp(
            SkillPlanArgumentReader.GetInt(plan, "maxLength", DefaultMaxLength),
            1,
            MaxLength);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("KamSkillRuntime/1.0");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return SkillResult.Failed(
                    $"Web request failed with status {(int)response.StatusCode} {response.ReasonPhrase}.",
                    SkillExecutionStatus.Failed,
                    "http_status");
            }

            return plan.SkillId.Equals("web.read_page", StringComparison.OrdinalIgnoreCase)
                ? SkillResult.Succeeded(FormatReadablePage(url, body, maxLength))
                : SkillResult.Succeeded(FormatFetchResult(url, response, body, maxLength));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SkillResult.Failed(
                $"Web request timed out after {timeoutMilliseconds} ms.",
                SkillExecutionStatus.TimedOut,
                "timeout");
        }
        catch (Exception ex)
        {
            return SkillResult.Failed(
                $"Web request failed: {ex.Message}",
                SkillExecutionStatus.Failed,
                "web_fetch_exception");
        }
    }

    private static string? ValidateUrl(string url, bool allowPrivateNetwork)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "Argument 'url' must be an absolute URL.";
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return "Only http and https URLs are supported.";
        }

        if (!allowPrivateNetwork && IsPrivateNetworkTarget(uri.Host))
        {
            return "Private network and localhost URLs are blocked unless allowPrivateNetwork is true.";
        }

        return null;
    }

    private static bool IsPrivateNetworkTarget(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IsPrivateAddress(address);
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.Equals(IPAddress.IPv6Loopback);
        }

        return false;
    }

    private static string FormatFetchResult(
        string url,
        HttpResponseMessage response,
        string body,
        int maxLength)
    {
        var truncatedBody = Truncate(body, maxLength, out var truncated);
        var builder = new StringBuilder();
        builder.AppendLine($"URL: {url}");
        builder.AppendLine($"Status: {(int)response.StatusCode}");
        builder.AppendLine($"Content-Type: {response.Content.Headers.ContentType?.MediaType ?? "unknown"}");
        builder.AppendLine("Content:");
        builder.AppendLine(truncatedBody);
        if (truncated)
        {
            builder.AppendLine("[truncated]");
        }

        return builder.ToString();
    }

    private static string FormatReadablePage(string url, string html, int maxLength)
    {
        var title = ExtractTitle(html);
        var text = ExtractReadableText(html);
        var truncatedText = Truncate(text, maxLength, out var truncated);
        var builder = new StringBuilder();
        builder.AppendLine($"URL: {url}");
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine($"Title: {title}");
        }

        builder.AppendLine("Readable Text:");
        builder.AppendLine(truncatedText);
        if (truncated)
        {
            builder.AppendLine("[truncated]");
        }

        return builder.ToString();
    }

    private static string ExtractTitle(string html)
    {
        var match = Regex.Match(
            html,
            @"<title[^>]*>(?<title>.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success
            ? NormalizeWhitespace(WebUtility.HtmlDecode(match.Groups["title"].Value))
            : string.Empty;
    }

    private static string ExtractReadableText(string html)
    {
        var withoutScripts = Regex.Replace(
            html,
            @"<(script|style|noscript)[^>]*>.*?</\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withBreaks = Regex.Replace(
            withoutScripts,
            @"</?(p|br|div|section|article|main|header|footer|li|h[1-6])[^>]*>",
            "\n",
            RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withBreaks, "<[^>]+>", " ");
        return NormalizeWhitespace(WebUtility.HtmlDecode(withoutTags));
    }

    private static string NormalizeWhitespace(string value)
    {
        var lines = value
            .Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Regex.Replace(line, @"\s+", " "))
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }

    private static string Truncate(string value, int maxLength, out bool truncated)
    {
        truncated = value.Length > maxLength;
        return truncated ? value[..maxLength] : value;
    }
}
