using FluentValidation;
using SmartVoiceAgent.Application.Commands;

namespace SmartVoiceAgent.Application.Validators;

/// <summary>
/// Validator for SendMessageCommand.
/// </summary>
public class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.Recipient)
            .NotEmpty().WithMessage("Recipient cannot be empty.");

        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message content cannot be empty.");
    }
}
