using AgentFrameworkToolkit.Tools;
using MediatR;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using System.ComponentModel;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions
{
    /// <summary>
    /// System Agent Functions for desktop application management and device control.
    /// </summary>
    public sealed class SystemAgentTools
    {
        private readonly IMediator _mediator;
        private readonly ConversationContextManager _contextManager;

        public SystemAgentTools(IMediator mediator, ConversationContextManager contextManager)
        {
            _mediator = mediator;
            _contextManager = contextManager;
        }

        [AITool("open_application_async","Opens a desktop application by name.")]
        public async Task<string> OpenApplicationAsync(
            [Description("Name of the application to open (e.g., Chrome, Spotify)")]
            string applicationName)
        {
            Console.WriteLine($"SystemAgent: Opening {applicationName}");

            if (_contextManager.IsApplicationOpen(applicationName))
            {
                // Agent bunu okuduğunda: "Zaten açıkmış, tekrar açmama gerek yok" der.
                return $"{applicationName} uygulaması zaten şu an açık.";
            }

            try
            {
                // Mediator'dan dönen raw sonucu loglama için kullanabilirsin ama Agent'a net bilgi veriyoruz.
                var result = await _mediator.Send(new OpenApplicationCommand(applicationName));

                _contextManager.SetApplicationState(applicationName, true);
                _contextManager.UpdateContext("app_open", applicationName, "Success");

                return $"{applicationName} başarıyla başlatıldı.";
            }
            catch (Exception ex)
            {
                _contextManager.UpdateContext("app_open_error", applicationName, ex.Message);
                return $"{applicationName} açılamadı. Hata: {ex.Message}";
            }
        }

        [AITool("close_application","Closes a running desktop application safely.")]
        public async Task<string> CloseApplicationAsync(
            [Description("Name of the application to close")]
            string applicationName)
        {
            Console.WriteLine($"SystemAgent: Closing {applicationName}");

            if (!_contextManager.IsApplicationOpen(applicationName))
            {
                return $"{applicationName} zaten kapalı veya çalışmıyor.";
            }

            try
            {
                await _mediator.Send(new CloseApplicationCommand(applicationName));

                _contextManager.SetApplicationState(applicationName, false);
                _contextManager.UpdateContext("app_close", applicationName, "Success");

                return $"{applicationName} başarıyla kapatıldı.";
            }
            catch (Exception ex)
            {
                return $"{applicationName} kapatılırken bir hata oluştu: {ex.Message}";
            }
        }

        [AITool("check_application_status","Checks if an application is installed and returns diagnostic info.")]
        public async Task<string> CheckApplicationAsync(
            [Description("Name of the application to verify")]
            string applicationName)
        {
            try
            {
                var result = await _mediator.Send(new CheckApplicationCommand(applicationName));
                return result?.ToString() ?? $"{applicationName} hakkında bilgi bulunamadı.";
            }
            catch (Exception ex)
            {
                return $"Kontrol sırasında hata: {ex.Message}";
            }
        }

        [AITool("get_application_path","Retrieves the full installation path for an application.")]
        public async Task<string> GetApplicationPathAsync(string applicationName)
        {
            try
            {
                var result = await _mediator.Send(new GetApplicationPathCommand(applicationName));
                return result?.ToString() ?? $"{applicationName} için dosya yolu bulunamadı.";
            }
            catch (Exception ex)
            {
                return $"Dosya yolu alınamadı: {ex.Message}";
            }
        }

        [AITool("is_application_running","Checks if an application is currently running.")]
        public async Task<string> IsApplicationRunningAsync(string applicationName)
        {
            try
            {
                var result = await _mediator.Send(new IsApplicationRunningCommand(applicationName));
                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Durum kontrolü başarısız: {ex.Message}";
            }
        }

        [AITool("list_installed_applications","Lists all installed applications on the system.")]
        public async Task<string> ListInstalledApplicationsAsync(bool includeSystemApps = false)
        {
            try
            {
                var result = await _mediator.Send(new ListInstalledApplicationsCommand(includeSystemApps));
                return result?.ToString() ?? "Yüklü uygulama listesi boş.";
            }
            catch (Exception ex)
            {
                return $"Liste alınamadı: {ex.Message}";
            }
        }

        [AITool("play_music","Plays music using available media players.")]
        public async Task<string> PlayMusicAsync(
            [Description("Name of the track, playlist, etc.")] string trackName)
        {
            try
            {
                var result = await _mediator.Send(new PlayMusicCommand(trackName));
                _contextManager.UpdateContext("music_play", trackName, "Started");

                // Result muhtemelen "Playing Bohemian Rhapsody on Spotify" gibi bir şeydir.
                return result?.ToString() ?? $"{trackName} çalınmaya başlandı.";
            }
            catch (Exception ex)
            {
                return $"{trackName} çalınamadı. Hata: {ex.Message}";
            }
        }

        [AITool("control_device","Controls system devices and hardware components.")]
        public async Task<string> ControlDeviceAsync(
            [Description("Name of the device (volume, wifi, etc)")] string deviceName,
            [Description("Action (increase, toggle, on, off)")] string action)
        {
            try
            {
                var result = await _mediator.Send(new ControlDeviceCommand(deviceName, action));
                return result?.ToString() ?? $"{deviceName} üzerinde {action} işlemi uygulandı.";
            }
            catch (Exception ex)
            {
                return $"{deviceName} cihazı kontrol edilemedi: {ex.Message}";
            }
        }

        public IEnumerable<AIFunction> GetTools()
        {
            return
            [
                AIFunctionFactory.Create(OpenApplicationAsync),
                AIFunctionFactory.Create(CloseApplicationAsync),
                AIFunctionFactory.Create(CheckApplicationAsync),
                AIFunctionFactory.Create(GetApplicationPathAsync),
                AIFunctionFactory.Create(IsApplicationRunningAsync),
                AIFunctionFactory.Create(ListInstalledApplicationsAsync),
                AIFunctionFactory.Create(PlayMusicAsync),
                AIFunctionFactory.Create(ControlDeviceAsync)
            ];
        }
    }
}