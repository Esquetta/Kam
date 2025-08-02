using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Application;
using System.Text.RegularExpressions;

public class DynamicAppExtractionService
{
    private readonly IApplicationService _applicationService;
    private readonly Dictionary<string, CachedAppInfo> _applicationCache;
    private readonly Dictionary<string, string> _aliasCache;
    private readonly SemaphoreSlim _cacheSemaphore;
    private DateTime _lastCacheUpdate;
    private readonly TimeSpan _cacheValidityPeriod;
    private readonly IApplicationServiceFactory applicationServiceFactory;

    public DynamicAppExtractionService(IApplicationServiceFactory applicationServiceFactory)
    {
        _applicationService = applicationServiceFactory.Create();
        _applicationCache = new Dictionary<string, CachedAppInfo>(StringComparer.OrdinalIgnoreCase);
        _aliasCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _cacheSemaphore = new SemaphoreSlim(1, 1);
        _lastCacheUpdate = DateTime.MinValue;
        _cacheValidityPeriod = TimeSpan.FromMinutes(15); // Cache süresi
        this.applicationServiceFactory = applicationServiceFactory;
    }

    public class CachedAppInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string ExecutableName { get; set; }
        public List<string> Aliases { get; set; } = new List<string>();
        public int Priority { get; set; } = 1;
        public string Category { get; set; } = "general";
    }

    /// <summary>
    /// Ana metod - kullanıcı girdisinden uygulama adını çıkarır
    /// </summary>
    public async Task<string> ExtractApplicationNameAsync(string userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            return null;

        // Metni temizle
        string cleanedText = CleanUserInput(userInput);

        // Cache'i kontrol et ve gerekirse güncelle
        await EnsureCacheIsUpdatedAsync();

        // 1. Tam isim eşleştirmesi
        var exactMatch = FindExactMatch(cleanedText);
        if (exactMatch != null)
            return exactMatch.ExecutableName;

        // 2. Alias eşleştirmesi
        var aliasMatch = FindAliasMatch(cleanedText);
        if (aliasMatch != null)
            return aliasMatch;

        // 3. Kısmi eşleştirme
        var partialMatch = FindPartialMatch(cleanedText);
        if (partialMatch != null)
            return partialMatch.ExecutableName;

        // 4. Bulanık eşleştirme
        var fuzzyMatch = FindFuzzyMatch(cleanedText);
        if (fuzzyMatch != null)
            return fuzzyMatch.ExecutableName;

        // 5. Real-time arama (WindowsApplicationService'i kullan)
        var realTimeResult = await SearchRealTimeAsync(cleanedText);
        if (!string.IsNullOrEmpty(realTimeResult))
            return ExtractExecutableNameFromPath(realTimeResult);

        // 6. Varsayılan davranış
        return HandleNoMatch(cleanedText);
    }

    /// <summary>
    /// Kullanıcı girdisini temizler
    /// </summary>
    private string CleanUserInput(string input)
    {
        string cleaned = input.ToLowerInvariant();

        // Türkçe ve İngilizce komut kelimelerini temizle
        var commandWords = new[]
        {
            @"\baç\b", @"\bopen\b", @"\bstart\b", @"\brun\b", @"\blaunch\b",
            @"\bkapat\b", @"\bclose\b", @"\bstop\b", @"\bshut\b", @"\bquit\b",
            @"\bbaşlat\b", @"\bçalıştır\b", @"\bçalıştırır\b", @"\baçar\b",
            @"\bkapatır\b", @"\bmısın\b", @"\bmisin\b", @"\blütfen\b", @"\bplease\b",
            @"\buygulama\b", @"\bapplication\b", @"\bapp\b", @"\bprogram\b"
        };

        foreach (var pattern in commandWords)
        {
            cleaned = Regex.Replace(cleaned, pattern, " ", RegexOptions.IgnoreCase);
        }

        // Fazla boşlukları temizle
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    /// <summary>
    /// Cache'in güncel olmasını sağlar
    /// </summary>
    private async Task EnsureCacheIsUpdatedAsync()
    {
        if (DateTime.Now - _lastCacheUpdate > _cacheValidityPeriod)
        {
            await _cacheSemaphore.WaitAsync();
            try
            {
                if (DateTime.Now - _lastCacheUpdate > _cacheValidityPeriod) // Double-check
                {
                    await RefreshApplicationCacheAsync();
                }
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Uygulama cache'ini yeniler
    /// </summary>
    private async Task RefreshApplicationCacheAsync()
    {
        _applicationCache.Clear();
        _aliasCache.Clear();

        try
        {
            var applications = await _applicationService.ListApplicationsAsync();

            foreach (var app in applications)
            {
                if (string.IsNullOrEmpty(app.Path) || !app.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var cachedApp = CreateCachedAppInfo(app);

                // Ana isimle cache'e ekle
                _applicationCache[cachedApp.Name] = cachedApp;

                // Alias'ları cache'e ekle
                foreach (var alias in cachedApp.Aliases)
                {
                    if (!_aliasCache.ContainsKey(alias))
                    {
                        _aliasCache[alias] = cachedApp.ExecutableName;
                    }
                }
            }

            _lastCacheUpdate = DateTime.Now;
        }
        catch (Exception ex)
        {
            // Log exception but don't throw
            Console.WriteLine($"Cache refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// AppInfoDTO'dan CachedAppInfo oluşturur
    /// </summary>
    private CachedAppInfo CreateCachedAppInfo(AppInfoDTO app)
    {
        var fileName = Path.GetFileNameWithoutExtension(app.Path);
        var directory = Path.GetFileName(Path.GetDirectoryName(app.Path)) ?? "";

        var cachedApp = new CachedAppInfo
        {
            Name = app.Name,
            Path = app.Path,
            ExecutableName = fileName,
            Category = DetermineCategory(app.Path, fileName),
            Priority = CalculatePriority(app.Path, fileName)
        };

        // Alias'ları oluştur
        cachedApp.Aliases.AddRange(GenerateAliases(fileName, directory, app.Path));

        return cachedApp;
    }

    /// <summary>
    /// Uygulama için alias'lar oluşturur
    /// </summary>
    private List<string> GenerateAliases(string fileName, string directory, string fullPath)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Ana dosya adı
        aliases.Add(fileName);

        // Dizin adı (genelde uygulama adı)
        if (!string.IsNullOrEmpty(directory) && directory != fileName)
        {
            aliases.Add(directory);
        }

        // Özel durumlar için alias'lar
        AddSpecialAliases(fileName, fullPath, aliases);

        // Kelime parçaları
        AddWordBasedAliases(fileName, aliases);

        return aliases.ToList();
    }

    /// <summary>
    /// Özel uygulamalar için alias'lar ekler
    /// </summary>
    private void AddSpecialAliases(string fileName, string fullPath, HashSet<string> aliases)
    {
        var lower = fileName.ToLower();
        var path = fullPath.ToLower();

        // Browser aliases
        if (lower.Contains("chrome"))
        {
            aliases.UnionWith(new[] { "google chrome", "tarayıcı", "browser" });
        }
        if (lower.Contains("firefox"))
        {
            aliases.UnionWith(new[] { "mozilla", "mozilla firefox" });
        }
        if (lower.Contains("edge"))
        {
            aliases.UnionWith(new[] { "microsoft edge", "ms edge" });
        }

        // Office aliases  
        if (lower.Contains("winword") || lower.Contains("word"))
        {
            aliases.UnionWith(new[] { "word", "microsoft word", "ms word", "kelime işlemci" });
        }
        if (lower.Contains("excel"))
        {
            aliases.UnionWith(new[] { "microsoft excel", "ms excel", "hesap tablosu" });
        }
        if (lower.Contains("powerpnt") || lower.Contains("powerpoint"))
        {
            aliases.UnionWith(new[] { "powerpoint", "ppt", "sunum" });
        }

        // Media aliases
        if (lower.Contains("spotify"))
        {
            aliases.UnionWith(new[] { "müzik", "music" });
        }
        if (lower.Contains("vlc"))
        {
            aliases.UnionWith(new[] { "video player", "medya oynatıcı" });
        }

        // Development aliases
        if (lower.Contains("code") && path.Contains("microsoft"))
        {
            aliases.UnionWith(new[] { "vscode", "vs code", "visual studio code" });
        }
        if (lower.Contains("devenv"))
        {
            aliases.UnionWith(new[] { "visual studio", "vs" });
        }

        // Communication aliases
        if (lower.Contains("teams"))
        {
            aliases.UnionWith(new[] { "microsoft teams", "toplantı" });
        }
        if (lower.Contains("discord"))
        {
            aliases.Add("gaming chat");
        }
        if (lower.Contains("whatsapp"))
        {
            aliases.UnionWith(new[] { "wp", "mesaj" });
        }

        // System aliases
        if (lower.Contains("notepad"))
        {
            aliases.UnionWith(new[] { "not defteri", "metin editörü" });
        }
        if (lower.Contains("calc"))
        {
            aliases.UnionWith(new[] { "calculator", "hesap makinesi" });
        }
        if (lower.Contains("mspaint"))
        {
            aliases.UnionWith(new[] { "paint", "boyama", "çizim" });
        }
    }

    /// <summary>
    /// Kelime tabanlı alias'lar ekler
    /// </summary>
    private void AddWordBasedAliases(string fileName, HashSet<string> aliases)
    {
        // CamelCase ve PascalCase'i ayır
        var words = Regex.Split(fileName, @"(?=[A-Z])|[\s\-_]")
            .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length > 1)
            .Select(w => w.ToLower())
            .ToArray();

        foreach (var word in words)
        {
            if (word.Length > 2) // Çok kısa kelimeleri atla
            {
                aliases.Add(word);
            }
        }
    }

    /// <summary>
    /// Uygulama kategorisini belirler
    /// </summary>
    private string DetermineCategory(string path, string fileName)
    {
        var lower = path.ToLower();
        var file = fileName.ToLower();

        if (file.Contains("chrome") || file.Contains("firefox") || file.Contains("edge"))
            return "browser";
        if (file.Contains("word") || file.Contains("excel") || file.Contains("powerpoint"))
            return "office";
        if (file.Contains("spotify") || file.Contains("vlc") || file.Contains("media"))
            return "media";
        if (file.Contains("code") || file.Contains("visual") || lower.Contains("development"))
            return "development";
        if (file.Contains("teams") || file.Contains("discord") || file.Contains("whatsapp"))
            return "communication";
        if (lower.Contains("games") || lower.Contains("steam"))
            return "gaming";

        return "general";
    }

    /// <summary>
    /// Uygulama önceliğini hesaplar
    /// </summary>
    private int CalculatePriority(string path, string fileName)
    {
        var score = 1;
        var lower = path.ToLower();
        var file = fileName.ToLower();

        // Popüler uygulamalara yüksek puan
        if (file.Contains("chrome") || file.Contains("spotify") || file.Contains("discord")) score += 3;
        if (file.Contains("word") || file.Contains("excel") || file.Contains("code")) score += 2;

        // Program Files'ta olanlar daha güvenilir
        if (lower.Contains("program files")) score += 2;

        // System32'deki uygulamalar düşük öncelik
        if (lower.Contains("system32")) score -= 1;

        return Math.Max(1, score);
    }

    // Eşleştirme metodları
    private CachedAppInfo FindExactMatch(string cleanedText)
    {
        return _applicationCache.Values
            .Where(app => string.Equals(app.Name, cleanedText, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(app.ExecutableName, cleanedText, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(app => app.Priority)
            .FirstOrDefault();
    }

    private string FindAliasMatch(string cleanedText)
    {
        if (_aliasCache.TryGetValue(cleanedText, out string exactAlias))
            return exactAlias;

        // Kısmi alias eşleştirmesi
        var partialAlias = _aliasCache
            .Where(kvp => kvp.Key.Contains(cleanedText) || cleanedText.Contains(kvp.Key))
            .OrderByDescending(kvp => kvp.Key.Length)
            .FirstOrDefault();

        return partialAlias.Value;
    }

    private CachedAppInfo FindPartialMatch(string cleanedText)
    {
        var matches = _applicationCache.Values
            .Where(app =>
                app.Name.ToLower().Contains(cleanedText) ||
                app.ExecutableName.ToLower().Contains(cleanedText) ||
                cleanedText.Contains(app.Name.ToLower()) ||
                cleanedText.Contains(app.ExecutableName.ToLower()) ||
                app.Aliases.Any(alias => alias.ToLower().Contains(cleanedText) || cleanedText.Contains(alias.ToLower()))
            )
            .OrderByDescending(app => app.Priority)
            .ThenByDescending(app => CalculateMatchScore(app, cleanedText))
            .ToList();

        return matches.FirstOrDefault();
    }

    private CachedAppInfo FindFuzzyMatch(string cleanedText)
    {
        var matches = _applicationCache.Values
            .Select(app => new
            {
                App = app,
                Similarity = CalculateBestSimilarity(app, cleanedText)
            })
            .Where(m => m.Similarity > 0.6)
            .OrderByDescending(m => m.Similarity * m.App.Priority)
            .ToList();

        return matches.FirstOrDefault()?.App;
    }

    private async Task<string> SearchRealTimeAsync(string cleanedText)
    {
        try
        {
            // WindowsApplicationService'in FindApplicationExecutableAsync metodunu kullan
            if (_applicationService is WindowsApplicationService windowsService)
            {
                // Reflection kullanarak private metoda erişim (gerekirse)
                var method = windowsService.GetType().GetMethod("FindApplicationExecutableAsync");
                if (method != null)
                {
                    var task = (Task<string>)method.Invoke(windowsService, new object[] { cleanedText });
                    return await task;
                }
            }
        }
        catch
        {
            // Başarısız olursa null döndür
        }
        return null;
    }

    // Yardımcı metodlar
    private double CalculateBestSimilarity(CachedAppInfo app, string cleanedText)
    {
        var similarities = new List<double>
        {
            CalculateLevenshteinSimilarity(app.Name.ToLower(), cleanedText),
            CalculateLevenshteinSimilarity(app.ExecutableName.ToLower(), cleanedText)
        };

        similarities.AddRange(app.Aliases.Select(alias =>
            CalculateLevenshteinSimilarity(alias.ToLower(), cleanedText)));

        return similarities.Max();
    }

    private int CalculateMatchScore(CachedAppInfo app, string cleanedText)
    {
        int score = 0;

        if (app.Name.ToLower().Contains(cleanedText)) score += 10;
        if (app.ExecutableName.ToLower().Contains(cleanedText)) score += 8;
        if (app.Aliases.Any(a => a.ToLower().Contains(cleanedText))) score += 6;
        if (cleanedText.Contains(app.Name.ToLower())) score += 5;
        if (cleanedText.Contains(app.ExecutableName.ToLower())) score += 4;

        return score;
    }

    private double CalculateLevenshteinSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0;

        int distance = LevenshteinDistance(s1, s2);
        int maxLength = Math.Max(s1.Length, s2.Length);

        return 1.0 - (double)distance / maxLength;
    }

    private int LevenshteinDistance(string s1, string s2)
    {
        int[,] matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost
                );
            }
        }

        return matrix[s1.Length, s2.Length];
    }

    private string ExtractExecutableNameFromPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return null;

        return Path.GetFileNameWithoutExtension(fullPath);
    }

    private string HandleNoMatch(string cleanedText)
    {
        // Eğer hiçbir eşleştirme bulunamazsa, temizlenmiş metni döndür
        // veya varsayılan bir uygulama belirle
        if (cleanedText.Length > 2)
            return cleanedText;

        return "chrome"; // Varsayılan
    }

    /// <summary>
    /// Cache'deki tüm uygulamaları döndürür
    /// </summary>
    public async Task<Dictionary<string, CachedAppInfo>> GetAllCachedApplicationsAsync()
    {
        await EnsureCacheIsUpdatedAsync();
        return new Dictionary<string, CachedAppInfo>(_applicationCache);
    }

    /// <summary>
    /// Cache'i manuel olarak yeniler
    /// </summary>
    public async Task RefreshCacheAsync()
    {
        await _cacheSemaphore.WaitAsync();
        try
        {
            await RefreshApplicationCacheAsync();
        }
        finally
        {
            _cacheSemaphore.Release();
        }
    }
}