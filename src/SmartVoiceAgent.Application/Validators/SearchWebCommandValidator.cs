using FluentValidation;
using SmartVoiceAgent.Application.Commands;

namespace SmartVoiceAgent.Application.Validators;

public class SearchWebCommandValidator : AbstractValidator<SearchWebCommand>
{
    public SearchWebCommandValidator()
    {
        RuleFor(x => x.Query).NotEmpty();
    }
}

