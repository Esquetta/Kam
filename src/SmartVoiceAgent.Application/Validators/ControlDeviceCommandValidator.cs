using FluentValidation;
using SmartVoiceAgent.Application.Commands;

namespace SmartVoiceAgent.Application.Validators;

public class ControlDeviceCommandValidator : AbstractValidator<ControlDeviceCommand>
{
    public ControlDeviceCommandValidator()
    {
        RuleFor(x=>x.DeviceName).NotEmpty();
        RuleFor(x=>x.Action).NotEmpty();
    }
}

