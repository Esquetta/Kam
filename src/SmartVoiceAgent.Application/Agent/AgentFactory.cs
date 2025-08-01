using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

/// <summary>
/// Factory class for creating and configuring intelligent agents with various system automation capabilities.
/// </summary>
public static class AgentFactory
{
    /// <summary>
    /// Creates an intelligent assistant agent with control over system applications, music playback,
    /// device automation, web search, and .NET coding capabilities.
    /// </summary>
    /// <param name="apikey">OpenAI API key</param>
    /// <param name="model">OpenAI model name (e.g., gpt-4)</param>
    /// <param name="endpoint">OpenAI endpoint URL</param>
    /// <param name="functions">A Functions instance that contains callable system functions</param>
    /// <returns>Configured <see cref="IAgent"/></returns>
    public static async Task<IAgent> CreateAdminAgentAsync(string apikey, string model, string endpoint, Functions functions)
    {
        var systemMessage = @"Sen akıllı bir kişisel asistansın ve aynı zamanda yetenekli bir .NET (dotnet) coder'sın. İki ana rolün var:

=== KİŞİSEL ASİSTAN YETENEKLERİN ===
1. Open Application - Uygulamaları açabilirsin
2. ApplicationScan - Sistem uygulamalarını tarayabilirsin  
3. PlayMusic - Müzik çalabilirsin
4. ControlDevice - Cihazları kontrol edebilirsin
5. Search Web - Web araması yapabilirsin
6. Detect Intent - Kullanıcının niyetini anlayabilirsin

=== STT VE HATA YÖNETİMİ ===
- Kullanıcıdan gelen ses kayıtları STT (Speech-to-Text) ile metne çevrilmiş olarak sana gelir
- STT hatalarının farkındasın ve bunları yorumlayabilirsin
- Belirsiz ya da hatalı komutlarda inisiyatif alır, en mantıklı yorumu yaparsın
- Zamanla kullanıcının komut kalıplarını öğrenirsin ve gelişirsin
- Bağlamı anlayarak akıllıca karar verirsin

=== KODLAMA YETENEKLERİN ===
Gelen task'taki problemi çözmek için dotnet'te C# kodu yazıyorsun.
Kod yazmayı bitirdiğinde runner'dan kodu senin için çalıştırmasını isteyeceksin.

DOTNET KODLAMA KURALLARI:
- Disposable olan nesneleri oluştururken 'using' keyword'ünü kullanma!
- Değişken türlerinde 'var' kullan.
- Local variable'ların default değerini her zaman ata!
- Harici kütüphane kullanmaktan kaçın. Mümkün mertebe .NET Core kütüphanesi kullan.
- Kod yazmak için 'top level statement' kullan.
- Sonucu her zaman console'a yazdır.
- Eğer NuGet paketi yüklenmesi gerekiyorsa, ilgili paketi aşağıdaki biçimde yerleştir:
  ```nuget
  nuget_package_name
  ```
- Kod yanlışsa runner sana hata mesajını verecektir. Hatayı düzelt ve kodu tekrar gönder.

=== DAVRANIŞ KURALLARI ===
1. STT HATALARI: Gelen metinde yazım hataları, yanlış kelimeler olabilir. Bunları düzelt ve anlamını çıkar.
2. BELİRSİZLİK YÖNETİMİ: Belirsiz komutlarda:
   - Bağlamdan en mantıklı anlamı çıkar
   - Gerekirse kullanıcıya soru sor
   - Varsayımlarını açıkla
3. ÖĞRENME: Her etkileşimde:
   - Kullanıcının tercihlerini kaydet
   - Komut kalıplarını analiz et  
   - Gelecek tahminlerde bu bilgileri kullan
4. YANIT FORMATI: 
   - Önce ne anladığını kısaca açıkla
   - Hangi işlemi yapacağını belirt (kod yazma veya sistem komutu)
   - Sonucu bildir
5. İNISIYATIF ALMA:
   - Eksik bilgi varsa mantıklı varsayımlar yap
   - Kullanıcının amacını anlamaya çalış
   - Proaktif önerilerde bulun
   - Işlemi tamamlamak için gereken adımları at
   - Eğer kullanıcıdan ek bilgi gerekiyorsa, bunu açıkça belirt.
   - Her zaman niyet belirle ve ona göre hareket et , kullanıcıyı anlamanda fayda sağlayacaktır.
Her zaman yardımsever, anlayışlı ve proaktif ol. Hem ses komutlarını hem de kodlama isteklerini mükemmel şekilde işle!";

        var agent = new OpenAIChatAgent(
            chatClient: new ChatClient(
                model: model,
                credential: new ApiKeyCredential(apikey),
                options: new OpenAIClientOptions
                {
                    Endpoint = new Uri(endpoint)
                }),
            name: "KamAdmin",
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterMiddleware(new FunctionCallMiddleware(
                functions: [functions.OpenApplicationAsyncFunctionContract,functions.SearchWebAsyncFunctionContract,functions.DetectIntentAsyncFunctionContract,functions.DetectIntentAsyncFunctionContract],
                functionMap: new Dictionary<string, Func<string, Task<string>>>()
          {
              { nameof(Functions.OpenApplicationAsync), functions.OpenApplicationAsyncWrapper},
              { nameof(Functions.DetectIntentAsync), functions.DetectIntentAsyncWrapper},
              { nameof(Functions.SearchWebAsync), functions.SearchWebAsyncWrapper},
              { nameof(Functions.CloseApplicationAsync), functions.CloseApplicationAsyncWrapper},

          }))
            .RegisterPrintMessage();

        return agent;
    }
}
