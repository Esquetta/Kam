using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Diagnostics;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Services.WebResearch;
public class WebResearchService : IWebResearchService
{
    private readonly HttpClient _httpClient;
    private readonly LoggerServiceBase _logger;
    private readonly string _searchApiKey;
    private readonly string _searchEngineId;

    public WebResearchService(HttpClient httpClient, LoggerServiceBase logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _searchApiKey = configuration.GetSection("WebResearch:SearchApiKey").Get<string>()
            ?? throw new NullReferenceException($" SearchApiKey section cannot found in configuration.");
        _searchEngineId = configuration.GetSection("WebResearch:SearchEngineId").Get<string>()
            ?? throw new NullReferenceException($" SearchEngineId section cannot found in configuration.");
    }

    public async Task<List<WebResearchResult>> SearchAsync(WebResearchRequest request)
    {
        try
        {
            _logger.Info($"'{request.Query}' konusu için web araştırması başlatılıyor...");

            var results = await PerformGoogleSearchAsync(request);           

            _logger.Info($"{results.Count} adet sonuç bulundu.");
            return results;
        }
        catch (Exception ex)
        {
            _logger.Error($"Google Search API'si ile arama yapılırken hata oluştu.");
            throw;
        }
    }

    public async Task OpenLinksInBrowserAsync(List<WebResearchResult> results)
    {
        try
        {
            if (!results.Any())
            {
                _logger.Warn("Açılacak link bulunamadı.");
                return;
            }

            _logger.Info($"{results.Count} adet link tarayıcıda açılıyor...");

            // İlk linki mevcut pencerede, diğerlerini yeni sekmede aç
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];

                if (i == 0)
                {
                    // İlk linki mevcut pencerede aç
                    await OpenUrlInBrowserAsync(result.Url, false);
                }
                else
                {
                    // Diğer linkleri yeni sekmede aç
                    await OpenUrlInBrowserAsync(result.Url, true);
                }

                // Tarayıcının yüklenmesi için kısa bir bekleme
                await Task.Delay(500);
            }

            _logger.Info("Tüm linkler başarıyla açıldı.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message);
            throw;
        }
    }

    public async Task<List<WebResearchResult>> SearchAndOpenAsync(WebResearchRequest request)
    {
        var results = await SearchAsync(request);
        await OpenLinksInBrowserAsync(results);
        return results;
    }

    private async Task<List<WebResearchResult>> PerformGoogleSearchAsync(WebResearchRequest request)
    {
        var results = new List<WebResearchResult>();

        try
        {
            if (string.IsNullOrEmpty(_searchApiKey) || string.IsNullOrEmpty(_searchEngineId))
            {
                _logger.Warn("Google Search API anahtarları yapılandırılmamış. Alternatif yöntem kullanılacak.");
                return results;
            }

            var encodedQuery = Uri.EscapeDataString(request.Query);
            var url = $"https://www.googleapis.com/customsearch/v1?" +
                     $"key={_searchApiKey}&" +
                     $"cx={_searchEngineId}&" +
                     $"q={encodedQuery}&" +
                     $"num={request.MaxResults}&" +
                     $"lr=lang_{request.Language}";

            var response = await _httpClient.GetStringAsync(url);
            var searchResponse = JsonSerializer.Deserialize<JsonElement>(response);

            if (searchResponse.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (results.Count >= request.MaxResults) break;

                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                    var link = item.TryGetProperty("link", out var linkProp) ? linkProp.GetString() : "";
                    var snippet = item.TryGetProperty("snippet", out var snippetProp) ? snippetProp.GetString() : "";

                    if (!string.IsNullOrEmpty(link) && IsValidUrl(link))
                    {
                        results.Add(new WebResearchResult
                        {
                            Title = title,
                            Url = link,
                            Description = snippet,
                            SearchDate = DateTime.Now
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Google Search API'si ile arama yapılırken hata oluştu.{ex.Message}");
        }

        return results;
    }


    private async Task OpenUrlInBrowserAsync(string url, bool newTab = false)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo();

            if (OperatingSystem.IsWindows())
            {
                // Windows'da varsayılan tarayıcıyı kullan
                if (newTab)
                {
                    // Chrome'da yeni sekme aç
                    processStartInfo.FileName = "chrome";
                    processStartInfo.Arguments = $"--new-tab \"{url}\"";
                    processStartInfo.UseShellExecute = true;

                    try
                    {
                        Process.Start(processStartInfo);
                    }
                    catch
                    {
                        // Chrome bulunamazsa varsayılan tarayıcıyı kullan
                        processStartInfo.FileName = "cmd";
                        processStartInfo.Arguments = $"/c start \"\" \"{url}\"";
                        processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        Process.Start(processStartInfo);
                    }
                }
                else
                {
                    processStartInfo.FileName = url;
                    processStartInfo.UseShellExecute = true;
                    Process.Start(processStartInfo);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                processStartInfo.FileName = "xdg-open";
                processStartInfo.Arguments = url;
                processStartInfo.UseShellExecute = false;
                Process.Start(processStartInfo);
            }
            else if (OperatingSystem.IsMacOS())
            {
                processStartInfo.FileName = "open";
                processStartInfo.Arguments = url;
                processStartInfo.UseShellExecute = false;
                Process.Start(processStartInfo);
            }

            _logger.Info($"Link açıldı: {url}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Link açılırken hata oluştu: {url},{ex.Message}");
            throw;
        }
    }

    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var result) &&
               (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }
}

// AutoGen Agent entegrasyonu için örnek sınıf
public class SmartResearchAgent
{
    private readonly IWebResearchService _researchService;
    private readonly ILogger<SmartResearchAgent> _logger;

    public SmartResearchAgent(IWebResearchService researchService, ILogger<SmartResearchAgent> logger)
    {
        _researchService = researchService;
        _logger = logger;
    }

    public async Task<string> ResearchAndOpen(string query, int maxResults = 5)
    {
        try
        {
            var request = new WebResearchRequest
            {
                Query = query,
                MaxResults = maxResults,
                Language = "tr"
            };

            _logger.LogInformation($"Araştırma başlatılıyor: {query}");

            var results = await _researchService.SearchAndOpenAsync(request);

            var summary = $"'{query}' konusu hakkında {results.Count} adet kaynak bulundu ve tarayıcınızda açıldı:\n\n";

            for (int i = 0; i < results.Count; i++)
            {
                summary += $"{i + 1}. {results[i].Title}\n";
                summary += $"   🔗 {results[i].Url}\n";
                if (!string.IsNullOrEmpty(results[i].Description))
                {
                    summary += $"   📝 {results[i].Description}\n";
                }
                summary += "\n";
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Araştırma sırasında hata oluştu.");
            return $"Araştırma yapılırken bir hata oluştu: {ex.Message}";
        }
    }
}