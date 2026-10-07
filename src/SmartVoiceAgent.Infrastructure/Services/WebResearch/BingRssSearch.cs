using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Infrastructure.Services.WebResearch;

/// <summary>
/// Searches Bing's RSS results feed, which needs no API key. Web search falls back to it when DuckDuckGo
/// blocks or returns nothing.
/// </summary>
public static partial class BingRssSearch
{
    /// <summary>
    /// Returns the feed address for a query, in the market that matches the language.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="language">A language such as <c>tr</c> or <c>en</c>.</param>
    public static Uri CreateUri(string query, string? language)
    {
        var market = language?.Trim().ToLowerInvariant() switch
        {
            "tr" or "turkish" or "türkçe" => "&setlang=tr&cc=TR",
            "en" or "english" or "ingilizce" => "&setlang=en&cc=US",
            "de" or "german" or "almanca" => "&setlang=de&cc=DE",
            "fr" or "french" or "fransızca" => "&setlang=fr&cc=FR",
            "es" or "spanish" or "ispanyolca" => "&setlang=es&cc=ES",
            _ => string.Empty
        };

        return new Uri($"https://www.bing.com/search?format=rss&q={Uri.EscapeDataString(query)}{market}");
    }

    /// <summary>
    /// Reads the results from a feed, skipping links that aren't http or https. Returns nothing for a page
    /// that isn't a feed.
    /// </summary>
    /// <param name="xml">The feed.</param>
    /// <param name="maxResults">The most results to return.</param>
    public static List<WebResearchResult> Parse(string xml, int maxResults)
    {
        var results = new List<WebResearchResult>();
        if (string.IsNullOrWhiteSpace(xml) || maxResults <= 0)
        {
            return results;
        }

        XDocument feed;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            feed = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return results;
        }

        foreach (var item in feed.Descendants("item"))
        {
            if (results.Count >= maxResults)
            {
                break;
            }

            var link = item.Element("link")?.Value.Trim();
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            results.Add(new WebResearchResult
            {
                Title = CleanText(item.Element("title")?.Value),
                Url = uri.AbsoluteUri,
                Description = CleanText(item.Element("description")?.Value),
                SearchDate = DateTime.Now
            });
        }

        return results;
    }

    private static string CleanText(string? text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
