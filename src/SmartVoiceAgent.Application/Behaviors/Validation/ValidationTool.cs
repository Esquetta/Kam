using FluentValidation;
using FluentValidation.Results;

namespace SmartVoiceAgent.Application.Behaviors.Validation
{
    public class ValidationTool
    {
        public static void Validate(IValidator validator, object entity)
        {
            ValidationContext<object> context = new(entity);
            ValidationResult validationResult = validator.Validate(context);
            if(!validationResult.IsValid)
                throw new ValidationException(validationResult.Errors);
        }
    }
}
