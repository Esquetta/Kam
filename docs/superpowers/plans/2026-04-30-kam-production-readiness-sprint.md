# Kam Production Readiness Sprint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Kam from local hardening into a production-ready workstation-agent release candidate with repeatable automated gates, live model validation, skill reliability evidence, safe desktop automation, and a manual live test checklist.

**Architecture:** Treat production readiness as a release system, not a single feature. The sprint tightens the existing .NET/Avalonia app around deterministic skill execution, model-provider health, readiness diagnostics, packaging, and CI parity with `scripts/local-production-smoke.ps1`.

**Tech Stack:** .NET 9, Avalonia UI, xUnit, FluentAssertions, GitHub Actions on `windows-latest`, `scripts/local-production-smoke.ps1`, OpenAI-compatible model providers, Kam skill runtime, Windows local publish.

---

## Current Baseline

- Repo root: the checkout directory containing `Kam.sln`
- Branch: `master`
- CI: `.github/workflows/dotnet.yml`
- Local release gate: `scripts/local-production-smoke.ps1`
- Existing release docs:
  - `docs/production-live-readiness.md`
  - `docs/local-production-smoke.md`
- Latest verified local gate before this plan:
  - `dotnet test Kam.sln --configuration Release`: 529 passing tests
  - `scripts/local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -Launch`: build warnings 0, tests 529/529, skill smoke 17/17, UI launched

## Sprint Length

10 working days. Keep commits small and push after each independently verified task group.

## Release Candidate Exit Gate

Every release-candidate build must pass all of these:

```powershell
dotnet restore Kam.sln
dotnet build Kam.sln --configuration Release --no-restore --no-incremental
dotnet test Kam.sln --configuration Release
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig
git diff --check
git status --short --branch
```

Manual release gate:

1. Launch the published app with `.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -Launch`.
2. Settings > AI Runtime: save one planner profile and verify Test Connection success.
3. Runtime Diagnostics > Refresh: Core AI and Planner Live Connection are ready.
4. Runtime Diagnostics > Run Skill Smoke: all smoke evals pass.
5. Run one simple command that produces planner trace plus normalized skill result.
6. Runtime Diagnostics > Live Production Test: shows `READY_FOR_LIVE_TEST`.
7. Runtime Diagnostics > Copy Report: copied report contains no API key, bearer token, password, or endpoint secret.
8. Plugins: cards show health, execution history, and eval status without visual clipping in dark and light mode.

---

## Task 1: CI Parity With Local Production Smoke

**Files:**
- Modify: `.github/workflows/dotnet.yml`
- Modify: `scripts/local-production-smoke.ps1`
- Test: `tests/SmartVoiceAgent.Tests/AgentHost/SkillSmokeCommandTests.cs`
- Test: add or extend `tests/SmartVoiceAgent.Tests/ReleaseChecks/LocalProductionSmokeScriptTests.cs`

- [x] **Step 1: Add script metadata tests**

Create `tests/SmartVoiceAgent.Tests/ReleaseChecks/LocalProductionSmokeScriptTests.cs` with assertions that the smoke script contains the release gates used by CI:

```csharp
using FluentAssertions;

namespace SmartVoiceAgent.Tests.Release;

public sealed class LocalProductionSmokeScriptTests
{
    [Fact]
    public void SmokeScript_ContainsReleaseCandidateGates()
    {
        var script = File.ReadAllText(FindRepoFile("scripts", "local-production-smoke.ps1"));

        script.Should().Contain("dotnet\", \"restore\"");
        script.Should().Contain("dotnet\", \"build\"");
        script.Should().Contain("dotnet\", \"test\"");
        script.Should().Contain("--skill-smoke");
        script.Should().Contain("dotnet\", \"publish\"");
        script.Should().Contain("MaxBuildWarnings");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from test output.");
    }
}
```

- [x] **Step 2: Run red test**

Run:

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter FullyQualifiedName~LocalProductionSmokeScriptTests
```

Expected before implementation: pass if current script already contains the gates, otherwise fail with the missing string.

- [x] **Step 3: Update CI to run the same release gates**

Change `.github/workflows/dotnet.yml` so the build job:

- restores `Kam.sln`
- builds in Release
- runs `dotnet test`
- runs `scripts/local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -SkipTests -SkipSkillSmoke -RequireAiConfig:$false`
- publishes the UI artifact

Do not require real API keys in CI. CI proves build/test/publish parity; local release smoke proves live model readiness.

- [x] **Step 4: Verify**

Run:

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter FullyQualifiedName~LocalProductionSmokeScriptTests
.\scripts\local-production-smoke.ps1 -PlanOnly
```

Expected:

- release metadata test passes
- plan output includes restore, build, tests, skill smoke, publish

- [x] **Step 5: Commit**

```powershell
git add .github\workflows\dotnet.yml scripts\local-production-smoke.ps1 tests\SmartVoiceAgent.Tests\ReleaseChecks\LocalProductionSmokeScriptTests.cs
git commit -m "Align CI with production smoke gates"
git push
```

## Task 2: Live Model Profile Readiness

**Files:**
- Modify: `src/Ui/SmartVoiceAgent.Ui/ViewModels/PageModels/SettingsViewModel.cs`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Services/ModelConnectionTestService.cs`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Services/OpenAiCompatibleModelCatalogService.cs`
- Test: `tests/SmartVoiceAgent.Tests/Ui/ViewModels/SettingsViewModelAiProfileTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Ui/Services/OpenAiCompatibleModelCatalogServiceTests.cs`

- [x] **Step 1: Add failing tests for explicit connection state**

Extend `SettingsViewModelAiProfileTests` with cases that prove the UI can distinguish:

- not tested
- testing
- success
- failure with sanitized error message

Expected assertions:

```csharp
viewModel.AiProfileStatus.Should().Contain("validated");
viewModel.AiProfileStatus.Should().NotContain("sk-");
viewModel.AiConnectionTestButtonText.Should().Be("Test Connection");
```

- [x] **Step 2: Run red test**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter FullyQualifiedName~SettingsViewModelAiProfileTests
```

- [x] **Step 3: Implement state normalization**

Update `SettingsViewModel` and `ModelConnectionTestService` so every provider returns a normalized result:

- `Provider`
- `ModelId`
- `Succeeded`
- `Message`
- `FailureCategory`
- `TestedAt`

Never expose endpoint values or API key fragments in user-visible messages.

- [x] **Step 4: Verify provider catalog refresh**

Run:

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SettingsViewModelAiProfileTests|FullyQualifiedName~OpenAiCompatibleModelCatalogServiceTests"
```

Expected: all targeted tests pass.

- [ ] **Step 5: Manual validation**

In the published app:

1. Settings > AI Runtime
2. Select OpenAI, OpenRouter, or Ollama
3. Refresh model list
4. Select model from catalog
5. Test Connection

Expected: status clearly shows success or an actionable sanitized failure.

Note: deferred to the release-candidate rehearsal because this step requires a stable foreground desktop session and a user-selected live profile.

- [x] **Step 6: Commit**

```powershell
git add src\Ui\SmartVoiceAgent.Ui\ViewModels\PageModels\SettingsViewModel.cs src\Ui\SmartVoiceAgent.Ui\Services\ModelConnectionTestService.cs src\Ui\SmartVoiceAgent.Ui\Services\OpenAiCompatibleModelCatalogService.cs tests\SmartVoiceAgent.Tests\Ui
git commit -m "Harden live model readiness feedback"
git push
```

## Task 3: Runtime Diagnostics As Release Source Of Truth

**Files:**
- Modify: `src/Ui/SmartVoiceAgent.Ui/ViewModels/PageModels/RuntimeDiagnosticsViewModel.cs`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Views/RuntimeDiagnosticsView.axaml`
- Test: `tests/SmartVoiceAgent.Tests/Ui/ViewModels/RuntimeDiagnosticsViewModelTests.cs`

- [x] **Step 1: Add failing readiness tests**

Extend `RuntimeDiagnosticsViewModelTests` to assert `READY_FOR_LIVE_TEST` only when all required evidence exists:

- AI profile validated
- agent host ready
- skill smoke summary passing
- latest planner trace ready
- latest skill result ready
- command loop ready

Expected core assertion:

```csharp
viewModel.LiveTestStatus.Should().Be("READY_FOR_LIVE_TEST");
viewModel.LiveTestNextAction.Should().Be("Run a production command loop smoke.");
```

The negative case must assert `NEEDS_ACTION` and a specific next action.

- [x] **Step 2: Run red test**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter FullyQualifiedName~RuntimeDiagnosticsViewModelTests
```

- [x] **Step 3: Implement deterministic readiness aggregation**

Update `RuntimeDiagnosticsViewModel` so the Live Production Test panel is derived from a single ordered checklist:

1. Core AI
2. Planner Live Connection
3. Agent Host
4. Skill Smoke
5. Planner Trace
6. Skill Result
7. Command Loop

The first failing item becomes the displayed next action.

- [x] **Step 4: Verify**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter FullyQualifiedName~RuntimeDiagnosticsViewModelTests
```

Expected: all diagnostics readiness tests pass.

- [x] **Step 5: Commit**

```powershell
git add src\Ui\SmartVoiceAgent.Ui\ViewModels\PageModels\RuntimeDiagnosticsViewModel.cs src\Ui\SmartVoiceAgent.Ui\Views\RuntimeDiagnosticsView.axaml tests\SmartVoiceAgent.Tests\Ui\ViewModels\RuntimeDiagnosticsViewModelTests.cs
git commit -m "Make runtime diagnostics the release readiness gate"
git push
```

## Task 4: Skill Catalog And Smoke Coverage Completion

**Files:**
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Evaluation/BuiltInSkillEvalCaseCatalog.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Health/SkillHealthService.cs`
- Modify: `src/Ui/SmartVoiceAgent.Ui/ViewModels/PageModels/PluginItem.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/Evaluation/BuiltInSkillEvalCaseCatalogTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/Health/SkillHealthServiceTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Ui/ViewModels/PluginsViewModelSkillHealthTests.cs`

- [x] **Step 1: Add missing-skill coverage test**

Add a test that every built-in skill manifest with `ExecutorType == "builtin"` has one smoke case unless the manifest explicitly marks smoke as not applicable.

Expected assertion:

```csharp
missingSmokeCases.Should().BeEmpty("every production skill needs a release smoke case or an explicit skip reason");
```

- [x] **Step 2: Run red test**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~BuiltInSkillEvalCaseCatalogTests|FullyQualifiedName~SkillHealthServiceTests|FullyQualifiedName~PluginsViewModelSkillHealthTests"
```

- [x] **Step 3: Add smoke cases or explicit skip reasons**

Update `BuiltInSkillEvalCaseCatalog` so safe built-ins have bounded smoke cases. For skills that need external credentials or destructive permissions, add explicit health metadata that the UI renders as optional or blocked, not silently missing.

- [x] **Step 4: Verify headless smoke**

```powershell
dotnet run --project src\SmartVoiceAgent.AgentHost.ConsoleApp --configuration Release -- --skill-smoke --summary artifacts\manual-skill-smoke.md
```

Expected:

- all required smoke cases pass
- optional credential-dependent skills are reported as optional, skipped, or blocked with clear reason

- [x] **Step 5: Commit**

```powershell
git add src\SmartVoiceAgent.Infrastructure\Skills tests\SmartVoiceAgent.Tests\Infrastructure\Skills src\Ui\SmartVoiceAgent.Ui\ViewModels\PageModels\PluginItem.cs tests\SmartVoiceAgent.Tests\Ui\ViewModels\PluginsViewModelSkillHealthTests.cs
git commit -m "Complete production skill smoke coverage"
git push
```

## Task 5: Planner And Command Loop Reliability

**Files:**
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Planning/ModelSkillPlannerService.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Planning/SkillPlanParser.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Services/SkillFirstCommandRuntimeService.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/Planning/ModelSkillPlannerServiceTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/Planning/SkillPlanParserTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Services/SkillFirstCommandRuntimeServiceTests.cs`

- [x] **Step 1: Add parser repair and failure tests**

Add tests for model responses with:

- plain JSON object
- fenced JSON
- leading explanatory text
- invalid JSON
- unknown skill id
- missing required argument

Expected:

```csharp
parseResult.Succeeded.Should().BeFalse();
parseResult.ErrorMessage.Should().Contain("unknown skill");
parseResult.SanitizedRawResponse.Should().NotContain("sk-");
```

- [x] **Step 2: Run red test**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~ModelSkillPlannerServiceTests|FullyQualifiedName~SkillPlanParserTests|FullyQualifiedName~SkillFirstCommandRuntimeServiceTests"
```

- [x] **Step 3: Implement planner guardrails**

Make the planner service contract explicit:

- prompt asks for JSON plan only
- parser accepts common JSON wrappers
- unknown skills fail before execution
- missing arguments fail before execution
- failure result still creates a planner trace
- no function/tool-calling dependency is required from the provider

- [x] **Step 4: Verify**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~ModelSkillPlannerServiceTests|FullyQualifiedName~SkillPlanParserTests|FullyQualifiedName~SkillFirstCommandRuntimeServiceTests"
```

- [x] **Step 5: Commit**

```powershell
git add src\SmartVoiceAgent.Infrastructure\Skills\Planning src\SmartVoiceAgent.Infrastructure\Services\SkillFirstCommandRuntimeService.cs tests\SmartVoiceAgent.Tests\Infrastructure\Skills\Planning tests\SmartVoiceAgent.Tests\Infrastructure\Services\SkillFirstCommandRuntimeServiceTests.cs
git commit -m "Harden JSON planner command loop"
git push
```

## Task 6: Desktop Automation Safety And Permissions

**Files:**
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Actions/SkillActionPermissionPolicy.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/BuiltIn/AgentTools/ShellSkillExecutor.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/BuiltIn/AgentTools/FileSkillExecutor.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Policy/SkillRuntimePolicyOptions.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/Actions/SkillActionPermissionPolicyTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/BuiltIn/ShellSkillExecutorTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/BuiltIn/FileSkillExecutorTests.cs`

- [ ] **Step 1: Add safety boundary tests**

Add tests for:

- shell destructive command blocked
- recursive delete requires explicit confirmation
- file read outside allowed root blocked
- previewable write produces diff before execution
- high-risk action cannot replay

- [ ] **Step 2: Run red test**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SkillActionPermissionPolicyTests|FullyQualifiedName~ShellSkillExecutorTests|FullyQualifiedName~FileSkillExecutorTests"
```

- [ ] **Step 3: Implement missing guardrails**

Keep the policy deterministic. Do not use model judgement for safety decisions.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SkillActionPermissionPolicyTests|FullyQualifiedName~ShellSkillExecutorTests|FullyQualifiedName~FileSkillExecutorTests"
```

- [ ] **Step 5: Commit**

```powershell
git add src\SmartVoiceAgent.Infrastructure\Skills\Actions src\SmartVoiceAgent.Infrastructure\Skills\BuiltIn\AgentTools src\SmartVoiceAgent.Infrastructure\Skills\Policy tests\SmartVoiceAgent.Tests\Infrastructure\Skills
git commit -m "Harden desktop skill safety boundaries"
git push
```

## Task 7: Packaging, Launch, And Local Release Artifact

**Files:**
- Modify: `scripts/local-production-smoke.ps1`
- Modify: `.github/workflows/dotnet.yml`
- Modify: `docs/local-production-smoke.md`
- Test: `tests/SmartVoiceAgent.Tests/Release/LocalProductionSmokeScriptTests.cs`

- [ ] **Step 1: Add release artifact assertions**

Extend `LocalProductionSmokeScriptTests` to assert the script writes:

- `summary.md`
- `skill-smoke.md`
- `publish\SmartVoiceAgent.Ui.exe`

- [ ] **Step 2: Run red test**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter FullyQualifiedName~LocalProductionSmokeScriptTests
```

- [ ] **Step 3: Update smoke summary**

Ensure `summary.md` records:

- build warning count
- test count
- skill smoke summary path
- publish directory
- launch process id when `-Launch` is used

- [ ] **Step 4: Verify live publish**

```powershell
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -Launch
```

Expected:

- build warnings 0
- all tests passing
- all required skill smoke cases passing
- published app starts and `Get-Process SmartVoiceAgent.Ui` shows `Responding=True`

- [ ] **Step 5: Commit**

```powershell
git add scripts\local-production-smoke.ps1 .github\workflows\dotnet.yml docs\local-production-smoke.md tests\SmartVoiceAgent.Tests\Release\LocalProductionSmokeScriptTests.cs
git commit -m "Finalize local release artifact smoke"
git push
```

## Task 8: Product UX Readiness Pass

**Files:**
- Modify: `src/Ui/SmartVoiceAgent.Ui/Views/MainWindow.axaml`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Views/SettingsView.axaml`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Views/RuntimeDiagnosticsView.axaml`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Views/PluginsView.axaml`
- Test: `tests/SmartVoiceAgent.Tests/Ui/MainWindowMetadataTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Ui/ThemeContrastTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Ui/PluginsViewIconLayoutTests.cs`

- [ ] **Step 1: Add UI metadata tests for release-critical copy**

Assert these labels exist:

- `ACTIVITY_LOG`
- `SKILL_RESULTS`
- `READY_FOR_LIVE_TEST`
- `Test Connection`

Assert legacy or unclear labels are absent:

- `KERNEL_LOG`
- `RESULT_VIEWER`
- visible API endpoint values
- visible API key fragments

- [ ] **Step 2: Run red test**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~MainWindowMetadataTests|FullyQualifiedName~ThemeContrastTests|FullyQualifiedName~PluginsViewIconLayoutTests"
```

- [ ] **Step 3: Fix UX issues**

Address only release-blocking UX:

- dark/light mode readability
- button alignment
- overflow or clipping
- model connection feedback
- readiness status clarity

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~MainWindowMetadataTests|FullyQualifiedName~ThemeContrastTests|FullyQualifiedName~PluginsViewIconLayoutTests"
```

- [ ] **Step 5: Manual visual smoke**

In the launched app, check:

- Coordinator
- Settings
- Runtime Diagnostics
- Plugins
- dark mode
- light mode

Expected: no clipped buttons, no overlapping text, no legacy branding, no exposed secrets.

- [ ] **Step 6: Commit**

```powershell
git add src\Ui\SmartVoiceAgent.Ui\Views tests\SmartVoiceAgent.Tests\Ui
git commit -m "Polish release-critical desktop UX"
git push
```

## Task 9: Secrets, Logs, And Support Report Hygiene

**Files:**
- Modify: `src/Ui/SmartVoiceAgent.Ui/ViewModels/PageModels/RuntimeDiagnosticsViewModel.cs`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Services/Concrete/UiLogService.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Planning/JsonSkillPlannerTraceStore.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/Skills/Execution/JsonSkillExecutionHistoryService.cs`
- Test: `tests/SmartVoiceAgent.Tests/Ui/ViewModels/RuntimeDiagnosticsViewModelTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/Planning/ModelSkillPlannerServiceTests.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/Execution/JsonSkillExecutionHistoryServiceTests.cs`

- [ ] **Step 1: Add redaction tests**

Add tests that inject these values into logs, traces, and execution history:

- `sk-test-secret`
- `Bearer abc123`
- `password=secret`
- `api_key=secret`

Expected:

```csharp
report.Should().NotContain("sk-test-secret");
report.Should().NotContain("Bearer abc123");
report.Should().Contain("[redacted]");
```

- [ ] **Step 2: Run red test**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~RuntimeDiagnosticsViewModelTests|FullyQualifiedName~JsonSkillExecutionHistoryServiceTests|FullyQualifiedName~ModelSkillPlannerServiceTests"
```

- [ ] **Step 3: Implement shared redaction helper if needed**

Use one helper for readiness reports, planner traces, and history output. Keep raw secret values out of persisted support artifacts.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~RuntimeDiagnosticsViewModelTests|FullyQualifiedName~JsonSkillExecutionHistoryServiceTests|FullyQualifiedName~ModelSkillPlannerServiceTests"
```

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "Redact secrets from readiness evidence"
git push
```

## Task 10: Release Candidate Rehearsal

**Files:**
- Modify: `docs/production-live-readiness.md`
- Modify: `docs/local-production-smoke.md`
- Create: `docs/release-candidate-checklist.md`

- [ ] **Step 1: Create release checklist**

Create `docs/release-candidate-checklist.md` with:

- required API/profile configuration
- automated commands
- manual UI checks
- optional integration checks
- known non-blockers
- release approval section

- [ ] **Step 2: Run full automated gate**

```powershell
dotnet restore Kam.sln
dotnet build Kam.sln --configuration Release --no-restore --no-incremental
dotnet test Kam.sln --configuration Release
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -Launch
git diff --check
```

Expected:

- build warnings 0
- all tests pass
- skill smoke all required cases pass
- app launches
- `Get-Process SmartVoiceAgent.Ui` reports a responding process

- [ ] **Step 3: Run manual gate**

Complete the manual release gate from this plan and paste the sanitized readiness report path into `docs/release-candidate-checklist.md`.

- [ ] **Step 4: Commit**

```powershell
git add docs\production-live-readiness.md docs\local-production-smoke.md docs\release-candidate-checklist.md
git commit -m "Document release candidate readiness gate"
git push
```

- [ ] **Step 5: Tag release candidate after approval**

After the user confirms the release candidate:

```powershell
git tag rc-2026-04-30
git push origin rc-2026-04-30
```

## Test Matrix

| Layer | Command | Required For RC |
| --- | --- | --- |
| Restore | `dotnet restore Kam.sln` | Yes |
| Build | `dotnet build Kam.sln --configuration Release --no-restore --no-incremental` | Yes, 0 warnings |
| Unit/integration/UI metadata | `dotnet test Kam.sln --configuration Release` | Yes |
| Headless skill smoke | `dotnet run --project src\SmartVoiceAgent.AgentHost.ConsoleApp --configuration Release -- --skill-smoke --summary artifacts\manual-skill-smoke.md` | Yes |
| Local production smoke | `.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig` | Yes |
| Launch smoke | `.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig -Launch` | Yes before manual test |
| CI | GitHub Actions `.NET CI` | Yes before tag |
| Manual UI | Settings, Runtime Diagnostics, Plugins, command loop | Yes |
| Optional integrations | Todoist, SMTP, Twilio, HuggingFace, Google Search | Only when credentials are configured |

## Required Credentials For Live RC

At least one core model path:

- OpenAI API key with a model selected from live catalog, or
- OpenRouter API key with a model selected from live catalog, or
- local Ollama running at `http://localhost:11434/v1`

Optional credentials:

- Todoist MCP token for Todoist task operations
- HuggingFace API key for cloud STT or language detection
- SMTP credentials for email sending
- Twilio credentials for SMS sending
- Google Custom Search key and search engine id for legacy web research

## Non-Blocking Future Work

These should not block the first production-ready Windows RC:

- macOS packaging and notarization
- browser-use style remote browser control
- full installer auto-update channel
- paid telemetry backend
- cloud sync

## Recommended Commit Groups

1. `Align CI with production smoke gates`
2. `Harden live model readiness feedback`
3. `Make runtime diagnostics the release readiness gate`
4. `Complete production skill smoke coverage`
5. `Harden JSON planner command loop`
6. `Harden desktop skill safety boundaries`
7. `Finalize local release artifact smoke`
8. `Polish release-critical desktop UX`
9. `Redact secrets from readiness evidence`
10. `Document release candidate readiness gate`

## Sprint Completion Definition

The sprint is complete only when:

- all task commits are pushed
- GitHub Actions is green on `master`
- local production smoke passes with required AI config
- app launch smoke produces a responding process
- Runtime Diagnostics reports `READY_FOR_LIVE_TEST`
- Plugins page reports required smoke evals passing
- release checklist is filled with the latest smoke summary path
- user approves tagging the release candidate
