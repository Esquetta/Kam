# Kam Release Candidate Checklist

Use this checklist for every local release candidate before tagging or publishing a build. The gate assumes Kam is a local desktop AI agent with a skill-first runtime, model-flexible planning, and a Windows-first production target.

## Release Candidate

- Candidate id: `rc-local-20260508`
- Source branch: `master`
- Commit under rehearsal: `93df389a9c80d35bf7bf7f26f1838be067b530d8`
- Smoke summary: `artifacts/local-production-smoke/20260508-192713/summary.md`
- Skill smoke summary: `artifacts/local-production-smoke/20260508-192713/skill-smoke.md`
- Command smoke summary: `artifacts/local-production-smoke/20260508-192713/command-smoke.md`
- Sanitized readiness report: `artifacts/local-production-smoke/20260508-192713/readiness-report.md`
- Approval owner: pending manual approval
- Approval date: pending

Note: the latest smoke evidence was captured from a clean tracked worktree before this checklist update.

## Required Core Configuration

- Planner profile is enabled in Settings > AI Runtime.
- Planner provider is one of OpenAI, OpenRouter, or Ollama.
- Planner model is selected from the provider catalog, not manually typed when the catalog is available.
- Planner API key is present for cloud providers and hidden in the UI.
- Planner endpoint value is not shown in normal settings UI.
- Optional Chat / Skill Execution profile is configured when imported skills need a separate model.

Equivalent local smoke keys:

```powershell
AIService:Provider
AIService:ApiKey
AIService:ModelId
AIService:Endpoint
```

The gate also accepts `AIService:EndPoint` for older local configuration.

## Automated Gate

Run from the repository root:

```powershell
dotnet restore Kam.sln
dotnet build Kam.sln --configuration Release --no-restore --no-incremental
dotnet test Kam.sln --configuration Release
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -ReleaseCandidate rc-local-20260508 -Launch
git diff --check
```

Required result:

- Restore succeeds.
- Release build succeeds with `0 Warning(s)` and `0 Error(s)`.
- All tests pass.
- Local production smoke writes `summary.md`.
- Skill smoke writes `skill-smoke.md` and all required cases pass.
- Command smoke writes `command-smoke.md` and the headless `list applications` command succeeds without confirmation.
- Publish creates `SmartVoiceAgent.Ui.exe`.
- Launch starts a responding `SmartVoiceAgent.Ui` process.
- `git diff --check` reports no whitespace errors.

## Manual UI Gate

Complete this against the launched published app:

- Command Center: no legacy branding, no clipped text, no overlapping controls.
- Settings: provider/model selection works; API key values are masked; Test Connection is aligned and reports a clear result.
- Runtime Diagnostics: Refresh shows Core AI and Planner Live Connection state clearly.
- Runtime Diagnostics: Run Skill Smoke completes and shows pass/fail evidence.
- Runtime Diagnostics: Live Production Test shows `READY_FOR_LIVE_TEST` before tagging.
- Runtime Diagnostics: Copy Report produces a support report with no API key, bearer token, password, endpoint, or secret value.
- Plugins: cards and icon buttons are aligned in dark and light mode; health/eval actions are visible.
- Activity panel: `ACTIVITY_LOG`, `PLAN_TRACE`, and `SKILL_RESULTS` are visible; `KERNEL_LOG`, `PLANNER_TRACE`, and `RESULT_VIEWER` are absent.
- Theme toggle: dark and light mode remain readable with no low-contrast card metadata.
- Command loop: confirm `command-smoke.md` exists, then submit a simple UI command and confirm it produces both planner trace evidence and normalized skill result evidence.

## Optional Integration Gate

Run only when credentials are intentionally configured:

- Todoist MCP: create or list a safe test task, then clean up if needed.
- SMTP email: send a test message only to an approved inbox.
- Twilio SMS: optional; skip unless Twilio credentials and a test number are approved.
- HuggingFace STT/language detection: validate only when cloud speech features are enabled.
- Google Custom Search: validate only when legacy Google-backed web research is enabled.

## Known Non-Blockers

- Optional integrations can be unconfigured when the core desktop agent path is ready.
- Twilio is not required for production readiness.
- Ollama can be used without an API key when the local server is running.
- Imported local skills may require user approval for sensitive file, shell, clipboard, or desktop actions.
- Manual release approval is still required before creating a git tag.

## Approval

- Automated gate completed: yes, for `rc-local-20260508`
- Manual UI gate completed: partial
- Sanitized readiness report reviewed: yes, saved to the artifact path above
- Release candidate approved for tag: no
- Approved tag: pending

Manual UI gate notes:

- Full local production smoke completed from a clean tracked worktree.
- Release build completed with `0 Warning(s)` and `0 Error(s)`.
- Test suite passed: `573/573`.
- Skill smoke passed: `38/38`.
- Headless command smoke passed: `list applications` -> `apps.list`, no confirmation required.
- Publish created `SmartVoiceAgent.Ui.exe` and launched process `32264`; Windows reported `Responding=True`.
- Command Center opened from the published artifact and showed `ACTIVITY_LOG`, `PLAN_TRACE`, and `SKILL_RESULTS`.
- UI command input accepted `list applications` and produced planner trace plus normalized `apps.list` skill result evidence.
- Remaining manual blocker before tagging: repeat Settings and Runtime Diagnostics page checks by hand, including Live Production Test `READY_FOR_LIVE_TEST` and Copy Report from the UI.
