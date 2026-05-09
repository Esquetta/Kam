# Kam Coding Agent Remaining Commands Plan

Goal: complete the next safe coding-agent command surface without bypassing the existing workspace and shell policy layers.

Implemented scope:

- `/test`: runs a fixed `dotnet test` command with structured argv and a bounded timeout.
- `/review`: runs deterministic read-only git review checks.
- `/dependabot`: runs the fixed NuGet vulnerability audit and lists open Dependabot PRs when `gh` is available.
- `/github`: reports git remotes, PR status, and recent workflow runs through read-only commands.
- `/plugins`: summarizes registered skill/plugin health through `ISkillHealthService`.
- `/mcp`: reports configured MCP endpoint status without printing secrets.
- `/agents`: lists registered runtime agents plus coding role templates.
- `/worktree`: lists worktrees and prints a plan; creation is intentionally not wired in non-interactive mode.
- `/hooks`: lists configured hooks only; hook execution is intentionally not wired.

Safety rules:

- Do not execute free-form command strings from slash-command arguments.
- Use `ProcessStartInfo.ArgumentList` for all process execution.
- Keep GitHub and Dependabot commands read-only.
- Keep MCP, plugin, agent, hook, and worktree mutating operations advisory until an explicit confirmation path exists.
- Keep summary files inside the active workspace.
- Keep file-skill reads, listings, metadata probes, and searches scoped to the configured workspace.

Verification:

- `CodingAgentCommandTests` covers slash command routing, deterministic process argv, degraded `gh` behavior, summary path confinement, and non-runtime delegation.
- `FileSkillExecutorTests` covers outside-workspace denial for read, list, exists, info, search, content search, tree, workspace map, outline, create, and read-lines flows.
