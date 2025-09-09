using AutoGen.Core;

namespace SmartVoiceAgent.Infrastructure.Agent;
public static class AgentDebugHelper
{
    public static async Task TestIndividualAgents(SmartGroupChat groupChat, string testMessage)
    {
        Console.WriteLine("🧪 INDIVIDUAL AGENT TESTING");
        Console.WriteLine("============================");

        var testMsg = new TextMessage(Role.User, testMessage, "TestUser");

        // Test each agent individually
        foreach (var agent in groupChat.AllMembers)
        {
            Console.WriteLine($"\n🎯 Testing {agent.Name}:");
            Console.WriteLine($"   Type: {agent.GetType().Name}");

            try
            {
                var response = await agent.GenerateReplyAsync(
                    [testMsg],
                    new GenerateReplyOptions { MaxToken = 500 },
                    CancellationToken.None);

                if (response != null)
                {
                    Console.WriteLine($"   ✅ Response: {response.GetContent()?.Substring(0, Math.Min(100, response.GetContent()?.Length ?? 0))}...");
                    Console.WriteLine($"   From: {response.From}");
                    Console.WriteLine($"   Role: {response.GetRole()}");
                }
                else
                {
                    Console.WriteLine($"   ❌ NULL response");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error: {ex.Message}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner: {ex.InnerException.Message}");
                }
            }
        }

        // Test admin specifically
        Console.WriteLine($"\n👑 Testing Admin ({groupChat.AdminAgent?.Name}):");
        if (groupChat.AdminAgent != null)
        {
            try
            {
                var adminResponse = await groupChat.AdminAgent.GenerateReplyAsync(
                    [testMsg],
                    new GenerateReplyOptions { MaxToken = 500 },
                    CancellationToken.None);

                if (adminResponse != null)
                {
                    Console.WriteLine($"   ✅ Admin Response: {adminResponse.GetContent()}");
                }
                else
                {
                    Console.WriteLine($"   ❌ Admin returned NULL");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Admin Error: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"   ❌ Admin is NULL");
        }
    }

    public static void ValidateGroupChatSetup(SmartGroupChat groupChat)
    {
        Console.WriteLine("🔍 GROUP CHAT VALIDATION");
        Console.WriteLine("=======================");

        Console.WriteLine($"Members Count: {groupChat.AllMembers?.Count() ?? 0}");
        Console.WriteLine($"Admin: {groupChat.AdminAgent?.Name ?? "NULL"}");

        if (groupChat.AllMembers != null)
        {
            foreach (var member in groupChat.AllMembers)
            {
                Console.WriteLine($"  Member: {member.Name} ({member.GetType().Name})");
            }
        }

        // Check if there's a workflow
        var workflowField = groupChat.GetType().GetField("_workflow",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (workflowField != null)
        {
            var workflow = workflowField.GetValue(groupChat);
            Console.WriteLine($"Workflow: {(workflow != null ? "Present" : "NULL")}");
        }

        Console.WriteLine("✅ Validation completed\n");
    }

    public static async Task TestBasicGroupChatFlow(SmartGroupChat groupChat)
    {
        Console.WriteLine("🔄 BASIC FLOW TEST");
        Console.WriteLine("==================");

        var testMessage = "Spotify'ı açarmısın.";
        var userMessage = new TextMessage(Role.User, testMessage);

        Console.WriteLine($"Sending: '{testMessage}'");

        try
        {
            // Test the basic SendAsync without analytics wrapper
            var messageFlow = groupChat.SendAsync([userMessage], maxRound: 3);

            var messageCount = 0;
            var receivedMessages = new List<IMessage>();

            await foreach (var message in messageFlow)
            {
                messageCount++;
                receivedMessages.Add(message);

                Console.WriteLine($"  [{messageCount}] {message.From}: {message.GetContent()?.Substring(0, Math.Min(50, message.GetContent()?.Length ?? 0))}...");

                if (messageCount > 5) // Safety break
                    break;
            }

            Console.WriteLine($"Total messages in flow: {messageCount}");

            if (messageCount == 0)
            {
                Console.WriteLine("❌ NO MESSAGES IN FLOW - This is the root problem!");
                Console.WriteLine("Possible causes:");
                Console.WriteLine("  - Workflow not configured");
                Console.WriteLine("  - Admin not responding");
                Console.WriteLine("  - Agent connection issues");
            }
            else
            {
                Console.WriteLine("✅ Message flow working");
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Basic flow test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine();
    }
}