# Smart Voice Agent - Agent Tools Analysis & Recommendations

## Current Agent Architecture Overview

### Existing Agents

| Agent | Purpose | Tools | Status |
|-------|---------|-------|--------|
| **SystemAgent** | Desktop operations, app management | 20+ tools (File, App, Device, Media) | ✅ Active |
| **TaskAgent** | Task management via Todoist | MCP-based (Todoist) | ✅ Active |
| **ResearchAgent** | Web search & information | Web search tool | ✅ Active |
| **CoordinatorAgent** | Orchestrates other agents | No direct tools | ✅ Active |
| **AnalyticsAgent** | Performance monitoring | No tools defined | ⚠️ Missing |

### Current Tools Summary

#### SystemAgentTools (20 tools)
- **Application Management**: open, close, check, get path, is running, list installed
- **Media Control**: play music
- **Device Control**: control device (volume, wifi, etc.)
- **File Operations** (via FileAgentTools): read, write, create, delete, copy, move, exists, info, list, search, create dir, read lines, open file, open directory, show in explorer

#### TaskAgentTools (MCP-based)
- Todoist integration via MCP (Model Context Protocol)
- Dynamic tool loading from MCP server

#### WebSearchAgentTools (1 tool)
- Web search with query, language, results count

---

## 🔍 Code Quality Analysis

### Issues Found

#### 1. **SystemAgentTools.cs**
| Issue | Severity | Description |
|-------|----------|-------------|
| Missing null checks | Medium | Some methods don't validate inputs before processing |
| No async streaming | Low | All operations are blocking await |
| Mixed languages | Low | Turkish responses hardcoded, should use i18n |
| No cancellation tokens | Medium | Methods don't accept CancellationToken |

#### 2. **TaskAgentTools.cs**
| Issue | Severity | Description |
|-------|----------|-------------|
| No error handling | **High** | InitializeAsync doesn't handle MCP connection failures |
| Missing retry logic | Medium | No retry for transient MCP failures |
| No timeout | Medium | McpClient.CreateAsync could hang indefinitely |
| Hardcoded client name | Low | "MCP.Client" should be configurable |

#### 3. **WebSearchAgentTools.cs**
| Issue | Severity | Description |
|-------|----------|-------------|
| Single tool | Low | Only has web search, could have more research tools |
| No caching | Medium | Same queries hit the API repeatedly |

#### 4. **FileAgentTools.cs**
| Issue | Severity | Description |
|-------|----------|-------------|
| Large file (726 lines) | Medium | Should split into partial classes |
| Duplicate code | Low | Path validation repeated in multiple methods |
| No progress reporting | Low | Large file operations don't report progress |

---

## 🛠️ Refactoring Recommendations

### High Priority

#### 1. Add Error Handling & Resilience to TaskAgentTools
```csharp
public async Task InitializeAsync()
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = await GetMcpClientAsync(_mcpOptions, cts.Token);
        _mcpTools = await client.ListToolsAsync(cts.Token);
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Failed to initialize MCP client");
        _mcpTools = Array.Empty<AIFunction>();
    }
}
```

#### 2. Add CancellationToken Support
All tool methods should accept `CancellationToken cancellationToken = default`

#### 3. Add Caching to WebSearchAgentTools
```csharp
private readonly IMemoryCache _cache;
// Cache results for 5 minutes
```

### Medium Priority

#### 4. Split FileAgentTools into Partial Classes
- FileAgentTools.Read.cs
- FileAgentTools.Write.cs
- FileAgentTools.Directory.cs
- FileAgentTools.Open.cs

#### 5. Create Base Tool Class
```csharp
public abstract class AgentToolsBase
{
    protected readonly ILogger? Logger;
    protected readonly ConversationContextManager ContextManager;
    
    protected async Task<string> ExecuteWithErrorHandlingAsync(Func<Task<string>> action, string operation)
    {
        try { return await action(); }
        catch (Exception ex) 
        { 
            Logger?.LogError(ex, "{Operation} failed", operation);
            return $"❌ {operation} failed: {ex.Message}";
        }
    }
}
```

---

## 💡 New Tool Ideas & MCP Server Recommendations

### A. Missing Critical Tools

#### 1. **Clipboard Tools** (SystemAgent)
```csharp
[AITool("get_clipboard", "Gets the current clipboard content")]
public Task<string> GetClipboardAsync()

[AITool("set_clipboard", "Sets the clipboard content")]
public Task<string> SetClipboardAsync(string content)
```

#### 2. **Screenshot Tools** (SystemAgent)
```csharp
[AITool("capture_screenshot", "Captures a screenshot of the screen")]
public Task<string> CaptureScreenshotAsync(int monitorIndex = 0)

[AITool("capture_window", "Captures a specific window")]
public Task<string> CaptureWindowAsync(string windowName)
```

#### 3. **Process Management** (SystemAgent)
```csharp
[AITool("list_processes", "Lists all running processes")]
public Task<string> ListProcessesAsync()

[AITool("kill_process", "Terminates a process by name or ID")]
public Task<string> KillProcessAsync(string processNameOrId)
```

#### 4. **System Information** (SystemAgent)
```csharp
[AITool("get_system_info", "Gets system information (CPU, RAM, disk)")]
public Task<string> GetSystemInfoAsync()

[AITool("get_battery_status", "Gets battery status for laptops")]
public Task<string> GetBatteryStatusAsync()
```

### B. MCP Server Recommendations

#### 1. **GitHub MCP Server** (New Agent: CodeAgent)
```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": { "GITHUB_PERSONAL_ACCESS_TOKEN": "..." }
    }
  }
}
```
**Use Cases**: Create issues, read code, manage PRs

#### 2. **PostgreSQL MCP Server** (New Agent: DatabaseAgent)
```json
{
  "mcpServers": {
    "postgres": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-postgres"],
      "env": { "DATABASE_URL": "postgresql://..." }
    }
  }
}
```
**Use Cases**: Query databases, generate reports

#### 3. **Slack MCP Server** (TaskAgent extension)
```json
{
  "mcpServers": {
    "slack": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-slack"],
      "env": { "SLACK_BOT_TOKEN": "..." }
    }
  }
}
```
**Use Cases**: Send notifications, read messages

#### 4. **Puppeteer MCP Server** (ResearchAgent extension)
```json
{
  "mcpServers": {
    "puppeteer": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-puppeteer"]
    }
  }
}
```
**Use Cases**: Web scraping, automated testing

#### 5. **Home Assistant MCP Server** (New Agent: HomeAgent)
```yaml
# configuration.yaml for HA
mcp:
  servers:
    home_assistant:
      url: "http://homeassistant.local:8123"
      token: "..."
```
**Use Cases**: Control lights, thermostats, sensors

### C. New Agent Proposals

#### 1. **CodeAgent** (Development Tools)
```csharp
public sealed class CodeAgentTools
{
    [AITool("analyze_code", "Analyzes code for issues")]
    [AITool("refactor_code", "Refactors code based on requirements")]
    [AITool("generate_tests", "Generates unit tests for code")]
    [AITool("git_commit", "Creates a git commit")]
    [AITool("git_branch", "Manages git branches")]
}
```

#### 2. **CommunicationAgent** (Email/Slack/Teams)
```csharp
public sealed class CommunicationAgentTools
{
    [AITool("send_email", "Sends an email")]
    [AITool("read_emails", "Reads emails from inbox")]
    [AITool("send_slack_message", "Sends a Slack message")]
    [AITool("schedule_meeting", "Schedules a calendar meeting")]
}
```

#### 3. **HomeAutomationAgent** (Smart Home)
```csharp
public sealed class HomeAutomationAgentTools
{
    [AITool("control_light", "Controls smart lights")]
    [AITool("set_temperature", "Sets thermostat temperature")]
    [AITool("lock_door", "Controls smart locks")]
    [AITool("check_sensor", "Reads sensor data")]
}
```

#### 4. **DocumentAgent** (Document Processing)
```csharp
public sealed class DocumentAgentTools
{
    [AITool("read_pdf", "Extracts text from PDF")]
    [AITool("convert_document", "Converts between document formats")]
    [AITool("summarize_document", "Summarizes long documents")]
    [AITool("compare_documents", "Compares two documents")]
}
```

---

## 📊 MCP Server Registry (Curated List)

### Official MCP Servers
| Server | Purpose | Installation |
|--------|---------|--------------|
| @modelcontextprotocol/server-github | GitHub integration | `npx -y @modelcontextprotocol/server-github` |
| @modelcontextprotocol/server-postgres | PostgreSQL access | `npx -y @modelcontextprotocol/server-postgres` |
| @modelcontextprotocol/server-slack | Slack integration | `npx -y @modelcontextprotocol/server-slack` |
| @modelcontextprotocol/server-puppeteer | Browser automation | `npx -y @modelcontextprotocol/server-puppeteer` |
| @modelcontextprotocol/server-sqlite | SQLite database | `npx -y @modelcontextprotocol/server-sqlite` |

### Community MCP Servers
| Server | Purpose | Installation |
|--------|---------|--------------|
| @mcpget/todoist | Enhanced Todoist | `npx -y @mcpget/todoist` |
| mcp-server-home-assistant | Home Assistant | `pip install mcp-server-home-assistant` |
| mcp-server-obsidian | Obsidian notes | `npx -y mcp-server-obsidian` |
| mcp-server-chroma | Vector database | `pip install mcp-server-chroma` |

---

## 🚀 Implementation Priority

### Phase 1: Critical Fixes (Week 1)
1. ✅ Add error handling to TaskAgentTools
2. ✅ Add cancellation tokens to all tools
3. ✅ Add timeout to MCP client initialization

### Phase 2: Core Enhancements (Week 2-3)
4. Add Clipboard tools
5. Add Screenshot tools
6. Add System Information tools
7. Add caching to WebSearchAgentTools

### Phase 3: New MCP Servers (Week 4)
8. Integrate GitHub MCP Server
9. Integrate PostgreSQL MCP Server
10. Integrate Slack MCP Server

### Phase 4: New Agents (Week 5-6)
11. Create CodeAgent
12. Create CommunicationAgent
13. Create HomeAutomationAgent (if HA available)

---

## 🔧 Tool Naming Convention

Current naming is consistent. Recommend maintaining:
```
{action}_{noun}_async  // for async operations
{action}_{noun}        // for sync operations
```

Examples:
- `open_application_async`
- `read_file`
- `search_web`

---

## 📝 Summary

**Strengths:**
- Well-structured tool architecture
- Good security validation in FileAgentTools
- Proper use of AITool attributes
- Clean separation of concerns

**Areas for Improvement:**
- Error handling in MCP integration
- Missing cancellation token support
- No caching for expensive operations
- Limited research tools
- Missing clipboard/screenshot tools

**Recommended Next Steps:**
1. Fix TaskAgentTools error handling (critical)
2. Add Clipboard and Screenshot tools (high value)
3. Integrate GitHub MCP Server (developer productivity)
4. Create CodeAgent (expand capabilities)
