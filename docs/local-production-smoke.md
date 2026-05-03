# Local Production Smoke

This runbook is the local gate before a hands-on production-style test of Kam.

## Command

```powershell
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -ReleaseCandidate rc-local-20260503
```

Useful variants:

```powershell
.\scripts\local-production-smoke.ps1 -PlanOnly -ReleaseCandidate rc-plan-20260503 -AllowDirtyWorktree
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -ReleaseCandidate rc-local-20260503 -Launch
```

## What It Checks

- Restores `Kam.sln`.
- Builds the solution in `Release`.
- Runs the test project in `Release`.
- Records release evidence:
  - concrete release candidate id
  - current git commit
  - tracked worktree state
  - smoke `summary.md` path
  - skill smoke `skill-smoke.md` path
  - published `SmartVoiceAgent.Ui.exe` path
- Checks required AI planner configuration without printing secret values. The check accepts either `dotnet user-secrets` / environment configuration or the enabled planner profile saved from the Settings screen:
  - `AIService:ApiKey`
  - `AIService:ModelId`
  - `AIService:Endpoint` / `AIService:EndPoint`
- Publishes the Avalonia UI to `artifacts/local-production-smoke/<timestamp>/publish`.
- Writes a smoke summary to `artifacts/local-production-smoke/<timestamp>/summary.md`.

## Manual Smoke After Launch

1. Open the published app with `-Launch`.
2. Confirm Settings can select provider/model profile without exposing API key values.
3. Submit a simple command that maps to a built-in skill, such as listing apps or reading a small file.
4. Confirm the right panel shows:
   - `PLAN_TRACE` with raw model response and parse status.
   - `SKILL_RESULTS` with normalized execution result.
5. Open Skills and confirm health cards show recent success rate, average duration, last failure detail, and execution history.
6. Open Runtime Diagnostics and use the Live Production Test panel as the manual gate:
   - `READY_FOR_LIVE_TEST` means core AI, live model connection, host, skill smoke, and command-loop evidence are ready.
   - `NEEDS_ACTION` means follow the panel's next action before treating the local session as production-ready.
   - Skill Smoke includes bounded active-window, visible-window, and accessibility-tree checks for the visual context path.
7. Use Copy Report from Runtime Diagnostics when sharing a local readiness snapshot. The report is intentionally sanitized and does not include API keys or endpoint values.
8. Run smoke evals from the Skills screen.
9. Record the generated `summary.md`, `skill-smoke.md`, and sanitized readiness report paths in `docs/release-candidate-checklist.md`.

## Notes

- `-ReleaseCandidate` must be a concrete identifier such as `rc-local-20260502`, a commit-based CI id, or a build number. Ambiguous values such as `latest` are rejected.
- Tracked local changes are rejected by default because release evidence must map to a reproducible source state. Use `-AllowDirtyWorktree` only for explicit non-release validation while developing the gate itself.
- Warnings from legacy nullable/XML-doc/platform analyzers can still appear during build. Treat command exit code and new test failures as the release gate.
- Use `-RequireAiConfig` for a real production-style run. Without it, missing AI keys are reported as warnings so build-only checks can still run on clean machines.
