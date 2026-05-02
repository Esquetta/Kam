using Core.CrossCuttingConcerns.Exceptions.Types;
using FluentValidation;
using ValidationException = Core.CrossCuttingConcerns.Exceptions.Types.ValidationException;

namespace SmartVoiceAgent.Application.Behaviors.Validation;

public class RequestValidationBehavior<TRequest, TResponse> :
    ICommandPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public RequestValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public Task<TResponse> Handle(TRequest request, CommandHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return HandleCore(request, next.Invoke, cancellationToken);
    }

    private async Task<TResponse> HandleCore(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        ValidationContext<TRequest> context = new(request);
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
        {
            throw new ValidationException(errors);
        }

        return await next();
    }
}

public class RequestValidationQueryBehavior<TRequest, TResponse> :
    IQueryPipelineBehavior<TRequest, TResponse>
    where TRequest : IQuery<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public RequestValidationQueryBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, QueryHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ValidationContext<TRequest> context = new(request);
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
        {
            throw new ValidationException(errors);
        }

        return await next();
    }
}
