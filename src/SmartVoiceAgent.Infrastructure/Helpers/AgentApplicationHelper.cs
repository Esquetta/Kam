using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Helpers;
public class AgentApplicationHelper : IAgentApplicationHelper
{
    private readonly IApplicationService _applicationService;
    private readonly IApplicationScanner _applicationScanner;

    public AgentApplicationHelper(
        IApplicationService applicationService,
        IApplicationScanner applicationScanner)
    {
        _applicationService = applicationService;
        _applicationScanner = applicationScanner;
    }

    public async Task<AgentApplicationResponse> CheckApplicationForAgentAsync(string appName)
    {
        try
        {
            // Önce yüklü mü kontrol et
            var installInfo = await _applicationScanner.FindApplicationAsync(appName);

            if (!installInfo.IsInstalled)
            {
                return new AgentApplicationResponse(
                    false,
                    $"'{appName}' uygulaması bilgisayarınızda yüklü değil. Lütfen önce uygulamayı yükleyin.",
                    appName,
                    null,
                    false,
                    false
                );
            }

            // Yüklüyse çalışıyor mu kontrol et
            var status = await _applicationService.GetApplicationStatusAsync(appName);
            var isRunning = status == Core.Enums.AppStatus.Running;

            var message = isRunning
                ? $"'{installInfo.DisplayName}' uygulaması zaten çalışıyor."
                : $"'{installInfo.DisplayName}' uygulaması yüklü ve açılmaya hazır.";

            return new AgentApplicationResponse(
                true,
                message,
                installInfo.DisplayName,
                installInfo.ExecutablePath,
                true,
                isRunning
            );
        }
        catch (Exception ex)
        {
            return new AgentApplicationResponse(
                false,
                $"Uygulama kontrolü sırasında bir hata oluştu: {ex.Message}",
                appName
            );
        }
    }

    public async Task<AgentApplicationResponse> OpenApplicationForAgentAsync(string appName)
    {
        try
        {
            // Önce uygulama durumunu kontrol et
            var checkResult = await CheckApplicationForAgentAsync(appName);

            if (!checkResult.IsInstalled)
            {
                return checkResult; // Yüklü değilse aynı mesajı döndür
            }

            if (checkResult.IsRunning)
            {
                return new AgentApplicationResponse(
                    true,
                    $"'{checkResult.ApplicationName}' uygulaması zaten çalışıyor.",
                    checkResult.ApplicationName,
                    checkResult.ExecutablePath,
                    true,
                    true
                );
            }

            // Uygulamayı aç
            await _applicationService.OpenApplicationAsync(appName);

            // Kısa bir bekleme sonrası durumu kontrol et
            await Task.Delay(2000);
            var newStatus = await _applicationService.GetApplicationStatusAsync(appName);
            var opened = newStatus == Core.Enums.AppStatus.Running;

            var message = opened
                ? $"'{checkResult.ApplicationName}' uygulaması başarıyla açıldı."
                : $"'{checkResult.ApplicationName}' uygulaması açılmaya çalışıldı, ancak durumu belirsiz.";

            return new AgentApplicationResponse(
                opened,
                message,
                checkResult.ApplicationName,
                checkResult.ExecutablePath,
                true,
                opened
            );
        }
        catch (Exception ex)
        {
            return new AgentApplicationResponse(
                false,
                $"Uygulama açma sırasında bir hata oluştu: {ex.Message}",
                appName
            );
        }
    }
}