# Kam Release Candidate Checklist

Use this checklist for every local release candidate before tagging or publishing a build. The gate assumes Kam is a local desktop AI agent with a skill-first runtime, model-flexible planning, and a Windows-first production target.

## Release Candidate

- Candidate id: `rc-local-20260503`
- Source branch: `master`
- Commit under rehearsal: `b15fc31c5fbc057cbab830353fdb1b912aa66430`
- Smoke summary: `artifacts/local-production-smoke/20260503-113220/summary.md`
- Skill smoke summary: `artifacts/local-production-smoke/20260503-113220/skill-smoke.md`
- Command smoke summary: pending latest release smoke
- Sanitized readiness report: `artifacts/local-production-smoke/20260503-113220/readiness-report.md`
- Approval owner: pending manual approval
- Approval date: pending

Note: the current smoke evidence was captured while this checklist was being edited, so the smoke summary records `dirty tracked worktree allowed` for the docs-only changes. Run the same smoke command again after committing this checklist before creating a release tag.

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
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -ReleaseCandidate rc-local-20260503 -Launch
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

- Automated gate completed: yes, for rehearsal evidence above
- Manual UI gate completed: partial
- Sanitized readiness report reviewed: yes, copied from Runtime Diagnostics and saved to the artifact path above
- Release candidate approved for tag: no
- Approved tag: pending

Manual UI gate notes:

- Runtime Diagnostics opened successfully in the published app.
- Core AI was `Ready`, Planner Live Connection was `Verified`, Host was `Online`, and Skills summary was `62/62 healthy`.
- Plugins opened successfully and showed `38/38 smoke evals passing`; action icons were visible without obvious clipping.
- Settings opened in dark and light mode; API key was masked and Test Connection was aligned.
- Copy Report produced a sanitized readiness report.
- Remaining manual blocker: the previous command input automation did not submit a planner-backed command in this desktop session. The automated gate now has a headless command smoke; rerun the gate and repeat one UI command before tagging.
