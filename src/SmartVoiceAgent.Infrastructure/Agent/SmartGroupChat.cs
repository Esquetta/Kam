using AutoGen.Core;
using SmartVoiceAgent.Application.Agent;
using SmartVoiceAgent.Core.Models;


namespace SmartVoiceAgent.Infrastructure.Agent;

public class SmartGroupChat : GroupChat, IGroupChat
{
    public ConversationContextManager ContextManager { get; }
    public GroupChatAnalytics Analytics { get; }
    public GroupChatOptions Options { get; }

    private int GetParticipantCount()
    {
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
        : base(members, admin, workflow: workflow)
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
            Console.WriteLine($"🚀 Starting conversation: {message}");

            // TextMessage yerine UserProxyAgent'tan mesaj göndermek daha doğru
            var userMessage = new TextMessage(Role.User, message, from: "User");
            var messages = new List<IMessage> { userMessage };

            var result = this.SendAsync(messages, maxRound, cancellationToken);

            IMessage lastMessage = null;
            var messageCount = 0;

            Console.WriteLine($"📨 Starting message processing...");

            await foreach (var item in result)
            {
                Console.WriteLine($"📩 Received message from {item.From}: {item.GetContent()?.Substring(0, Math.Min(100, item.GetContent()?.Length ?? 0))}...");
                lastMessage = item;
                messageCount++;

                // Response'u hemen döndür, tüm mesajları bekleme
                if (item.From != "User" && !string.IsNullOrEmpty(item.GetContent()))
                {
                    Console.WriteLine($"✅ Found response from {item.From}");
                    break;
                }
            }

            if (lastMessage == null)
            {
                Console.WriteLine("❌ No response received from agents");
                throw new InvalidOperationException("No response received from agents");
            }

            Analytics.RecordConversation(new ConversationMetrics
            {
                ConversationId = conversationId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = true,
                UserInput = message,
                FinalResult = lastMessage.GetContent(),
                ParticipantCount = GetParticipantCount(),
                MessageCount = messageCount
            });

            Console.WriteLine($"✅ Conversation completed successfully");
            return lastMessage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error in SendWithAnalyticsAsync: {ex.Message}");
            Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");

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