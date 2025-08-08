using AutoGen.Core;
using SmartVoiceAgent.Application.Agent;
using SmartVoiceAgent.Core.Models;
using System.Security.Policy;


namespace SmartVoiceAgent.Infrastructure.Agent;

public class SmartGroupChat : GroupChat, IGroupChat
{
    public ConversationContextManager ContextManager { get; }
    public GroupChatAnalytics Analytics { get; }
    public GroupChatOptions Options { get; }

    private int GetParticipantCount()
    {
        // Reflection yöntemi veya diğer yöntemlerle agent sayısını alın.
        // ...
        return _memberCount;
    }

    private readonly int _memberCount;

    public SmartGroupChat(
        IEnumerable<IAgent> members,
        Graph workflow,
        IAgent admin,
        ConversationContextManager contextManager,
        GroupChatAnalytics analytics,
        GroupChatOptions options)
        : base(members, admin, null, workflow)
    {
        _memberCount = members?.Count() ?? 0;
        ContextManager = contextManager;
        Analytics = analytics;
    }

    public async Task<IMessage> SendWithAnalyticsAsync(
        string message,
        string from = "User",
        int maxRound = 10,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var conversationId = Guid.NewGuid().ToString();

        try
        {
            ContextManager.StartConversation(conversationId, message);

            var result = this.SendAsync([new TextMessage(Role.User, message)], maxRound, cancellationToken);

            string? lastMessage = null;
            IMessage resultMessage = null;
            await foreach (var item in result)
            {
                lastMessage = item.GetContent();
                resultMessage = item;
            }
            Analytics.RecordConversation(new ConversationMetrics
            {
                ConversationId = conversationId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = true,
                UserInput = message,
                FinalResult = lastMessage,
                ParticipantCount = GetParticipantCount(),
                MessageCount = Messages.Count()
            });

            return resultMessage;
        }
        catch (Exception ex)
        {
            Analytics.RecordConversation(new ConversationMetrics
            {
                ConversationId = conversationId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = false,
                UserInput = message,
                Error = ex.Message,
                ParticipantCount = GetParticipantCount()
            });
            throw;
        }
        finally
        {
            ContextManager.EndConversation(conversationId);
        }
    }
}