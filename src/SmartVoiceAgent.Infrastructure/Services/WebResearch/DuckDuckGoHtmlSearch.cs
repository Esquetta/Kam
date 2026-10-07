using System.Net;
using System.Text.RegularExpressions;
using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Infrastructure.Services.WebResearch;

/// <summary>
/// Searches DuckDuckGo's HTML results page, which needs no API key. Web search uses it when no Google Custom
/// Search key is set.
/// </summary>
public static partial class DuckDuckGoHtmlSearch
{
    /// <summary>
    /// The browser-like user agent the HTML page expects; it answers unknown clients with an error page.
    /// </summary>
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36";

    /// <summary>
    /// The shortest gap between two searches. Faster searches get the bot check after the second one.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);

    private static readonly Uri Endpoint = new("https://html.duckduckgo.com/html/");
    private static readonly SemaphoreSlim Turn = new(1, 1);
    private static DateTime _lastSearchUtc = DateTime.MinValue;

    /// <summary>
    /// Returns the search as the page's own form posts it, with the region that matches the language.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="language">A language such as <c>tr</c> or <c>en</c>.</param>
    public static HttpRequestMessage CreateRequest(string query, string? language)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = query,
                ["kl"] = Region(language)
            })
        };
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html");
        request.Headers.Referrer = Endpoint;
        return request;
    }

    /// <summary>
    /// Waits until <see cref="MinimumInterval"/> has passed since the last search, then counts this one.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    public static async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        await Turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastSearchUtc + MinimumInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _lastSearchUtc = DateTime.UtcNow;
        }
        finally
        {
            Turn.Release();
        }
    }

    private static string Region(string? language)
    {
        return language?.Trim().ToLowerInvariant() switch
        {
            "tr" or "turkish" or "türkçe" => "tr-tr",
            "en" or "english" or "ingilizce" => "us-en",
            "de" or "german" or "almanca" => "de-de",
            "fr" or "french" or "fransızca" => "fr-fr",
            "es" or "spanish" or "ispanyolca" => "es-es",
            _ => "wt-wt"
        };
    }

    /// <summary>
    /// Returns whether the page is DuckDuckGo's bot check instead of results, which it shows after too many
    /// searches in a short time.
    /// </summary>
    /// <param name="html">The page.</param>
    public static bool IsBotCheck(string html)
    {
        return html.Contains("anomaly-modal", StringComparison.Ordinal)
            || html.Contains("/anomaly.js", StringComparison.Ordinal)
            || html.Contains("challenge-form", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the organic results from a results page, skipping ads and links that aren't http or https.
    /// </summary>
    /// <param name="html">The results page.</param>
    /// <param name="maxResults">The most results to return.</param>
    public static List<WebResearchResult> Parse(string html, int maxResults)
    {
        var results = new List<WebResearchResult>();
        if (string.IsNullOrEmpty(html) || maxResults <= 0)
        {
            return results;
        }

        var titles = ResultTitle().Matches(html);
        for (var i = 0; i < titles.Count && results.Count < maxResults; i++)
        {
            var title = titles[i];
            var end = i + 1 < titles.Count ? titles[i + 1].Index : html.Length;
            var url = ResolveUrl(title.Groups["href"].Value);
            if (url is null)
            {
                continue;
            }

            var snippet = ResultSnippet().Match(html, title.Index, end - title.Index);
            results.Add(new WebResearchResult
            {
                Title = CleanText(title.Groups["text"].Value),
                Url = url,
                Description = snippet.Success ? CleanText(snippet.Groups["text"].Value) : string.Empty,
                SearchDate = DateTime.Now
            });
        }

        return results;
    }

    private static string? ResolveUrl(string href)
    {
        var value = WebUtility.HtmlDecode(href).Trim();
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            value = "https:" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            // Organic links go through /l/?uddg=<target>; ads go through /y.js and are skipped.
            var target = uri.AbsolutePath == "/l/" ? QueryValue(uri.Query, "uddg") : null;
            if (target is null || !Uri.TryCreate(target, UriKind.Absolute, out uri))
            {
                return null;
            }
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
    }

    private static string? QueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair[..separator] == name)
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }

    private static string CleanText(string html)
    {
        var text = WebUtility.HtmlDecode(Tag().Replace(html, string.Empty));
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex("""<a[^>]*class="result__a"[^>]*href="(?<href>[^"]*)"[^>]*>(?<text>.*?)</a>""", RegexOptions.Singleline)]
    private static partial Regex ResultTitle();

    [GeneratedRegex("""class="result__snippet"[^>]*>(?<text>.*?)</(?:a|div|td)>""", RegexOptions.Singleline)]
    private static partial Regex ResultSnippet();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
