# Production Live Readiness

This is the current local-production checklist for turning Kam into a usable workstation agent.

## Required Core

Kam needs one working planner model profile. Configure it from Settings > AI Runtime, then press Test Connection.

Recommended starting options:

- OpenAI: API key, provider OpenAI, model selected from the live catalog.
- OpenRouter: API key, provider OpenRouter, model selected from the live catalog.
- Ollama: no cloud API key, local Ollama server running at `http://localhost:11434/v1`.

For a production-style local smoke without the UI, the equivalent configuration keys are:

- `AIService:Provider`
- `AIService:ApiKey`
- `AIService:ModelId`
- `AIService:Endpoint`

The smoke gate also accepts the legacy spelling `AIService:EndPoint`.

## Recommended Core Add-Ons

- Chat / skill execution model: optional, but recommended for imported skills and richer task execution.
- Todoist MCP token: optional, needed only for Todoist task operations.
- HuggingFace API key: optional, needed only if cloud STT / language detection is preferred over local Whisper or Ollama.

## Optional Integrations

- SMTP email credentials: optional, only needed for email sending.
- Twilio credentials: optional, only needed for SMS sending.
- Google Custom Search key and search engine id: optional, only needed for the legacy Google-backed web research service.

## Local Live Gate

Run:

```powershell
.\scripts\local-production-smoke.ps1 -Configuration Release -Runtime win-x64 -RequireAiConfig
```

Then launch and manually verify:

- Settings > AI Runtime Test Connection shows a successful live model catalog check.
- Runtime Diagnostics > Refresh shows Core AI as ready and Planner Live Connection as verified.
- A simple command produces a planner trace and a normalized skill result.
- Skills page evals run and report health.
- Optional integrations are tested only when their credentials are intentionally configured.
