# Kam Coding Agent Remaining Commands Plan

Goal: complete the next safe coding-agent command surface without bypassing the existing workspace and shell policy layers.

Implemented scope:

- `/test`: runs a fixed `dotnet test` command with structured argv and a bounded timeout.
- `/review`: runs deterministic read-only git review checks.
- `/dependabot`: runs the fixed NuGet vulnerability audit and lists open Dependabot PRs when `gh` is available.
- `/github`: reports git remotes, PR status, and recent workflow runs through read-only commands.
- `/github app`: reports configured GitHub App connection status and the recommended read-only repository permissions.
- `/github repos`: lists repositories visible to the configured GitHub App installation.
- `/plugins`: summarizes registered skill/plugin health through `ISkillHealthService`.
- `/mcp`: reports configured MCP endpoint status without printing secrets.
- `/agents`: lists registered runtime agents plus coding role templates.
- `/worktree`: lists worktrees and prints a plan; creation requires an explicit `/worktree add --execute <sibling-path> <branch>` confirmation and keeps the target under the workspace parent.
- `/hooks`: lists configured hooks only; hook execution is intentionally not wired.

Safety rules:

- Do not execute free-form command strings from slash-command arguments.
- Use `ProcessStartInfo.ArgumentList` for all process execution.
- Keep GitHub and Dependabot commands read-only.
- Keep GitHub App credentials in user secrets or environment variables; never persist PEM content, JWTs, installation tokens, or private key paths in UI settings or command output.
- Keep MCP, plugin, agent, and hook mutating operations advisory until an explicit confirmation path exists.
- Keep worktree creation gated behind the explicit `--execute` flag and validated sibling-path boundaries.
- Keep summary files inside the active workspace.
- Keep file-skill reads, listings, metadata probes, and searches scoped to the configured workspace.

Verification:

- `CodingAgentCommandTests` covers slash command routing, deterministic process argv, degraded `gh` behavior, summary path confinement, and non-runtime delegation.
- `GitHubAppInstallationClientTests` covers missing-config degradation, installation-token repository listing, JWT issuer shape, and private-key path redaction.
- `FileSkillExecutorTests` covers outside-workspace denial for read, list, exists, info, search, content search, tree, workspace map, outline, create, and read-lines flows.
