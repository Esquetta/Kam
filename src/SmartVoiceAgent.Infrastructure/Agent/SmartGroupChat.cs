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

            // CRITICAL FIX 1: Doğru mesaj formatı
            var userMessage = new TextMessage(Role.User, message, from: from);

            Console.WriteLine($"📨 Created user message: From={userMessage.From}, Content={userMessage.GetContent()}");

            var result = this.SendAsync([new TextMessage(Role.User, message, from)], maxRound);

            IMessage lastValidMessage = null;
            var messageCount = 0;
            var allMessages = new List<IMessage>();

            Console.WriteLine($"📨 Processing message stream...");

            await foreach (var receivedMessage in result)
            {
                messageCount++;
                allMessages.Add(receivedMessage);

                Console.WriteLine($"📩 [{messageCount}] From: {receivedMessage.From}");
                Console.WriteLine($"    Content: {receivedMessage.GetContent()?.Substring(0, Math.Min(150, receivedMessage.GetContent()?.Length ?? 0))}...");
                Console.WriteLine($"    Role: {receivedMessage.GetRole()}");

                // CRITICAL FIX 3: Sadece User olmayan mesajları kabul et
                if (receivedMessage.From != from &&
                    !string.IsNullOrEmpty(receivedMessage.GetContent()) &&
                    receivedMessage.GetContent().Trim().Length > 10) // Minimum content length
                {
                    Console.WriteLine($"✅ Valid response found from {receivedMessage.From}");
                    lastValidMessage = receivedMessage;

                    // CRITICAL FIX 4: TaskAgent'tan gelen mesajları bekle
                    if (receivedMessage.From?.Contains("TaskAgent") == true ||
                        receivedMessage.From?.Contains("SystemAgent") == true ||
                        receivedMessage.From?.Contains("WebAgent") == true)
                    {
                        Console.WriteLine($"🎯 Agent response received from {receivedMessage.From}");
                        // Agent'tan cevap gelirse biraz daha bekle
                        continue;
                    }

                    // CRITICAL FIX 5: Coordinator'dan final cevap gelirse bitir
                    if (receivedMessage.From?.Contains("Coordinator") == true &&
                        messageCount > 2) // En az bir routing olmuş olmalı
                    {
                        Console.WriteLine($"🏁 Final coordinator response received");
                        break;
                    }
                }
                else
                {
                    Console.WriteLine($"⏭️  Skipping message from {receivedMessage.From} (User or empty content)");
                }

                // CRITICAL FIX 6: Maximum round kontrolü
                if (messageCount >= maxRound)
                {
                    Console.WriteLine($"⏰ Max rounds ({maxRound}) reached, ending conversation");
                    break;
                }
            }

            // CRITICAL FIX 7: Valid response kontrolü
            if (lastValidMessage == null)
            {
                Console.WriteLine("❌ No valid response received from any agent");

                // Debug: Tüm mesajları yazdır
                Console.WriteLine("📋 All received messages:");
                for (int i = 0; i < allMessages.Count; i++)
                {
                    var msg = allMessages[i];
                    Console.WriteLine($"  {i + 1}. [{msg.From}] {msg.GetRole()}: {msg.GetContent()}");
                }

                // Fallback: En son agent mesajını al
                lastValidMessage = allMessages.LastOrDefault(m => m.From != from) ??
                                  allMessages.LastOrDefault() ??
                                  new TextMessage(Role.Assistant, "İşlem tamamlanamadı", from: "System");
            }

            Console.WriteLine($"📊 Conversation stats:");
            Console.WriteLine($"   Total messages: {messageCount}");
            Console.WriteLine($"   Duration: {(DateTime.UtcNow - startTime).TotalMilliseconds:F0}ms");
            Console.WriteLine($"   Final response from: {lastValidMessage.From}");

            // Analytics kayıt
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

            Console.WriteLine($"✅ Conversation completed successfully");
            return lastValidMessage;
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
                ParticipantCount = _memberCount
            });

            // Hata durumunda fallback response
            return new TextMessage(Role.Assistant, $"Üzgünüm, bir hata oluştu: {ex.Message}", from: "System");
        }
        finally
        {
            ContextManager.EndConversation(conversationId);
        }
    }
}