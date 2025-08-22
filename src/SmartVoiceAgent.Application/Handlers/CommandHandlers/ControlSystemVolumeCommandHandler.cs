using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;
public sealed class ControlSystemVolumeCommandHandler : IRequestHandler<ControlSystemVolumeCommand, CommandResult>
{
    private readonly ISystemControlServiceFactory _systemControlFactory;

    public ControlSystemVolumeCommandHandler(ISystemControlServiceFactory systemControlFactory)
    {
        _systemControlFactory = systemControlFactory;
    }

    public async Task<CommandResult> Handle(ControlSystemVolumeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var systemControl = _systemControlFactory.CreateSystemService();
            var action = request.Action.ToLower();
            bool success = false;
            string message = "";

            switch (action)
            {
                case "increase":
                case "arttır":
                case "yükselt":
                    success = await systemControl.IncreaseSystemVolumeAsync();
                    message = success ? "Ses seviyesi arttırıldı." : "Ses seviyesi arttırılamadı.";
                    break;

                case "decrease":
                case "azalt":
                case "düşür":
                    success = await systemControl.DecreaseSystemVolumeAsync();
                    message = success ? "Ses seviyesi azaltıldı." : "Ses seviyesi azaltılamadı.";
                    break;

                case "mute":
                case "kapat":
                case "sustur":
                    success = await systemControl.MuteSystemVolumeAsync();
                    message = success ? "Ses kapatıldı." : "Ses kapatılamadı.";
                    break;

                case "unmute":
                case "aç":
                case "açık":
                    success = await systemControl.UnmuteSystemVolumeAsync();
                    message = success ? "Ses açıldı." : "Ses açılamadı.";
                    break;

                case "set":
                case "ayarla":
                    success = await systemControl.SetSystemVolumeAsync(request.Level);
                    message = success ? $"Ses seviyesi {request.Level} olarak ayarlandı." : "Ses seviyesi ayarlanamadı.";
                    break;

                default:
                    return new CommandResult
                    {
                        Success = false,
                        Message = $"Geçersiz ses kontrolü komutu: {request.Action}",
                        Error = "Invalid volume action"
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
                Message = $"Ses kontrolü sırasında hata oluştu: {ex.Message}",
                Error = ex.Message
            };
        }
    }
}