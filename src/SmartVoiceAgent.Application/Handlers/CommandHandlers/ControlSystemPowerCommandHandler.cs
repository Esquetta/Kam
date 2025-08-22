using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;
public sealed class ControlSystemPowerCommandHandler : IRequestHandler<ControlSystemPowerCommand, CommandResult>
{
    private readonly ISystemControlServiceFactory _systemControlFactory;

    public ControlSystemPowerCommandHandler(ISystemControlServiceFactory systemControlFactory)
    {
        _systemControlFactory = systemControlFactory;
    }

    public async Task<CommandResult> Handle(ControlSystemPowerCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var systemControl = _systemControlFactory.CreateSystemService();
            var action = request.Action.ToLower();
            bool success = false;
            string message = "";

            switch (action)
            {
                case "shutdown":
                case "kapat":
                case "bilgisayarı kapat":
                    success = await systemControl.ShutdownSystemAsync(request.DelayMinutes);
                    message = request.DelayMinutes > 0
                        ? $"Sistem {request.DelayMinutes} dakika sonra kapatılacak."
                        : "Sistem kapatılıyor.";
                    break;

                case "restart":
                case "yeniden başlat":
                case "reboot":
                    success = await systemControl.RestartSystemAsync(request.DelayMinutes);
                    message = request.DelayMinutes > 0
                        ? $"Sistem {request.DelayMinutes} dakika sonra yeniden başlatılacak."
                        : "Sistem yeniden başlatılıyor.";
                    break;

                case "sleep":
                case "uyku":
                case "uyku modu":
                    success = await systemControl.SleepSystemAsync();
                    message = success ? "Sistem uyku moduna geçiyor." : "Uyku modu başlatılamadı.";
                    break;

                case "lock":
                case "kilitle":
                case "ekran kilidi":
                    success = await systemControl.LockSystemAsync();
                    message = success ? "Sistem kilitlendi." : "Sistem kilitlenemedi.";
                    break;

                default:
                    return new CommandResult
                    {
                        Success = false,
                        Message = $"Geçersiz güç kontrolü komutu: {request.Action}",
                        Error = "Invalid power action"
                    };
            }

            return new CommandResult
            {
                Success = success,
                Message = message
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                Success = false,
                Message = $"Güç kontrolü sırasında hata oluştu: {ex.Message}",
                Error = ex.Message
            };
        }
    }
}
