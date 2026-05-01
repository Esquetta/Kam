# Contributing To Kam

Kam is moving toward a production-ready local desktop agent. Contributions should keep that direction intact: reliable skill execution, clear diagnostics, safe automation, and a polished Avalonia UI.

## Development Baseline

- Use .NET 9.
- Work from the repository root containing `Kam.sln`.
- Keep changes scoped to one product slice.
- Do not commit secrets, generated artifacts, `bin/`, `obj/`, `.vs/`, or local smoke output.

## Before Opening A Pull Request

Run:

```powershell
dotnet restore Kam.sln
dotnet build Kam.sln --configuration Release --no-restore --no-incremental
dotnet test Kam.sln --configuration Release
git diff --check
```

For release-facing changes, also run:

```powershell
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig
```

## Commit Style

Prefer concrete product-slice commits:

- `Harden live model readiness feedback`
- `Complete production skill smoke coverage`
- `Polish release-critical desktop UX`
- `Redact secrets from readiness evidence`

## Test Expectations

- Runtime and skill changes need focused unit tests plus relevant smoke coverage.
- UI changes need view-model tests, metadata tests, contrast checks, or XAML layout tests when practical.
- Release tooling changes need script metadata tests and a local smoke run.
- Security-sensitive changes need explicit negative tests.

## Product Principles

- Keep model-provider behavior explicit and observable.
- Prefer skill-first execution over provider-specific tool/function calling.
- Preserve cross-platform boundaries even when Windows is the first production target.
- Show readiness evidence in the UI instead of relying on logs alone.
- Never expose API keys, bearer tokens, passwords, or private endpoints in user-visible diagnostics.
