# Kam Skill-First Runtime Design

## Purpose

Kam should evolve from a model-driven voice assistant into a cross-platform personal voice agent product. The immediate product problem is that model-native tool/function calling is not stable enough across OpenRouter models. The target architecture keeps flexible model choice, but moves action execution into a deterministic skill runtime owned by Kam.

The user-approved product direction is a hybrid assistant:

- Cross-platform by design, with Windows as the first fully supported platform and macOS prepared as the next platform.
- Avalonia remains the shared UI layer.
- Models plan and explain actions, but Kam validates and executes actions.
- Existing tools are revised into stable built-in skills.
- Users can import skills from skills.sh, skills.re, local folders, Claude skills, Codex skills, MCP servers, and local scripts.

## Current Repo Context

The current application already has useful foundations:

- `SmartVoiceAgent.Core`, `SmartVoiceAgent.Application`, `SmartVoiceAgent.Infrastructure`, and `SmartVoiceAgent.Ui` provide a Clean Architecture-style split.
- Avalonia UI has a Settings page and JSON settings storage.
- Infrastructure already contains voice capture, STT providers, intent detection, agent orchestration, application control, system control, web research, MCP/Todoist tooling, and several agent tool classes.
- `AIService` is used by the agent runtime, while intent detection and web research still use separate `OpenRouter:*` configuration paths. That split makes runtime model changes hard and makes product behavior inconsistent.
- `IChatClient` is currently registered as a singleton from configuration. That blocks clean runtime model/profile switching.
- Existing agent tools depend too much on model-native function calling, which is the source of instability.

## External Compatibility Notes

Kam should target the Agent Skills ecosystem rather than inventing an incompatible package shape. The relevant public conventions are:

- `SKILL.md` packages are directories containing instructions and optional `scripts/`, `references/`, and `assets/`.
- Skills use progressive disclosure: lightweight metadata is available for routing, while full instructions/resources load only when needed.
- skills.re describes the format as protocol-agnostic and portable across agents such as Claude Code, Codex, and Gemini.
- Claude/Codex skills should be imported as procedural packages, but Kam must still classify how they execute: instructions-only, MCP, built-in adapter, or local script.

References:

- skills.re documentation: https://skills.re/docs/intro
- Claude skills overview: https://claude.com/docs/skills/overview
- Claude custom skills guide: https://claude.com/docs/skills/how-to

## Product Architecture

Kam should be separated into three durable runtime layers.

### Assistant Runtime

The Assistant Runtime owns model/provider profiles, planning, clarification, and final user-facing responses.

Responsibilities:

- Load the active model profile for each role.
- Ask the model to produce a structured `SkillPlan`.
- Repair or reject malformed model output.
- Fall back to deterministic intent/pattern detection when model output is unusable.
- Ask a clarification question when confidence is too low or required arguments are missing.
- Never execute operating system actions directly.

The model output contract should look like:

```json
{
  "skillId": "apps.open",
  "arguments": {
    "applicationName": "Spotify"
  },
  "confidence": 0.91,
  "requiresConfirmation": false,
  "reasoning": "The user asked to open Spotify."
}
```

Native function calling can remain an optional optimization for providers/models that support it well, but execution authority remains with the Skill Runtime.

### Skill Runtime

The Skill Runtime is the single action execution layer.

Responsibilities:

- Maintain a registry of built-in, MCP, imported, and script-backed skills.
- Normalize imported skills into a Kam manifest.
- Validate input arguments before execution.
- Enforce permissions and confirmation rules.
- Execute built-in .NET skills, MCP calls, or local scripts through dedicated executors.
- Return a structured result envelope with status, message, data, timings, and errors.
- Track health, last run, success rate, and permission state per skill.

### Platform Runtime

The Platform Runtime hides Windows/macOS differences behind adapters.

The same skill id should work across platforms where supported. For example, `apps.open` remains stable while the executor delegates to:

- Windows adapter: Start Menu, registry, process scanner, shell execution.
- macOS adapter: `/Applications`, Launch Services, Spotlight metadata, `open` command, permission checks.

Platform adapters should cover:

- Application scan/open/close/status.
- System volume, WiFi, Bluetooth, power operations.
- Screen capture/context and accessibility permissions.
- Voice input/capture permissions.
- Feature availability and user-facing unsupported-platform messages.

## Skill Manifest

Every imported or built-in skill should normalize into a `KamSkillManifest`.

Required fields:

```json
{
  "id": "apps.open",
  "source": "builtin",
  "version": "1.0.0",
  "displayName": "Open Application",
  "description": "Opens an installed desktop application.",
  "triggers": ["open app", "launch application", "start program"],
  "examples": ["Open Spotify", "Chrome'u ac"],
  "executorType": "builtin",
  "permissions": ["process.launch"],
  "inputSchema": {
    "type": "object",
    "required": ["applicationName"],
    "properties": {
      "applicationName": {
        "type": "string",
        "minLength": 1,
        "maxLength": 120
      }
    }
  },
  "riskLevel": "high"
}
```

Recommended metadata:

- `author`
- `homepage`
- `checksum`
- `installedFrom`
- `installedAt`
- `lastReviewedAt`
- `reviewStatus`
- `supportedPlatforms`
- `dependencies`
- `mcpServer`
- `scriptEntrypoint`

## Skill Sources

### Built-In Skills

Built-in skills are trusted by default and enabled by default. They replace the current unstable tool classes with testable, deterministic execution units.

Initial built-in skill set:

- `apps.open`
- `apps.close`
- `apps.status`
- `apps.list`
- `system.volume`
- `system.power`
- `system.wifi`
- `system.bluetooth`
- `web.search`
- `web.open`
- `communication.email.send`
- `communication.sms.send`

Current tool migration:

- `SystemAgentTools` becomes `apps.*`, `system.*`, and `files.*` skills.
- `TaskAgentTools` becomes MCP-backed `tasks.todoist.*` skills.
- `WebSearchAgentTools` becomes `web.search`, `web.open`, and `web.summarize`.
- `CommunicationAgentTools` becomes `communication.email.*` and `communication.sms.*`.
- File operations should be isolated as separate high-risk skills with scoped filesystem permissions.

### MCP Skills

MCP is a first-class executor type. Todoist should stay MCP-backed because it is already the most stable integration.

MCP-backed skills:

- Start disabled until connection test passes.
- Show server endpoint/path and capabilities in the UI.
- Surface MCP errors as skill execution errors, not model failures.
- Support health checks.

### Imported Agent Skills

Kam supports importing `SKILL.md` folders from:

- skills.sh / skills.re packages.
- Local folders.
- Claude skill folders.
- Codex skill folders.

Import behavior:

- The package is parsed and normalized into a `KamSkillManifest`.
- The imported skill starts as `Disabled + Review Required`.
- The user sees source, author, checksum, instructions summary, scripts, dependencies, and requested permissions before enabling.
- Instructions-only skills can assist planning but cannot execute actions unless mapped to an executor.
- Skills with scripts are treated as script skills and require explicit permission review.

### Local Script Skills

Script skill support should use a practical permission-based model with strict gates for high-risk actions.

Supported script runtimes in later implementation phases:

- PowerShell
- Python
- Node.js

Script execution rules:

- Each script has a timeout.
- All stdout/stderr are captured.
- Working directory is scoped to the skill unless the user grants more access.
- Environment variables are limited and explicit.
- Secrets are passed only through secure references, not plain manifest values.
- Network access, process launch, broad filesystem access, clipboard write, screen control, and system settings require explicit permission.
- Critical actions can require confirmation every run or a "remember for this skill" approval.

## Security Model

The approved import policy is trust-first:

- Downloaded and local skills start disabled.
- Imported skills require review before activation.
- Built-in skills are enabled by default.
- MCP skills require a passing connection test before activation.
- Permissions can be revoked later.

Risk levels:

- `low`: instructions-only, formatting, local read-only reference use.
- `medium`: limited network or limited filesystem write.
- `high`: process launch, app control, clipboard write, system setting changes, screen read/control.
- `critical`: arbitrary shell command, destructive file operation, broad filesystem access, unrestricted script execution.

Execution gate:

- Validate `SkillPlan` against the skill input schema.
- Check the skill is enabled and healthy enough to run.
- Check permissions.
- Ask for confirmation when risk or policy requires it.
- Execute through the matching executor.
- Persist execution history.

## Unified Model Provider Profiles

The current split between `AIService` and `OpenRouter:*` should be replaced with model provider profiles.

Profile shape:

```json
{
  "id": "openrouter-primary",
  "provider": "OpenRouter",
  "displayName": "OpenRouter Primary",
  "endpoint": "https://openrouter.ai/api/v1",
  "apiKeyRef": "secure-store://openrouter-primary",
  "modelId": "openai/gpt-4.1-mini",
  "roles": ["planner", "chat", "summarizer"],
  "temperature": 0.2,
  "maxTokens": 1200,
  "enabled": true
}
```

Supported provider direction:

- OpenRouter first in the UI.
- OpenAI-compatible provider abstraction for future OpenAI and custom endpoints.
- Ollama/local model support.
- Later Anthropic/Gemini profiles if desired.

Runtime requirements:

- `IChatClient` should stop being a singleton tied to startup configuration.
- Add `IModelProviderRegistry` and `IModelClientFactory`.
- Agent, intent detection, and web research should use the same model abstraction.
- Provider/profile changes should not require closing the application.
- Agent registry should rehydrate or refresh its clients when the active profile changes.
- API keys should move out of plain settings JSON when possible and use OS secure storage.

Model output reliability:

- Prefer low-temperature planner role settings.
- Require strict JSON for `SkillPlan`.
- Add JSON repair.
- Add schema validation.
- Add fallback parser/pattern matching.
- Add clarification when arguments are missing.

## Settings and Product UX

### AI Runtime Panel

The Settings UI should include:

- Provider selector.
- Endpoint field.
- API key input with masked value.
- Model id input or preset selector.
- Role assignment: planner, chat, summarizer.
- Test connection button.
- Current health state: connected, missing key, invalid key, rate limited, slow, disabled.
- Last error and latency.

### Skill Manager

The existing plugin/settings surfaces should evolve into a product-grade Skill Manager.

Skill Manager should show:

- Built-in, MCP, imported, and script skills.
- Enabled/disabled state.
- Review required state.
- Permissions.
- Risk level.
- Last run.
- Success rate.
- Last error.
- Test button.
- Disable/revoke permission actions.

### Execution Feedback

Voice and text command execution should show:

- Planned skill.
- Required confirmation when relevant.
- Execution start.
- Result or failure.
- Which provider produced the plan.
- Which skill executor ran the action.

## Roadmap

### Phase 1: Unified AI Settings and Planner Contract

Goals:

- Replace split model configuration with provider profiles.
- Add runtime model/profile selection.
- Add planner JSON contract.
- Keep current user-facing behavior working.

Deliverables:

- `ModelProviderProfile` model.
- `IModelProviderRegistry`.
- `IModelClientFactory`.
- Settings persistence for model profiles.
- UI fields for provider/API key/model.
- Connection test.
- Refactor agent, intent, and web research to use the shared abstraction.
- Tests for profile validation and planner output parsing.

### Phase 2: Minimum Built-In Skill Runtime

Goals:

- Prove deterministic skill execution with a small, high-value skill set.
- Reduce dependence on model-native function calling.

Deliverables:

- `KamSkillManifest`.
- `SkillPlan`.
- `SkillResult`.
- `ISkillRegistry`.
- `ISkillExecutor`.
- Built-in `apps.open`, `apps.status`, `apps.list`.
- Permission metadata and validation.
- Tests for schema validation, skill lookup, and execution result handling.

### Phase 3: Built-In Tool Migration

Goals:

- Convert current unstable tool classes into stable skills.

Deliverables:

- `system.volume`.
- `system.power`.
- `system.wifi`.
- `system.bluetooth`.
- `web.search`.
- `web.open`.
- `communication.email.send`.
- `communication.sms.send`.
- `tasks.todoist.*` as MCP-backed skills.
- Result envelopes and health checks.

### Phase 4: Skill Manager UI

Goals:

- Make skill status, risk, permissions, and errors visible to users.

Deliverables:

- Skill list UI.
- Review details panel.
- Enable/disable controls.
- Permission grant/revoke controls.
- Last run and error display.
- Test skill action.

### Phase 5: User Skill Import

Goals:

- Import external `SKILL.md` packages safely.

Deliverables:

- Local folder import.
- skills.sh/skills.re import flow.
- Claude/Codex skill folder import.
- Manifest normalization.
- Review-required state.
- MCP executor mapping.
- Script executor mapping.
- Script timeout/logging/permission enforcement.

### Phase 6: Cross-Platform Adapter Hardening

Goals:

- Keep the skill contract stable while preparing macOS support.

Deliverables:

- Platform capability registry.
- Windows app/system adapters cleaned behind interfaces.
- macOS app scan/open/status adapter skeleton.
- macOS permission detection design.
- User-facing unsupported capability messages.

### Phase 7: Product Hardening

Goals:

- Make Kam feel like a reliable product rather than a demo.

Deliverables:

- Onboarding wizard.
- Provider and skill health dashboard.
- Execution history.
- Export/import settings.
- Backup and recovery for skill registry.
- Packaging/release polish.

## First Implementation Slice

The first implementation slice should combine Phase 1 with the smallest useful part of Phase 2:

- Unified model provider profiles.
- Runtime provider/model/API key UI.
- Planner `SkillPlan` contract.
- Built-in skill registry.
- Built-in `apps.open`, `apps.status`, and `apps.list`.

This directly addresses the current instability while keeping scope controlled. It also creates the foundation for imported skills and macOS adapters without forcing those features into the first code slice.

## Acceptance Criteria For The First Slice

- User can configure an OpenRouter-compatible profile from the UI.
- User can test the configured provider/model from the UI.
- API key is masked in the UI and not logged.
- Existing startup does not crash when no key is configured; AI-dependent actions show actionable status.
- Planner produces or is normalized into `SkillPlan`.
- Invalid planner JSON falls back to deterministic parsing or clarification.
- `apps.open`, `apps.status`, and `apps.list` execute through the skill runtime.
- Skill execution returns a structured result.
- Tests cover profile validation, planner parsing, skill registry lookup, permission checks, and built-in app skill execution with fakes.
- `dotnet build` and the relevant test suite pass.

