# Security Policy

Kam is a local desktop automation agent. It can inspect files, run bounded shell commands, read desktop context, interact with application state, and call model providers. Security work therefore focuses on two principles:

1. never expose secrets in logs, diagnostics, traces, or support reports;
2. never let model output bypass deterministic skill policy, validation, and confirmation.

## Supported Versions

Kam is pre-1.0 production-readiness software. Security fixes are applied to the `master` branch until release-candidate tags are introduced.

## Reporting A Vulnerability

Do not open a public issue for security vulnerabilities.

Email: security@esquetta.com

Include:

- affected version or commit hash;
- operating system;
- reproduction steps;
- expected impact;
- sanitized logs, screenshots, or readiness reports.

Remove API keys, bearer tokens, passwords, private endpoints, and local private paths before sharing evidence.

## Security Model

### Secrets

- API keys belong in the Settings UI, user secrets, or environment variables.
- API keys must not be committed to `appsettings.json`, docs, screenshots, issue reports, planner traces, or execution history.
- Runtime Diagnostics and Copy Report flows must redact API keys, bearer tokens, passwords, and provider credentials.

### Skill Execution

Kam uses a skill-first runtime:

- model output becomes a JSON skill plan;
- the skill id must exist in the registry;
- arguments are validated before execution;
- policy checks run before high-risk actions;
- confirmation is required where the skill policy demands it;
- normalized results and execution history are recorded.

Provider tool/function-calling behavior is not trusted as a safety boundary.

### File And Workspace Access

- File operations validate paths before access.
- Workspace operations are bounded by configured roots and request limits.
- Dangerous file writes should be previewable before execution.
- Recursive delete or broad mutation flows require explicit confirmation and policy approval.

### Shell Execution

- Shell skills are bounded by timeout, output length, working directory, and blocked-pattern policy.
- Destructive command patterns are blocked by deterministic checks.
- High-risk shell operations must not be replayable without a fresh confirmation path.

### Desktop Context

- Window, accessibility, and screen-context skills are read-oriented unless an explicit action skill is invoked.
- Desktop automation must keep user-visible evidence in Runtime Diagnostics or skill execution history.
- Future browser-control integrations must follow the same bounded skill policy.

### Network And Provider Access

- OpenAI-compatible provider endpoints are configured by the user.
- Endpoint and key values are not shown in normal UI status text.
- Web/page skills should block private-network and localhost access unless policy explicitly allows it.

## Developer Checklist

- [ ] Validate all skill arguments before execution.
- [ ] Add negative tests for blocked or unsafe inputs.
- [ ] Do not log API keys, bearer tokens, passwords, auth headers, or private endpoints.
- [ ] Keep safety decisions deterministic; do not ask the model whether an action is safe.
- [ ] Add or update smoke coverage for new production skills.
- [ ] Run `dotnet test Kam.sln --configuration Release`.
- [ ] For release-facing changes, run `.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig`.

## Useful Files

- `src/SmartVoiceAgent.CrossCuttingConcerns/Security/SecurityUtilities.cs`
- `src/SmartVoiceAgent.Infrastructure/Skills/Execution/SkillExecutionPipeline.cs`
- `src/SmartVoiceAgent.Infrastructure/Skills/Execution/SkillArgumentValidator.cs`
- `src/SmartVoiceAgent.Infrastructure/Skills/Policy/SkillRuntimePolicyOptions.cs`
- `src/SmartVoiceAgent.Infrastructure/Skills/Actions/SkillActionPermissionPolicy.cs`
- `src/SmartVoiceAgent.Infrastructure/Skills/BuiltIn/AgentTools/ShellSkillExecutor.cs`
- `src/SmartVoiceAgent.Infrastructure/Skills/BuiltIn/AgentTools/FileSkillExecutor.cs`
- `docs/production-live-readiness.md`
- `docs/local-production-smoke.md`

## Current Release Security Focus

- Redaction across logs, planner traces, execution history, and readiness reports.
- Full smoke coverage for required built-in skills.
- Deterministic planner parsing and skill validation.
- Clear Runtime Diagnostics evidence for model, host, command-loop, and skill-health readiness.
