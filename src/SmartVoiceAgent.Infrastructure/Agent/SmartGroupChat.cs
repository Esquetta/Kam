using AutoGen.Core;
using SmartVoiceAgent.Application.Agent;
using SmartVoiceAgent.Core.Models;


namespace SmartVoiceAgent.Infrastructure.Agent;

public class SmartGroupChat : GroupChat
{
    public ConversationContextManager ContextManager { get; }
    public GroupChatAnalytics Analytics { get; }
    public GroupChatOptions Options { get; }


    private readonly IEnumerable<IAgent> _members;
    private readonly IAgent _admin;
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
        _members = members?.ToList() ?? new List<IAgent>();
        _admin = admin;
        _memberCount = _members?.Count() ?? 0;

        ContextManager = contextManager;
        Analytics = analytics;
        Options = options;
    }

    public IEnumerable<IAgent> AllMembers => _members;
    public IAgent AdminAgent => _admin;
    public int MemberCount => _memberCount;

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
            Console.WriteLine($"📨 Created message: From='{userMessage.From}', Content='{userMessage.GetContent()}'");


            IAsyncEnumerable<IMessage> conversationFlow;

            try
            {
                conversationFlow = this.SendAsync(
                   [userMessage]);

                Console.WriteLine("✅ SendAsync called successfully");
            }
            catch (Exception sendEx)
            {
                Console.WriteLine($"❌ SendAsync failed: {sendEx.Message}");
                throw;
            }

            IMessage lastValidMessage = null;
            var messageCount = 0;
            var allMessages = new List<IMessage>();
            var hasReceivedAnyMessage = false;

            Console.WriteLine($"📨 Starting message processing...");

            try
            {
                await foreach (var receivedMessage in conversationFlow.WithCancellation(cancellationToken))
                {
                    hasReceivedAnyMessage = true;
                    messageCount++;
                    allMessages.Add(receivedMessage);

                    Console.WriteLine($"📩 [{messageCount}] From: '{receivedMessage.From ?? "NULL"}' | Role: {receivedMessage.GetRole()}");

                    var content = receivedMessage.GetContent();
                    if (!string.IsNullOrEmpty(content))
                    {
                        var safeContent = content.Length > 150 ? content.Substring(0, 150) + "..." : content;
                        Console.WriteLine($"    Content: {safeContent}");
                    }
                    else
                    {
                        Console.WriteLine($"    Content: [EMPTY OR NULL]");
                    }

                    // Validation with safer checks
                    if (!string.IsNullOrEmpty(receivedMessage.From) &&
                        receivedMessage.From != from &&
                        !string.IsNullOrEmpty(content) &&
                        content.Trim().Length > 3)
                    {
                        Console.WriteLine($"✅ Valid response from: {receivedMessage.From}");
                        lastValidMessage = receivedMessage;

                        // Check if this is a final response
                        var agentNames = new[] { "SystemAgent", "TaskAgent", "WebSearchAgent", "Coordinator" };
                        if (agentNames.Any(agent =>
                            string.Equals(receivedMessage.From, agent, StringComparison.OrdinalIgnoreCase)))
                        {
                            Console.WriteLine($"🎯 Final response received from: {receivedMessage.From}");

                            // Wait a bit more to see if there are additional responses
                            var additionalWaitTime = TimeSpan.FromSeconds(1);
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            cts.CancelAfter(additionalWaitTime);

                            try
                            {
                                await Task.Delay(500, cts.Token); // Short wait for potential additional messages
                            }
                            catch (OperationCanceledException)
                            {
                                // Expected timeout
                            }

                            break;
                        }
                    }

                    // Safety limits
                    if (messageCount >= maxRound)
                    {
                        Console.WriteLine($"⏰ Reached max rounds ({maxRound})");
                        break;
                    }

                    if ((DateTime.UtcNow - startTime).TotalSeconds > 30)
                    {
                        Console.WriteLine($"⏰ Conversation timeout (30s)");
                        break;
                    }
                }
            }
            catch (Exception foreachEx)
            {
                Console.WriteLine($"❌ Error during message processing: {foreachEx.Message}");
                Console.WriteLine($"❌ StackTrace: {foreachEx.StackTrace}");

                // This might be where the substring error occurs
                if (foreachEx.Message.Contains("startIndex cannot be larger"))
                {
                    Console.WriteLine("🚨 SUBSTRING ERROR DETECTED - This suggests agent responses are malformed");
                }
            }

            // Enhanced result validation
            if (!hasReceivedAnyMessage)
            {
                Console.WriteLine("❌ NO MESSAGES RECEIVED FROM AGENTS");
                Console.WriteLine("🔧 Possible causes:");
                Console.WriteLine("   - Agent workflow is not configured correctly");
                Console.WriteLine("   - Agents are not responding to messages");
                Console.WriteLine("   - GroupChat routing is broken");


                if (lastValidMessage == null)
                {
                    Console.WriteLine("⚠️ Creating fallback response");

                    // Debug output
                    Console.WriteLine("📋 Debug info:");
                    Console.WriteLine($"   Messages received: {messageCount}");
                    Console.WriteLine($"   Has any message: {hasReceivedAnyMessage}");
                    Console.WriteLine($"   All messages count: {allMessages.Count}");

                    for (int i = 0; i < Math.Min(5, allMessages.Count); i++)
                    {
                        var msg = allMessages[i];
                        var msgContent = msg.GetContent();
                        var safeContent = string.IsNullOrEmpty(msgContent) ? "[NULL]" :
                            msgContent.Length > 50 ? msgContent.Substring(0, 50) + "..." : msgContent;
                        Console.WriteLine($"   {i + 1}. From: '{msg.From ?? "NULL"}' | Content: '{safeContent}'");
                    }

                    lastValidMessage = new TextMessage(
                        Role.Assistant,
                        "Özür dilerim, şu anda yanıt veremiyorum. Agent'lar yanıt vermiyor olabilir.",
                        from: "System");
                }

                // Final logging
                Console.WriteLine($"📊 Final Conversation Summary:");
                Console.WriteLine($"   Total messages processed: {messageCount}");
                Console.WriteLine($"   Duration: {(DateTime.UtcNow - startTime).TotalMilliseconds:F0}ms");
                Console.WriteLine($"   Final response from: '{lastValidMessage?.From ?? "NULL"}'");
                Console.WriteLine($"   Response length: {lastValidMessage?.GetContent()?.Length ?? 0} chars");

                // Analytics
                Analytics.RecordConversation(new ConversationMetrics
                {
                    ConversationId = conversationId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = lastValidMessage != null && hasReceivedAnyMessage,
                    UserInput = message,
                    FinalResult = lastValidMessage?.GetContent() ?? "No response",
                    ParticipantCount = _memberCount,
                    MessageCount = messageCount
                });

                return lastValidMessage;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ MAJOR ERROR in SendWithAnalyticsAsync: {ex.Message}");
            Console.WriteLine($"❌ Error Type: {ex.GetType().Name}");
            Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");

            // Error analytics
            Analytics.RecordConversation(new ConversationMetrics
            {
                ConversationId = conversationId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = false,
                UserInput = message ?? "NULL",
                Error = ex.Message,
                ParticipantCount = _memberCount
            });

            return new TextMessage(
                Role.Assistant,
                $"Sistem hatası oluştu: {ex.Message}",
                from: "ErrorHandler");
        }
        finally
        {
            ContextManager.EndConversation(conversationId);
            Console.WriteLine($"🏁 Conversation {conversationId} cleanup completed\n");
        }
        return new TextMessage(
                Role.Assistant,
                "İşlem tamamlandı.",
                from: "System");
    }

}