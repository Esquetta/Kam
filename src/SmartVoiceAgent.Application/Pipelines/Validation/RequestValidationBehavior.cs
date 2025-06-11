using Core.CrossCuttingConcerns.Exceptions.Types;
using FluentValidation;
using MediatR;
using ValidationException = Core.CrossCuttingConcerns.Exceptions.Types.ValidationException;
namespace SmartVoiceAgent.Application.Behaviors.Validation
{
    public class RequestValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;
        public RequestValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ValidationContext<object> context = new(request);
            IEnumerable<ValidationExceptionModel> errors = _validators
                .Select(validator => validator.Validate(context))
                .SelectMany(result => result.Errors)
                .Where(failure => failure != null)
                .GroupBy(
                    keySelector: p => p.PropertyName,
                    resultSelector: (propertyName, errors) =>
                        new ValidationExceptionModel { Property = propertyName, Errors = errors.Select(e => e.ErrorMessage) }
                )
                .ToList();

            if (errors.Any())
                throw new ValidationException(errors);
            TResponse response = await next();
            return response;
        }
    }
}
