# Kam Coding Agent MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the first usable coding-agent slice: a repo-scoped command surface, explicit workspace permissions, and safer shell/file defaults.

**Architecture:** Keep the existing skill-first runtime. Add a thin console coding command layer that configures a workspace root before DI is built, then delegates natural-language requests to `ICommandRuntimeService`. Enforce workspace policy through DI-created file tools and shell runtime options so the policy applies below the command surface.

**Tech Stack:** .NET 9, console host, existing skill runtime, xUnit/FluentAssertions.

---

### Task 1: Coding command surface

**Files:**
- Create: `src/SmartVoiceAgent.AgentHost.ConsoleApp/CodingAgentCommand.cs`
- Modify: `src/SmartVoiceAgent.AgentHost.ConsoleApp/Program.cs`
- Test: `tests/SmartVoiceAgent.Tests/AgentHost/CodingAgentCommandTests.cs`

- [ ] **Step 1: Add tests for `/help`, `/status`, `/permissions`, `/diff`, and runtime delegation**

Create tests that pass a temporary git workspace to the command, assert slash commands do not call `ICommandRuntimeService`, and assert plain text is delegated as the command text.

- [ ] **Step 2: Implement `CodingAgentCommand`**

Support `--coding-agent`, `--workspace`, `--command`, `--summary`, `/help`, `/status`, `/permissions`, `/diff`, `/review`, and `/test`. The first four should be implemented now; `/review` and `/test` should return explicit "not wired yet" text rather than pretending to run.

- [ ] **Step 3: Wire the command in `Program.cs`**

Pre-parse options before host construction, inject `CodingAgent:IsEnabled` and `CodingAgent:WorkspaceRoot` into configuration, then run `CodingAgentCommand` before the legacy interactive test menu.

### Task 2: Workspace policy foundation

**Files:**
- Create: `src/SmartVoiceAgent.Core/Models/CodingAgent/CodingAgentOptions.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/DependencyInjection/ServiceRegistration.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Policy/SkillRuntimePolicyOptions.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/DependencyInjection/SkillRuntimeRegistrationTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/BuiltIn/ShellSkillExecutorTests.cs`

- [ ] **Step 1: Add coding-agent options**

Define `CodingAgentOptions` with `SectionName`, `IsEnabled`, `WorkspaceRoot`, `ApprovalMode`, and `RequireShellAllowList`.

- [ ] **Step 2: Scope `FileAgentTools` when coding mode is enabled**

Change DI registration from open `FileAgentTools` to a factory that passes `WorkspaceRoot` only when `CodingAgent:IsEnabled=true`.

- [ ] **Step 3: Apply shell workspace policy to built-in `shell.run` manifest**

When the registry is created, set `shell.allowedWorkingDirectories` to the workspace root and set a new `shell.requireAllowedCommands=true` flag when coding mode requires a shell allowlist.

- [ ] **Step 4: Make shell allowlist fail closed when requested**

Extend `ShellSkillExecutor` so `shell.requireAllowedCommands=true` denies commands when `shell.allowedCommands` is empty.

### Task 3: Verification

**Files:**
- No new source files unless tests expose a small missing seam.

- [ ] **Step 1: Run targeted tests**

Run:

```powershell
dotnet test tests/SmartVoiceAgent.Tests/SmartVoiceAgent.Tests.csproj --filter "FullyQualifiedName~CodingAgentCommandTests|FullyQualifiedName~SkillRuntimeRegistrationTests|FullyQualifiedName~ShellSkillExecutorTests|FullyQualifiedName~FileSkillExecutorTests"
```

- [ ] **Step 2: Run build**

Run:

```powershell
dotnet build Kam.sln
```

- [ ] **Step 3: Inspect git status**

Confirm only the plan, coding-agent MVP files, and the pre-existing avatar/Dependabot files are modified.
