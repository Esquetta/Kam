using FluentValidation;
using SmartVoiceAgent.Core.Commands;

namespace SmartVoiceAgent.Application.Validators;

/// <summary>
/// Validator for OpenApplicationCommand.
/// </summary>
public class OpenApplicationCommandValidator : AbstractValidator<OpenApplicationCommand>
{
    public OpenApplicationCommandValidator()
    {
        RuleFor(x => x.ApplicationName)
            .NotEmpty().WithMessage("Application name cannot be empty.")
            .MinimumLength(2).WithMessage("Application name must be at least 2 characters long.");
    }
}
