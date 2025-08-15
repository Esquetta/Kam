using SmartVoiceAgent.Core.Dtos;

namespace SmartVoiceAgent.Core.Interfaces;
public interface IAgentApplicationHelper
{
    /// <summary>
    /// Agent için uygulama durumunu kontrol eder ve uygun yanıtı döner.
    /// </summary>
    /// <param name="appName">Kontrol edilecek uygulama adı.</param>
    /// <returns>Agent'a döndürülecek yanıt bilgisi.</returns>
    Task<AgentApplicationResponse> CheckApplicationForAgentAsync(string appName);

    /// <summary>
    /// Agent için uygulama açma işlemini gerçekleştirir.
    /// </summary>
    /// <param name="appName">Açılacak uygulama adı.</param>
    /// <returns>İşlem sonucu ve mesaj.</returns>
    Task<AgentApplicationResponse> OpenApplicationForAgentAsync(string appName);
}
