using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services.WebResearch;

/// <summary>
/// OpenRouter API ile AI destekli akıllı web araştırma servisi
/// </summary>
public class AiWebResearchService : IWebResearchService
{
    private readonly HttpClient _httpClient;
    private readonly LoggerServiceBase _logger;
    private readonly string _openRouterApiKey;
    private readonly string _model;
    private readonly string _searchApiKey;
    private readonly string _searchEngineId;

    public AiWebResearchService(HttpClient httpClient, LoggerServiceBase logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;

        _searchApiKey = configuration.GetSection("WebResearch:SearchApiKey").Get<string>()
            ?? throw new NullReferenceException("SearchApiKey section cannot found in configuration.");
        _searchEngineId = configuration.GetSection("WebResearch:SearchEngineId").Get<string>()
            ?? throw new NullReferenceException("SearchEngineId section cannot found in configuration.");

        // OpenRouter API setup
        _openRouterApiKey = configuration.GetSection("OpenRouter:ApiKey").Get<string>()
            ?? throw new NullReferenceException("OpenRouter ApiKey not found in configuration.");
        _model = configuration.GetSection("OpenRouter:Model").Get<string>() ?? "microsoft/wizardlm-2-8x22b";

        // HttpClient'ı temiz başlat - her method kendi header'larını ayarlayacak
        _httpClient.DefaultRequestHeaders.Clear();
    }

    public async Task<List<WebResearchResult>> SearchAsync(WebResearchRequest request)
    {
        try
        {
            _logger.Info($"'{request.Query}' konusu için AI destekli araştırma başlatılıyor...");

            // 1. AI ile araştırma planı oluştur
            var researchPlan = await CreateResearchPlanAsync(request);

            // 2. Her anahtar kelime için arama yap
            var allResults = new List<WebResearchResult>();
            foreach (var keyword in researchPlan.Keywords)
            {
                var keywordResults = await PerformGoogleSearchAsync(new WebResearchRequest
                {
                    Query = keyword,
                    Language = request.Language,
                    MaxResults = 3
                });
                allResults.AddRange(keywordResults);
            }

            // 3. AI ile sonuçları değerlendir ve filtrele
            var filteredResults = await FilterAndRankResultsAsync(request, allResults, researchPlan);

            _logger.Info($"{filteredResults.Count} adet kaliteli sonuç bulundu.");
            return filteredResults;
        }
        catch (Exception ex)
        {
            _logger.Error($"AI destekli arama yapılırken hata oluştu: {ex.Message}");
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

            _logger.Info($"{results.Count} adet kaliteli link tarayıcıda açılıyor...");

            var linksToOpen = results.Take(5).ToList();

            foreach (var result in linksToOpen)
            {
                // URL'nin geçerli olduğunu kontrol et
                if (!IsValidUrl(result.Url))
                {
                    _logger.Warn($"Geçersiz URL atlandı: {result.Url}");
                    continue;
                }

                try
                {
                    await OpenUrlInBrowserAsync(result.Url);
                    await Task.Delay(1000); // Tarayıcının yüklenmesi için bekleme
                }
                catch (Exception ex)
                {
                    _logger.Error($"Link açılırken hata: {result.Url} - {ex.Message}");
                    // Bir link hata verse bile diğerlerini açmaya devam et
                    continue;
                }
            }

            _logger.Info("Kaliteli linkler başarıyla açıldı.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Linkler açılırken hata oluştu: {ex.Message}");
            throw;
        }
    }

    public async Task<List<WebResearchResult>> SearchAndOpenAsync(WebResearchRequest request)
    {
        var results = await SearchAsync(request);
        await OpenLinksInBrowserAsync(results);
        return results;
    }

    /// <summary>
    /// AI destekli içerik özetleme ve link açma
    /// </summary>
    public async Task<string> SummarizeAndOpenLinksAsync(WebResearchRequest request)
    {
        try
        {
            // Önce arama yap
            var results = await SearchAsync(request);

            if (!results.Any())
            {
                return "Araştırma sonucu bulunamadı.";
            }

            // Linkleri aç
            await OpenLinksInBrowserAsync(results);

            // AI ile özet oluştur
            var summary = await CreateSummaryAsync(request, results);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.Error($"Özet oluşturulurken hata: {ex.Message}");
            return $"Araştırma tamamlandı ancak özet oluşturulamadı. Hata: {ex.Message}";
        }
    }

    /// <summary>
    /// AI ile araştırma sonuçlarının özetini oluşturur
    /// </summary>
    private async Task<string> CreateSummaryAsync(WebResearchRequest request, List<WebResearchResult> results)
    {
        var resultsText = string.Join("\n\n", results.Select(r =>
            $"Başlık: {r.Title}\nURL: {r.Url}\nÖzet: {r.Description}"));

        var prompt = $@"
Kullanıcı ""{request.Query}"" konusunda araştırma yaptı.
Aşağıdaki araştırma sonuçlarına dayanarak:
1. Konunun genel özetini çıkar
2. Önemli noktaları belirt
3. Kullanıcının dikkat etmesi gereken hususları söyle
4. Kısa ve öz bir özet hazırla (maksimum 500 kelime)

Araştırma Sonuçları:
{resultsText}

Lütfen Türkçe olarak yanıtla.";

        try
        {
            var summary = await CallOpenRouterAsync(
                "Sen araştırma sonuçlarını özetleyen bir uzmansın. Bulguları açık ve anlaşılır şekilde özetlersin.",
                prompt
            );

            return summary;
        }
        catch (Exception ex)
        {
            _logger.Error($"AI özet oluşturulamadı: {ex.Message}");
            return CreateFallbackSummary(results);
        }
    }

    /// <summary>
    /// AI kullanılamadığında basit özet oluşturur
    /// </summary>
    private string CreateFallbackSummary(List<WebResearchResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Araştırma Sonuçları:");
        sb.AppendLine(new string('=', 50));

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine($"{i + 1}. {result.Title}");
            sb.AppendLine($"   URL: {result.Url}");
            if (!string.IsNullOrEmpty(result.Description))
            {
                sb.AppendLine($"   Özet: {result.Description}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<ResearchPlan> CreateResearchPlanAsync(WebResearchRequest request)
    {
        var prompt = $@"
Kullanıcı şu konuda araştırma istiyor: ""{request.Query}""
Bu araştırma için:
1. En uygun anahtar kelimeleri belirle (3-5 adet)
2. Araştırmanın amacını tanımla
3. Hangi tür kaynaklara odaklanılması gerektiğini belirt

SADECE JSON formatında yanıt ver, başka hiçbir metin ekleme:
{{
    ""purpose"": ""araştırmanın amacı"",
    ""keywords"": [""anahtar1"", ""anahtar2"", ""anahtar3""],
    ""preferredSources"": [""resmi siteler"", ""teknik belgeler"", ""güncel haberler""],
    ""language"": ""{request.Language}""
}}";

        var response = await CallOpenRouterAsync(
            "Sen araştırma uzmanısın. Kullanıcının isteğine göre etkili araştırma planları oluşturursun. SADECE temiz JSON yanıtı ver, markdown veya başka format kullanma.",
            prompt
        );

        try
        {
            // AI yanıtını temizle
            var cleanResponse = CleanJsonResponse(response);
            _logger.Info($"AI Yanıtı: {cleanResponse}");

            var plan = JsonSerializer.Deserialize<ResearchPlan>(cleanResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.Info($"Araştırma planı oluşturuldu: {plan?.Keywords?.Count} anahtar kelime");
            return plan ?? new ResearchPlan { Keywords = [request.Query] };
        }
        catch (Exception ex)
        {
            _logger.Warn($"AI yanıtı parse edilemedi: '{response}'. Hata: {ex.Message}");
            return new ResearchPlan
            {
                Keywords = [request.Query],
                Purpose = "Genel araştırma"
            };
        }
    }

    private async Task<List<WebResearchResult>> FilterAndRankResultsAsync(
        WebResearchRequest request,
        List<WebResearchResult> results,
        ResearchPlan plan)
    {
        if (!results.Any()) return results;

        var resultsJson = JsonSerializer.Serialize(results.Select(r => new
        {
            r.Title,
            r.Url,
            r.Description
        }), new JsonSerializerOptions { WriteIndented = true });

        var prompt = $@"
Kullanıcı ""{request.Query}"" konusunda araştırma istiyor.
Araştırma amacı: {plan.Purpose}

Aşağıdaki sonuçları değerlendir ve:
1. Kullanıcının isteğiyle en alakalı olanları seç
2. Kaliteli, güvenilir kaynaklara öncelik ver
3. Tekrar eden veya alakasız sonuçları filtrele
4. En fazla {request.MaxResults} sonuç döndür

Sonuçlar:
{resultsJson}

SADECE JSON formatında yanıt ver:
{{
    ""selectedIndices"": [0, 1, 2],
    ""reasoning"": ""seçim gerekçesi""
}}";

        string response = "";
        try
        {
            response = await CallOpenRouterAsync(
                "Sen araştırma sonuçlarını değerlendiren bir uzmansın. Kullanıcının ihtiyacına en uygun sonuçları seçersin. SADECE temiz JSON yanıtı ver.",
                prompt
            );

            // AI yanıtını temizle
            var cleanResponse = CleanJsonResponse(response);
            _logger.Info($"Filtreleme AI Yanıtı: {cleanResponse}");

            var selection = JsonSerializer.Deserialize<SelectionResult>(cleanResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (selection?.SelectedIndices != null)
            {
                var filteredResults = selection.SelectedIndices
                    .Where(i => i >= 0 && i < results.Count)
                    .Select(i => results[i])
                    .ToList();

                _logger.Info($"AI filtreleme: {results.Count} -> {filteredResults.Count} sonuç");
                _logger.Info($"Filtreleme gerekçesi: {selection.Reasoning}");
                return filteredResults;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"AI filtreleme başarısız. Ham yanıt: '{response}'. Hata: {ex.Message}");
        }

        return results.Take(request.MaxResults).ToList();
    }

    private async Task<string> CallOpenRouterAsync(string systemMessage, string userMessage)
    {
        try
        {
            var requestBody = new
            {
                model = _model,
                prompt = $"System: {systemMessage}\n\nUser: {userMessage}\n\nAssistant:",
                max_tokens = 1000,
                temperature = 0.7,
                stop = new[] { "User:", "System:" }
            };

            var json = JsonSerializer.Serialize(requestBody);

            // HttpRequestMessage kullanarak OpenRouter header'larını ayarla
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/completions");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // OpenRouter için gerekli header'ları ekle
            request.Headers.Add("Authorization", $"Bearer {_openRouterApiKey}");
            request.Headers.Add("HTTP-Referer", "https://esquetta.netlify.app/");
            request.Headers.Add("X-Title", "Smart Voice Agent");

            // Security: Never log API keys or sensitive headers
            _logger.Info($"OpenRouter request prepared (content length: {json.Length})");
            // Authorization header intentionally not logged

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"OpenRouter API error: {response.StatusCode} - {errorContent}");
                // Security: Request headers not logged to avoid leaking sensitive data
                throw new HttpRequestException($"OpenRouter API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.Info($"OpenRouter Ham Yanıt: {responseContent}");

            var apiResponse = JsonSerializer.Deserialize<OpenRouterResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse?.Choices?.Any() == true)
            {
                var firstChoice = apiResponse.Choices.First();
                return firstChoice.Text?.Trim() ?? "";
            }

            throw new InvalidOperationException("OpenRouter API yanıtı beklenmeyen formatta");
        }
        catch (JsonException jsonEx)
        {
            _logger.Error($"JSON Parse hatası: {jsonEx.Message}");
            return await CallOpenRouterChatAsync(systemMessage, userMessage);
        }
        catch (Exception ex)
        {
            _logger.Error($"OpenRouter API çağrısı başarısız: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Chat Completions API ile fallback çağrı
    /// </summary>
    private async Task<string> CallOpenRouterChatAsync(string systemMessage, string userMessage)
    {
        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemMessage },
                    new { role = "user", content = userMessage }
                },
                max_tokens = 1000,
                temperature = 0.7
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // OpenRouter için gerekli header'ları ekle
            request.Headers.Add("Authorization", $"Bearer {_openRouterApiKey}");
            request.Headers.Add("HTTP-Referer", "https://esquetta.netlify.app/");
            request.Headers.Add("X-Title", "Smart Voice Agent");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"OpenRouter Chat API hatası: {response.StatusCode} - {errorContent}");
                throw new HttpRequestException($"OpenRouter Chat API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.Info($"OpenRouter Chat Ham Yanıt: {responseContent}");

            var apiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse?.Choices?.Any() == true)
            {
                var firstChoice = apiResponse.Choices.First();
                return firstChoice.Message?.Content?.Trim() ?? "";
            }

            throw new InvalidOperationException("OpenRouter Chat API yanıtı beklenmeyen formatta");
        }
        catch (Exception ex)
        {
            _logger.Error($"OpenRouter Chat API fallback başarısız: {ex.Message}");
            throw;
        }
    }

    private async Task<List<WebResearchResult>> PerformGoogleSearchAsync(WebResearchRequest request)
    {
        var results = new List<WebResearchResult>();

        try
        {
            if (string.IsNullOrEmpty(_searchApiKey) || string.IsNullOrEmpty(_searchEngineId))
            {
                _logger.Warn("Google Search API anahtarları yapılandırılmamış.");
                return results;
            }

            // URL encoding ve parametre düzeltmeleri
            var encodedQuery = Uri.EscapeDataString(request.Query);

            // Language code düzeltmesi (Google'ın beklediği format)
            var languageCode = GetGoogleLanguageCode(request.Language);

            var url = $"https://www.googleapis.com/customsearch/v1?" +
                     $"key={_searchApiKey}&" +
                     $"cx={_searchEngineId}&" +
                     $"q={encodedQuery}&" +
                     $"num={Math.Min(request.MaxResults, 10)}&" + // Google max 10 results per request
                     $"hl={languageCode}&" +                      // Interface language
                     $"gl={GetGoogleCountryCode(request.Language)}"; // Country for results

            _logger.Info($"Google Search URL: {url.Replace(_searchApiKey, "***API_KEY***")}");

            // HttpRequestMessage kullanarak Google Search için header'ları ayarla
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

            // Google Custom Search için sadece gerekli header'ları ekle (Authorization gerekmez)
            httpRequest.Headers.Add("User-Agent", "Smart Voice Agent/1.0");
            httpRequest.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"Google Search API hatası: {response.StatusCode} - {errorContent}");

                // Specific error handling
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.Error("Google Search API: 403 Forbidden - API key veya Search Engine ID kontrol edin");
                }
                else if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    _logger.Error("Google Search API: 400 Bad Request - Query parametreleri kontrol edin");
                }

                return results; // Hata durumunda boş liste döndür
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.Info($"Google Search Response Length: {responseContent.Length}");

            var searchResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

            // Error response kontrolü
            if (searchResponse.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.TryGetProperty("message", out var msgProp)
                    ? msgProp.GetString()
                    : "Unknown error";
                _logger.Error($"Google Search API Error: {errorMessage}");
                return results;
            }

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
                            Title = CleanText(title) ?? "Başlık Yok",
                            Url = link,
                            Description = CleanText(snippet) ?? "Açıklama Yok",
                            SearchDate = DateTime.Now
                        });
                    }
                }
            }
            else
            {
                _logger.Warn("Google Search API'den 'items' property'si bulunamadı");

                // Search information kontrolü
                if (searchResponse.TryGetProperty("searchInformation", out var searchInfo))
                {
                    if (searchInfo.TryGetProperty("totalResults", out var totalResults))
                    {
                        _logger.Info($"Total Results: {totalResults.GetString()}");
                        if (totalResults.GetString() == "0")
                        {
                            _logger.Info("Arama sonucu bulunamadı");
                        }
                    }
                }
            }

            _logger.Info($"Google Search'den {results.Count} sonuç alındı");
        }
        catch (HttpRequestException httpEx)
        {
            _logger.Error($"Google Search HTTP hatası: {httpEx.Message}");
            if (httpEx.Message.Contains("401"))
            {
                _logger.Error("Google Search API Key geçersiz veya süresi dolmuş");
            }
            else if (httpEx.Message.Contains("403"))
            {
                _logger.Error("Google Search API quota aşıldı veya Custom Search Engine ID geçersiz");
            }
        }
        catch (TaskCanceledException timeoutEx)
        {
            _logger.Error($"Google Search timeout: {timeoutEx.Message}");
        }
        catch (JsonException jsonEx)
        {
            _logger.Error($"Google Search JSON parse hatası: {jsonEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Google Search API genel hata: {ex.Message}");
            _logger.Error($"Stack Trace: {ex.StackTrace}");
        }

        return results;
    }

    /// <summary>
    /// Google'ın beklediği language code formatına çevirir
    /// </summary>
    private string GetGoogleLanguageCode(string language)
    {
        return language?.ToLower() switch
        {
            "tr" or "turkish" or "türkçe" => "tr",
            "en" or "english" or "ingilizce" => "en",
            "de" or "german" or "almanca" => "de",
            "fr" or "french" or "fransızca" => "fr",
            "es" or "spanish" or "ispanyolca" => "es",
            _ => "tr"
        };
    }

    /// <summary>
    /// Google'ın beklediği country code formatına çevirir
    /// </summary>
    private string GetGoogleCountryCode(string language)
    {
        return language?.ToLower() switch
        {
            "tr" or "turkish" or "türkçe" => "TR",
            "en" or "english" or "ingilizce" => "US",
            "de" or "german" or "almanca" => "DE",
            "fr" or "french" or "fransızca" => "FR",
            "es" or "spanish" or "ispanyolca" => "ES",
            _ => "TR"
        };
    }

    /// <summary>
    /// Text'i temizler (HTML entities, fazla boşluklar vs.)
    /// </summary>
    private string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // HTML entities decode
        text = WebUtility.HtmlDecode(text);

        // Multiple spaces to single space - DÜZELTME: Regex sınıfını doğrudan kullan
        text = Regex.Replace(text, @"\s+", " ");

        // Trim
        text = text.Trim();

        return text;
    }

    /// <summary>
    /// Google Custom Search Engine kurulumu için yardımcı metod
    /// Test amaçlı API'yi doğrular
    /// </summary>
    public async Task<bool> TestGoogleSearchAsync()
    {
        try
        {
            var testUrl = $"https://www.googleapis.com/customsearch/v1?" +
                         $"key={_searchApiKey}&" +
                         $"cx={_searchEngineId}&" +
                         $"q=test&" +
                         $"num=1";

            _logger.Info("Google Search API test ediliyor...");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, testUrl);
            httpRequest.Headers.Add("User-Agent", "Smart Voice Agent/1.0");
            httpRequest.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(httpRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.Info("Google Search API test başarılı");
                return true;
            }
            else
            {
                _logger.Error($"Google Search API test başarısız: {response.StatusCode} - {content}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Google Search API test hatası: {ex.Message}");
            return false;
        }
    }

    private async Task OpenUrlInBrowserAsync(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url) || !IsValidUrl(url))
            {
                _logger.Warn($"Geçersiz URL: {url}");
                return;
            }

            var processStartInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                Verb = "open"
            };

            if (OperatingSystem.IsWindows())
            {
                processStartInfo.FileName = url;
            }
            else if (OperatingSystem.IsLinux())
            {
                processStartInfo.FileName = "xdg-open";
                processStartInfo.Arguments = url;
                processStartInfo.UseShellExecute = false;
            }
            else if (OperatingSystem.IsMacOS())
            {
                processStartInfo.FileName = "open";
                processStartInfo.Arguments = url;
                processStartInfo.UseShellExecute = false;
            }
            else
            {
                _logger.Warn("Desteklenmeyen işletim sistemi");
                return;
            }

            using var process = Process.Start(processStartInfo);
            _logger.Info($"Link açıldı: {url}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Link açılırken hata oluştu: {url} - {ex.Message}");
            throw;
        }
    }

    private static bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return Uri.TryCreate(url, UriKind.Absolute, out var result) &&
               (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// AI yanıtını JSON parse için temizler
    /// </summary>
    private string CleanJsonResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        // Markdown kod bloklarını temizle
        response = response.Replace("```json", "").Replace("```", "");

        // Başta ve sondaki whitespace'leri temizle
        response = response.Trim();

        // Backtick karakterlerini temizle
        response = response.Replace("`", "");

        // Eğer yanıt JSON ile başlamıyorsa, JSON'u bulmaya çalış
        var jsonStartIndex = response.IndexOf('{');
        var jsonEndIndex = response.LastIndexOf('}');

        if (jsonStartIndex >= 0 && jsonEndIndex > jsonStartIndex)
        {
            response = response.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex + 1);
        }

        // Son kontrol: hala geçerli değilse boş obje döndür
        if (!response.StartsWith('{'))
        {
            return "{}";
        }

        return response;
    }

    /// <summary>
    /// AI yanıtının JSON olup olmadığını kontrol eder
    /// </summary>
    private bool IsValidJson(string jsonString)
    {
        try
        {
            JsonSerializer.Deserialize<object>(jsonString);
            return true;
        }
        catch
        {
            return false;
        }
    }
}