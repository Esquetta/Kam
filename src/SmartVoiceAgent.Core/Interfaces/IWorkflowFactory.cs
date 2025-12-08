using Microsoft.Extensions.AI;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IWorkflowFactory
{
    Workflow<ChatMessage> CreateDynamicWorkflow();
}