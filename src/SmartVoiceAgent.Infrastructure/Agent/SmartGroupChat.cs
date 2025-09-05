using AutoGen.Core;
using SmartVoiceAgent.Application.Agent;
using SmartVoiceAgent.Core.Models;


namespace SmartVoiceAgent.Infrastructure.Agent;

public class SmartGroupChat : GroupChat, IGroupChat
{
    public ConversationContextManager ContextManager { get; }
    public GroupChatAnalytics Analytics { get; }
    public GroupChatOptions Options { get; }

    private readonly int _memberCount;

    public SmartGroupChat(
        IEnumerable<IAgent> members,
        Graph workflow,
        IAgent admin,
        ConversationContextManager contextManager,
        GroupChatAnalytics analytics,
        GroupChatOptions options)
        : base(members, admin)
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

            // CRITICAL: Proper message construction
            var userMessage = new TextMessage(Role.User, message, from);

            Console.WriteLine($"📨 Sending message: From='{userMessage.From}', Content='{userMessage.GetContent()}'");

            // CRITICAL: Use correct SendAsync method with proper workflow
            var conversationFlow = this.SendAsync([new TextMessage(Role.User, message, from)], maxRound);

            IMessage lastValidMessage = null;
            var messageCount = 0;
            var allMessages = new List<IMessage>();

            Console.WriteLine($"📨 Processing conversation flow...");

            await foreach (var receivedMessage in conversationFlow.WithCancellation(cancellationToken))
            {
                messageCount++;
                allMessages.Add(receivedMessage);

                Console.WriteLine($"📩 [{messageCount}] Agent: '{receivedMessage.From}' | Role: {receivedMessage.GetRole()}");

                var content = receivedMessage.GetContent();
                if (!string.IsNullOrEmpty(content))
                {
                    var preview = content.Length > 100 ? content.Substring(0, 100) + "..." : content;
                    Console.WriteLine($"    Content: {preview}");
                }

                // İsim uyumunu kontrol et
                if (receivedMessage.From != from &&
                    !string.IsNullOrEmpty(content) &&
                    content.Trim().Length > 5)
                {
                    Console.WriteLine($"✅ Valid response from: {receivedMessage.From}");
                    lastValidMessage = receivedMessage;

                    // Specific agents'tan cevap gelirse conversation'ı bitir
                    var respondingAgents = new[] { "SystemAgent", "TaskAgent", "WebSearchAgent" };
                    if (respondingAgents.Any(agent => receivedMessage.From?.Equals(agent, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        Console.WriteLine($"🎯 Agent task completed by: {receivedMessage.From}");
                        break;
                    }
                }

                // MaxRound kontrolü
                if (messageCount >= maxRound)
                {
                    Console.WriteLine($"⏰ Reached max rounds ({maxRound}), ending conversation");
                    break;
                }

                // Timeout protection
                if ((DateTime.UtcNow - startTime).TotalSeconds > 30)
                {
                    Console.WriteLine($"⏰ Conversation timeout (30s), ending");
                    break;
                }
            }

            // Result validation
            if (lastValidMessage == null)
            {
                Console.WriteLine("⚠️ No valid response received, creating fallback response");

                // Debug: Print all messages
                Console.WriteLine("📋 All messages received:");
                for (int i = 0; i < allMessages.Count; i++)
                {
                    var msg = allMessages[i];
                    Console.WriteLine($"  {i + 1}. From: '{msg.From}' | Role: {msg.GetRole()} | Content: '{msg.GetContent()}'");
                }

                // Fallback response
                lastValidMessage = new TextMessage(
                    Role.Assistant,
                    "Komutunuz alındı ancak işlem tamamlanamadı. Lütfen tekrar deneyin.",
                    from: "System");
            }

            Console.WriteLine($"📊 Conversation Summary:");
            Console.WriteLine($"   Messages: {messageCount}");
            Console.WriteLine($"   Duration: {(DateTime.UtcNow - startTime).TotalMilliseconds:F0}ms");
            Console.WriteLine($"   Final response from: '{lastValidMessage.From}'");

            // Analytics
            Analytics.RecordConversation(new ConversationMetrics
            {
                ConversationId = conversationId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = lastValidMessage != null,
                UserInput = message,
                FinalResult = lastValidMessage?.GetContent() ?? "No response",
                ParticipantCount = _memberCount,
                MessageCount = messageCount
            });

            return lastValidMessage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SendWithAnalyticsAsync Error: {ex.Message}");
            Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");

            // Error analytics
            Analytics.RecordConversation(new ConversationMetrics
            {
                ConversationId = conversationId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = false,
                UserInput = message,
                Error = ex.Message,
                ParticipantCount = _memberCount
            });

            // Error response
            return new TextMessage(
                Role.Assistant,
                $"Sistem hatası: {ex.Message}",
                from: "System");
        }
        finally
        {
            ContextManager.EndConversation(conversationId);
            Console.WriteLine($"🏁 Conversation {conversationId} ended\n");
        }
    }
}