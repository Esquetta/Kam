# Agent Function Calling Optimization Guide

## Problem: Inconsistent Function Execution

You're experiencing intermittent function calling - sometimes the AI uses tools, sometimes it just responds with text. This is a **common issue** that depends on multiple factors.

---

## 🔍 Root Causes

### 1. **Model Capability** (Most Important)

Not all AI models support function calling equally:

| Model Tier | Examples | Function Calling | Success Rate |
|------------|----------|------------------|--------------|
| **Excellent** | GPT-4o, GPT-4, Claude 3.5 Sonnet | Native support | 90-95% |
| **Good** | Claude 3 Haiku, Gemini Pro | Good support | 75-85% |
| **Fair** | WizardLM, Mixtral | Limited support | 50-70% |
| **Poor** | Older/local models | Poor/no support | 20-40% |

**Your current model**: `microsoft/wizardlm-2-8x22b` - **Fair tier**, expect 60-70% reliability

### 2. **Temperature Setting**

High temperature = more "creative" = less likely to follow function schema

```csharp
// Bad - too creative
Temperature = 0.8  // Model might ignore functions

// Good - more deterministic
Temperature = 0.1  // Model follows instructions better
```

### 3. **Tool Naming Conventions**

Some models are sensitive to naming:

```csharp
// ❌ Problematic
[AITool("open_application_async", ...)]  // _async suffix confuses some models

// ✅ Better
[AITool("open_application", ...)]
[AITool("open_app", ...)]  // Even shorter is better for some models
```

### 4. **Instruction Clarity**

The agent needs explicit instructions on WHEN to use tools:

```csharp
// ❌ Vague
"You can open applications"

// ✅ Explicit
"When user says 'open', 'start', or 'launch' followed by an app name, 
YOU MUST call the open_application function. Do not just say you'll do it."
```

---

## ✅ Solutions

### Solution 1: Use a Better Model (Recommended)

Switch to a model with native function calling:

```json
// appsettings.json
{
  "AIService": {
    "Provider": "OpenRouter",
    "ModelId": "anthropic/claude-3.5-sonnet",  // Excellent function calling
    "Temperature": 0.1  // Low for consistency
  }
}
```

**Budget-friendly alternatives:**
- `anthropic/claude-3-haiku` - Good balance of price/performance
- `google/gemini-flash-1.5` - Fast, decent function calling

### Solution 2: Improve Instructions

Update `AgentFactory.cs`:

```csharp
var instructions = @"You are a System Agent with access to tools/functions.

CRITICAL INSTRUCTIONS:
1. When user wants to open an app, YOU MUST call 'open_application' function immediately
2. When user wants to close an app, YOU MUST call 'close_application' function immediately  
3. Do NOT just say you'll do it - actually call the function
4. After calling a function, report the result to the user

Function calling rules:
- Always use exact app names (Chrome, not chrome.exe)
- Always call functions for actions, never just describe them
- Wait for function result before responding

Available actions:
- open_application(name) - Opens any desktop app
- close_application(name) - Closes running apps
- control_device(device, action) - Controls volume/wifi/etc
- play_music(track) - Plays music
- read_file(path) - Reads file contents
- write_file(path, content) - Writes to files
- list_files(path) - Lists directory contents";
```

### Solution 3: Add Function-Calling Examples

Some models need examples in the prompt:

```csharp
var instructions = @"You are a System Agent with tool access.

EXAMPLES OF WHEN TO USE FUNCTIONS:

User: 'Open Chrome'
→ Call: open_application(applicationName: ""Chrome"")

User: 'Close Spotify'
→ Call: close_application(applicationName: ""Spotify"")

User: 'Increase volume'
→ Call: control_device(deviceName: ""volume"", action: ""increase"")

User: 'What's in my Documents folder?'
→ Call: list_files(directoryPath: ""C:\Users\[User]\Documents"")

RULE: Always use functions for actions. Never just say you'll do it.";
```

### Solution 4: Force Function Mode (If Supported)

Some SDKs support forcing function calls:

```csharp
// In AgentBuilder or when calling the model
var response = await agent.RunAsync(messages, new AgentRunOptions
{
    ToolChoice = ToolChoice.Auto,  // or ToolChoice.Required
    Temperature = 0.1
});
```

### Solution 5: Tool Name Optimization

Rename tools to be more "action-oriented":

```csharp
// Current names
[AITool("open_application_async", ...)]
[AITool("close_application", ...)]

// Better names for AI understanding
[AITool("open_app", "Opens a desktop application immediately")]
[AITool("close_app", "Closes a running application immediately")]
[AITool("set_volume", "Changes the system volume level")]
[AITool("list_directory", "Lists files in a folder")]
```

---

## 🛠️ Quick Fix Checklist

- [ ] **Switch to GPT-4o or Claude 3.5 Sonnet** (biggest impact)
- [ ] **Set Temperature to 0.1 or 0.2** (more deterministic)
- [ ] **Add explicit "YOU MUST CALL FUNCTION" instructions**
- [ ] **Include examples in the system prompt**
- [ ] **Use shorter, action-oriented tool names**
- [ ] **Remove `_async` suffixes from tool names**

---

## 📊 Testing Function Calling

Test with this prompt sequence:

```
User: Open Chrome
→ Expected: Function call to open_application

User: Close Chrome  
→ Expected: Function call to close_application

User: What's the weather?
→ Expected: No function call (informational query)

User: Play some music
→ Expected: Function call to play_music
```

---

## 🎯 Recommended Configuration

```csharp
// AgentFactory.cs - CreateSystemAgent()
public AIAgent CreateSystemAgent()
{
    var instructions = @"You are a System Agent that controls the computer.

MANDATORY RULES:
1. For ANY action request (open, close, play, control), YOU MUST call the appropriate function
2. Never respond with text when a function can do the action
3. Always wait for function results before replying

FUNCTIONS (use these immediately when applicable):
- open_app(name) - When user says 'open X'
- close_app(name) - When user says 'close X'  
- control_device(device, action) - When user says 'increase volume', 'turn off wifi', etc.
- play_music(track) - When user says 'play X'
- read_file(path) - When user asks about file contents
- write_file(path, content) - When user wants to save something

EXAMPLE:
User: 'Open Chrome'
You: [Call open_app with name=""Chrome""]";

    return new AgentBuilder(_chatClient, _serviceProvider)
        .WithName("SystemAgent")
        .WithInstructions(instructions)
        .WithTools<SystemAgentTools>()
        .Build();
}
```

---

## 🔧 Model-Specific Tips

### For WizardLM models:
- Use temperature 0.1 or lower
- Be extremely explicit in instructions
- Add multiple examples
- Consider using shorter tool names

### For Claude models:
- Native function calling works well
- Temperature 0.2-0.3 is fine
- Clear descriptions are sufficient

### For GPT-4/GPT-4o:
- Best function calling support
- Temperature 0.1 for consistency
- Can handle complex tool schemas

---

## 📈 Measuring Improvement

Track these metrics:
- **Function call rate**: % of action requests that trigger functions
- **Correct parameter rate**: % of functions with correct parameters
- **Hallucination rate**: % of function calls that don't match user intent

Target: >90% function call rate, >95% correct parameters
