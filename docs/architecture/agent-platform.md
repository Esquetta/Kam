# Kam Agent Platform: target architecture

Status: phase 1 implemented (October 2026); phases 2 to 4 proposed. This replaces the skill-first planner with a tool-calling agent runtime of the kind Claude Code, Codex and Cursor use. Skills, MCP servers and plugins all plug into that one runtime.

## Why the current design has to change

What happens today:

- **The chat does not run an agent.** `SkillFirstCommandRuntimeService` asks the model for one JSON object naming one skill. It runs that one skill and stops: there is no loop and no follow-up turn after a tool result. Native tool calling is never used. In the planner prompt, built-in skills carry an id and argument list but no description.
- **Two parallel agent stacks exist.** `AgentFactory` builds System, Task, Research, Communication and Coordinator agents with real `AIFunction` tools, and `SmartAgentOrchestrator` is registered, but the chat path never calls either one.
- **MCP is Todoist only.** It is HTTP only and hardcoded in `McpOptions`, `TaskAgentTools` and `AiRuntimeConfigurationMapper`. Its tools are attached to the TaskAgent, which is never used. Users cannot add MCP servers. `McpSkillAdapter` is a stub that has no executor.
- **Imported skills are weak.**
  - An imported `SKILL.md` can only produce desktop-automation steps. It cannot run its scripts, and it cannot use the shell or MCP.
  - Imports are not persisted, so they are gone after a restart.
  - Approving an external skill calls the model again, so the plan that runs can differ from the plan the user approved.
- **Integrations are separate code paths.**
  - Todoist, GitHub, SMTP and Twilio each have their own UI and configuration code.
  - Settings are read once at startup, so a changed key needs a restart.
  - GitHub and Todoist cannot be reached from chat.
- **There is no real coding mode.**
  - The coding agent can only be turned on from the console app.
  - Its tools are read-only, it gets one extra round at most, and it can only propose `file.patch` and `tests.run`.
  - There is no workspace picker, no AGENTS.md context, no checkpoints and no undo.
- **Lists are hardcoded.** Mutating ids, previewable ids and read-only tool ids live in hardcoded lists spread across five files.

## Target architecture

```
                ┌──────────────────────── UI / Voice / Tray / Console ────────────────────────┐
                │  chat threads · streaming · approval cards · diff viewer · plugin manager   │
                └────────────────────────────────────┬────────────────────────────────────────┘
                                                     │ IAgentSessionService
┌────────────────────────────────────────────────────▼─────────────────────────────────────────┐
│ Agent Runtime                                                                                 │
│  AgentLoop (IChatClient + FunctionInvokingChatClient)   ContextBuilder   SessionStore        │
│   model → tool calls → approval gate → results → model … until done / max turns / cancel     │
│  Subagents (task tool)   Context compaction   Usage + trace                                  │
└──────────────┬───────────────────────────┬───────────────────────────┬───────────────────────┘
               │ IToolCatalog              │ IPermissionService        │ ISkillCatalog
┌──────────────▼──────────────┐ ┌──────────▼─────────────┐ ┌───────────▼────────────────────┐
│ Tool providers              │ │ Approval policy        │ │ Agent Skills (SKILL.md)        │
│  Built-in: files, edit,     │ │  modes: ask / auto-edit│ │  name+description in prompt,   │
│  shell, git, web, desktop,  │ │  / full-auto           │ │  body loaded on demand,        │
│  apps, clipboard, email     │ │  rules: allow/deny     │ │  bundled scripts via shell     │
│  MCP servers (stdio, http)  │ │  per tool + args       │ │  user, project, plugin scopes  │
│  Plugin tools               │ │  approval bound to the │ └────────────────────────────────┘
└──────────────┬──────────────┘ │  exact call            │
               │                └────────────────────────┘
┌──────────────▼───────────────────────────────────────────────────────────────────────────────┐
│ Plugin host: plugin.json bundles skills/, agents/, commands/, .mcp.json, hooks               │
│  sources: local folder, git URL, marketplace.json; Claude Code plugin layout compatible     │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 1. Agent loop (replaces the skill planner)

- Built on `Microsoft.Extensions.AI` with Kam's own loop (`AgentRuntime`) rather than `FunctionInvokingChatClient`, so the approval gate sits between the model's call and the tool and the loop is testable with a fake `IChatClient`.
  - The model calls tools natively.
  - Results go back to the model.
  - The loop runs until the model answers, the iteration limit is reached, or the user cancels.
- Tokens stream to the chat as they arrive, and each tool call shows as a step with its arguments, status and result.
- Every thread is persisted in `%AppData%/Kam/sessions/<id>.json` and reopens after a restart.
- Long threads are compacted: older turns are summarised once the context passes a token budget.
- Voice, the tray "New task" item and the console app all go through the same `IAgentSessionService`.

### 2. One tool model

- `IToolProvider` returns `AITool`s, and each tool carries metadata: `Risk` (read, write, execute, network, external) and `Category`.
- The existing built-in skill executors are adapted to tools once. The duplicate `[AITool]` classes and `ISkillExecutor` wrappers then merge.
- The hardcoded id lists go away, because every decision reads the tool's metadata.

### 3. Permissions and approval

- **Modes:**
  - **Ask**: every write, execute or external call needs approval.
  - **Auto-edit**: file edits inside the workspace are allowed; shell commands and external calls still ask.
  - **Full auto**: everything is allowed, and a confirmation is shown once.
- **Rules:** persisted allow and deny rules such as `shell(git status*)` or `mcp.github.*`, with an "Always allow" button on the approval card.
- **Binding:** the approval is tied to the exact tool name and arguments, using `FunctionApprovalRequestContent`. What runs is exactly what the user saw. This fixes the external-skill bug described above.
- File edits show a diff on the approval card.

### 4. MCP host

- Configured in `%AppData%/Kam/mcp.json` and in the project's `.mcp.json`. The format is the same `mcpServers` shape Claude Desktop and Claude Code use, so existing configs can be pasted in.
- Transports: stdio (`command`, `args`, `env`) and streamable HTTP or SSE (`url`, `headers`).
- Secrets are referenced as `${secret:NAME}` and resolved from the existing encrypted secret store.
- Servers start lazily and their tools are listed on demand. Each server shows its status in Integrations and can be restarted from there.
- Todoist becomes an ordinary MCP entry. GitHub can use the official GitHub MCP server or the built-in GitHub tools.

### 5. Agent Skills

- The format is Anthropic's open Agent Skills format: `SKILL.md` with a `name` and `description` in the frontmatter, plus optional `scripts/` and resource files.
- Only the name and description go into the system prompt. When a skill is relevant, the model calls `load_skill`, reads the body, and uses the normal tools to follow it. That includes running bundled scripts through the shell, subject to approval.
- Scopes:
  - user: `%AppData%/Kam/skills`
  - project: `.kam/skills`, with `.claude/skills` also read
  - plugin-provided skills
- Imports are copied into the user scope, so they survive a restart.

### 6. Plugins

- A bundle has a `plugin.json` manifest with name, version and description, plus any of: `skills/`, `agents/` (subagent definitions), `commands/` (slash commands), `.mcp.json` and `hooks/`.
- The layout is compatible with Claude Code plugins (`.claude-plugin/plugin.json`), so existing plugins install as they are.
- Plugins install from a folder, a git URL, or a `marketplace.json` catalogue. They can be enabled, disabled, updated and removed from the Skills page, which is renamed Plugins.

### 7. Coding mode

- The user picks a workspace folder per thread.
- `AGENTS.md`, `CLAUDE.md` or `KAM.md` from the workspace is loaded into the system prompt.
- **Tools:**
  - read, search (ripgrep-style) and glob
  - `apply_patch` and edit (unified diff)
  - shell with a timeout and output limit
  - git status, diff, log, commit, branch and worktree
  - run tests
- Before each edit, the touched files are snapshotted. "Rewind" restores any earlier point, and the diff of the whole session can be reviewed.

### 8. Subagents

- Defined as markdown files with frontmatter: `name`, `description`, `tools`, `model`, followed by the instructions.
- The main agent delegates through a `task` tool. Subagents run with their own context and tool subset, can run in parallel, and return a summary.
- Built-in subagents replace the dead AgentFactory agents: explorer (read-only), coder, researcher and desktop operator.

### 9. Integrations

- Each integration implements `IIntegration` with a status, connect and disconnect, a connection test, and the tools it provides (built-in or MCP).
- Settings reload live through `IOptionsMonitor` and the secret store, so a restart is no longer needed.
- The Integrations page lists every integration with the same card design, plus a "Add MCP server" form.

### 10. Hooks and memory (later)

- **Hooks:** `PreToolUse`, `PostToolUse` and `SessionStart` run local scripts. Plugins can ship hooks.
- **Memory:** user memory in `%AppData%/Kam/memory.md`, plus project memory. The agent can read it and append to it.

## Phase 1 as built

| Piece | Where |
|---|---|
| Loop, approval wait, result truncation | `Infrastructure/Agent/Runtime/AgentRuntime.cs` |
| System prompt | `Infrastructure/Agent/Runtime/AgentSystemPrompt.cs` |
| Built-in skills as tools (name, schema, risk) | `Infrastructure/Agent/Runtime/SkillToolProvider.cs` |
| Approval modes and "always allow" rules | `Infrastructure/Agent/Runtime/ToolPermissionService.cs`, saved in `%AppData%/Kam/agent-permissions.json` |
| Thread store | `Infrastructure/Agent/Runtime/JsonAgentSessionStore.cs` |
| Contracts | `Core/Interfaces/IAgentRuntime.cs`, `IAgentToolProvider.cs`, `IToolPermissionService.cs`, `IAgentSessionStore.cs`; events in `Core/Models/Agents/AgentEvent.cs` |
| Chat UI | `Ui/ViewModels/MainWindowViewModel.AgentRuntime.cs`, `AgentChatMessageViewModel.cs` |

- Skill ids become tool names by replacing characters providers reject, so `files.read_lines` is `files_read_lines`.
- Risk comes from the skill's permissions: process launch or control and `shell.run` are Execute, file or clipboard writes are Write, high-risk `communication.*` is External, network-only is Network, everything else is Read.
- Ask mode runs Read and Network tools without asking. Auto-edit adds Write. Full auto runs everything.
- Tool calls still go through `ISkillExecutionPipeline`, so skill policy, granted permissions, argument validation, timeouts and execution history apply as before.
- Chat uses the model in `AIService:Chat` when it is configured, otherwise the root `AIService` model.
- `AgentRuntime:Enabled` set to `false` in configuration sends chat back to the legacy single-skill planner. Voice commands still use the legacy path until phase 2.

## Delivery plan

| Phase | Scope | Result |
|---|---|---|
| 1 | Agent loop, built-in tools adapted to `AITool`, approval gate, streaming chat, session store | Multi-step agent in chat replaces the skill planner (planner kept behind a setting for one release) |
| 2 | MCP host (stdio + http, `mcp.json`), Agent Skills with progressive disclosure, plugin install | Users can add any MCP server or Claude-style plugin; Todoist moves to MCP config |
| 3 | Coding mode (workspace, AGENTS.md, edit/patch, git, checkpoints), subagents | Codex/Claude Code style coding inside Kam |
| 4 | Integration abstraction with live reload, hooks, memory; delete AgentFactory legacy agents, `SmartAgentOrchestrator`, AutoGen and Semantic Kernel packages | One architecture, smaller dependency surface |

Each phase ships with tests and keeps the app buildable.
