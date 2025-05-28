using FluentValidation;

namespace SmartVoiceAgent.Application.Validators;

public class PlayMusicCommandValidator : AbstractValidator<PlayMusicCommand>
{
    public PlayMusicCommandValidator()
    {
        RuleFor(x => x.TrackName).NotEmpty();
    }
}

