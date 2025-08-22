using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;
public sealed class ControlScreenBrightnessCommandHandler : IRequestHandler<ControlScreenBrightnessCommand, CommandResult>
{
    private readonly ISystemControlServiceFactory _systemControlFactory;

    public ControlScreenBrightnessCommandHandler(ISystemControlServiceFactory systemControlFactory)
    {
        _systemControlFactory = systemControlFactory;
    }

    public async Task<CommandResult> Handle(ControlScreenBrightnessCommand request, CancellationToken cancellationToken)
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
                case "parlat":
                case "yükselt":
                    success = await systemControl.IncreaseScreenBrightnessAsync();
                    message = success ? "Ekran parlaklığı arttırıldı." : "Ekran parlaklığı arttırılamadı.";
                    break;

                case "decrease":
                case "azalt":
                case "karart":
                case "düşür":
                    success = await systemControl.DecreaseScreenBrightnessAsync();
                    message = success ? "Ekran parlaklığı azaltıldı." : "Ekran parlaklığı azaltılamadı.";
                    break;

                case "set":
                case "ayarla":
                    success = await systemControl.SetScreenBrightnessAsync(request.Level);
                    message = success ? $"Ekran parlaklığı {request.Level}% olarak ayarlandı." : "Ekran parlaklığı ayarlanamadı.";
                    break;

                default:
                    return new CommandResult
                    {
                        Success = false,
                        Message = $"Geçersiz parlaklık kontrolü komutu: {request.Action}",
                        Error = "Invalid brightness action"
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
                Message = $"Ekran parlaklığı kontrolü sırasında hata oluştu: {ex.Message}",
                Error = ex.Message
            };
        }
    }
}