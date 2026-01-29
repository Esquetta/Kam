# Slack MCP Server Integration Guide

This guide explains how to set up and use the Slack MCP (Model Context Protocol) server integration with Smart Voice Agent.

## Overview

The Slack MCP integration allows the Smart Voice Agent to interact with your Slack workspace, enabling features like:

- 📢 Posting messages to channels
- 💬 Replying to threads
- 👥 Listing users and channels
- 📜 Getting channel history
- 😀 Adding emoji reactions
- 👤 Getting user profiles

## Prerequisites

1. A Slack workspace where you have admin/owner permissions
2. Node.js and NPM installed (for NPX transport)
3. Your Slack Bot Token and Team ID

## Setup Instructions

### Step 1: Create a Slack App

1. Visit [Slack API Apps page](https://api.slack.com/apps)
2. Click **"Create New App"**
3. Choose **"From scratch"**
4. Enter an app name (e.g., "Smart Voice Agent")
5. Select your workspace
6. Click **"Create App"**

### Step 2: Configure Bot Token Scopes

Navigate to **"OAuth & Permissions"** in the left sidebar and add these Bot Token Scopes:

| Scope | Description |
|-------|-------------|
| `channels:history` | View messages and content in public channels |
| `channels:read` | View basic channel information |
| `chat:write` | Send messages as the app |
| `reactions:write` | Add emoji reactions to messages |
| `users:read` | View users and basic information |
| `users.profile:read` | View detailed user profiles |

To add scopes:
1. Scroll to "Scopes" section
2. Click **"Add an OAuth Scope"** under "Bot Token Scopes"
3. Add each scope from the list above

### Step 3: Install App to Workspace

1. Scroll to the top of "OAuth & Permissions" page
2. Click **"Install to Workspace"**
3. Review permissions and click **"Allow"**
4. Copy the **"Bot User OAuth Token"** (starts with `xoxb-`)

### Step 4: Get Your Team ID

1. In Slack, click your workspace name at the top left
2. Go to **"Settings & administration"** → **"Workspace settings"**
3. Your Team ID is at the end of the URL or in the workspace information
4. It starts with a `T` (e.g., `T01234567`)

Alternative method:
- Right-click any channel
- Select "Open channel details"
- Look at the channel URL - it contains the Team ID

### Step 5: Configure Smart Voice Agent

Add the following to your `appsettings.json` or User Secrets:

```json
{
  "McpOptions": {
    "SlackBotToken": "xoxb-your-bot-token-here",
    "SlackTeamId": "T01234567",
    "SlackChannelIds": "C01234567,C76543210"
  }
}
```

**Configuration Options:**

| Option | Required | Description |
|--------|----------|-------------|
| `SlackBotToken` | ✅ | Your Bot User OAuth Token (xoxb-*) |
| `SlackTeamId` | ✅ | Your Slack workspace ID (T*) |
| `SlackChannelIds` | ❌ | Comma-separated list of allowed channel IDs |
| `SlackTransport` | ❌ | Transport type: "stdio" (default) or "http" |
| `SlackHttpPort` | ❌ | HTTP port when using HTTP transport (default: 3000) |
| `SlackAuthToken` | ❌ | Bearer token for HTTP transport authorization |

### Step 6: Invite Bot to Channels

For the bot to post messages in channels:

1. In Slack, go to the channel
2. Type `/invite @YourBotName`
3. The bot will join the channel

## Available MCP Tools

Once configured, the following tools become available to agents:

### Channel Management

| Tool | Description | Parameters |
|------|-------------|------------|
| `slack_list_channels` | List public/predefined channels | `limit`, `cursor` |
| `slack_get_channel_history` | Get recent messages from a channel | `channel_id`, `limit` |

### Messaging

| Tool | Description | Parameters |
|------|-------------|------------|
| `slack_post_message` | Post a new message | `channel_id`, `text` |
| `slack_reply_to_thread` | Reply to a thread | `channel_id`, `thread_ts`, `text` |
| `slack_add_reaction` | Add emoji reaction | `channel_id`, `timestamp`, `reaction` |
| `slack_get_thread_replies` | Get thread replies | `channel_id`, `thread_ts` |

### Users

| Tool | Description | Parameters |
|------|-------------|------------|
| `slack_get_users` | List workspace users | `limit`, `cursor` |
| `slack_get_user_profile` | Get user profile | `user_id` |

## Usage Examples

### Post a Message

```csharp
// In an agent tool or command handler
var slackTools = serviceProvider.GetRequiredService<SlackAgentTools>();
await slackTools.InitializeAsync();

var result = await slackTools.SendMessageAsync(
    channelId: "C01234567",
    message: "Hello from Smart Voice Agent! 👋");
```

### List Channels

```csharp
var result = await slackTools.GetChannelsAsync(limit: 50);
```

### Using in an Agent

```csharp
public sealed class CommunicationAgentTools
{
    private readonly SlackAgentTools _slackTools;

    public CommunicationAgentTools(SlackAgentTools slackTools)
    {
        _slackTools = slackTools;
    }

    [AITool("send_slack_message", "Sends a message to a Slack channel")]
    public async Task<string> SendSlackMessageAsync(
        [Description("Channel ID (e.g., C01234567)")] string channelId,
        [Description("Message text to send")] string message)
    {
        await _slackTools.InitializeAsync();
        return await _slackTools.SendMessageAsync(channelId, message);
    }
}
```

## Transport Options

### Stdio Transport (Default)

Uses NPX to run the Slack MCP server. Best for local development.

**Pros:**
- Simple setup
- No additional ports needed
- Automatic updates via NPX

**Cons:**
- Requires Node.js and NPM
- Spawns separate process

### HTTP Transport

Runs the MCP server as an HTTP service.

**Setup:**
1. Install the Slack MCP server globally:
   ```bash
   npm install -g @modelcontextprotocol/server-slack
   ```

2. Or use Docker:
   ```bash
   docker run -d -p 3000:3000 \
     -e SLACK_BOT_TOKEN=xoxb-your-token \
     -e SLACK_TEAM_ID=T01234567 \
     mcp/slack --transport http
   ```

3. Configure HTTP transport:
   ```json
   {
     "McpOptions": {
       "SlackBotToken": "xoxb-your-token",
       "SlackTeamId": "T01234567",
       "SlackTransport": "http",
       "SlackHttpPort": 3000
     }
   }
   ```

**Pros:**
- Can run remotely
- Better for production environments
- Supports authentication tokens

**Cons:**
- Requires port management
- More complex setup

## Troubleshooting

### "Slack is not properly configured" Warning

**Cause:** Missing or invalid Bot Token or Team ID

**Solution:**
1. Verify your Bot Token starts with `xoxb-`
2. Verify your Team ID starts with `T`
3. Check configuration is saved in appsettings.json or User Secrets

### "Failed to initialize Slack MCP client"

**Cause:** Connection issues or invalid credentials

**Solution:**
1. Verify your Bot Token is valid
2. Check that the app is installed to your workspace
3. Ensure you have internet connectivity
4. Check the logs for detailed error messages

### "not_in_channel" Error

**Cause:** Bot is not a member of the channel

**Solution:**
1. In Slack, go to the channel
2. Type `/invite @YourBotName`
3. Retry the operation

### "missing_scope" Error

**Cause:** Required OAuth scope not granted

**Solution:**
1. Go to your Slack App's OAuth & Permissions page
2. Add the missing scope
3. Reinstall the app to your workspace
4. Update the Bot Token in configuration

### "channel_not_found" Error

**Cause:** Channel ID is incorrect or channel is private

**Solution:**
1. Get the correct Channel ID from Slack
2. For private channels, invite the bot first
3. Ensure the channel ID format is correct (starts with `C`)

## Security Considerations

1. **Never commit tokens to version control**
   - Use User Secrets for local development
   - Use environment variables in production

2. **Limit channel access**
   - Use `SlackChannelIds` to restrict which channels the bot can access
   - Only add necessary OAuth scopes

3. **Use HTTP transport with authentication in production**
   - Set `SlackAuthToken` for Bearer token authentication
   - Run behind a firewall or VPN

4. **Monitor bot activity**
   - Check Slack App's analytics dashboard
   - Review audit logs regularly

## References

- [Slack MCP Server - Official](https://github.com/modelcontextprotocol/servers/tree/main/src/slack)
- [Slack API Documentation](https://api.slack.com/)
- [Slack App Directory](https://api.slack.com/apps)
- [Model Context Protocol Specification](https://modelcontextprotocol.io/)

## Support

If you encounter issues:

1. Check the application logs for detailed error messages
2. Verify your Slack App configuration
3. Test your Bot Token with a simple API call
4. Review the [Slack API documentation](https://api.slack.com/)
