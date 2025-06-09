using Core.CrossCuttingConcerns.Exceptions.Types;
using SmartVoiceAgent.CrossCuttingConcerns.Exceptions.Types;

namespace Core.CrossCuttingConcerns.Exceptions.Handlers;

public abstract class ExceptionHandler
{
    public Task HandleExceptionAsync(Exception exception) =>
        exception switch
        {
            AgentOperationException agentOperationException => HandleException(agentOperationException),
            ApplicationNotFoundException applicationNotFoundException => HandleException(applicationNotFoundException),
            CommandNotRecognizedException commandNotRecognizedException => HandleException(commandNotRecognizedException),
            VoiceRecognitionException voiceRecognitionException => HandleException(voiceRecognitionException),
            ValidationException validationException => HandleException(validationException),
            _ => HandleException(exception)
        };

    protected abstract Task HandleException(AgentOperationException agentOperationException);
    protected abstract Task HandleException(ApplicationNotFoundException applicationNotFoundException);
    protected abstract Task HandleException(CommandNotRecognizedException commandNotRecognizedException);
    protected abstract Task HandleException(VoiceRecognitionException voiceRecognitionException);
    protected abstract Task HandleException(ValidationException validationException);
    protected abstract Task HandleException(Exception exception);
}
