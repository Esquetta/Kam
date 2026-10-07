# Smart Voice Agent (KAM Neural Core) - Agent Guide

> **For AI Coding Agents**: This document provides essential information about the project structure, architecture, and development conventions. Read this first before making any changes.

## Project Overview

**Smart Voice Agent** (also known as KAM Neural Core) is an advanced AI-powered voice assistant with a tool-calling agent, system control, and intelligent task management capabilities. It supports voice recognition, natural language processing, and can control system applications and devices.

### Key Features
- **Voice**: push to talk, a local "Hey Kam" wake phrase and spoken replies; speech-to-text with local Whisper, an OpenAI-compatible API or HuggingFace
- **Languages**: English and Turkish interface
- **Agent Runtime**: one tool-calling agent loop over built-in skills, MCP servers, Agent Skills and plugins
- **System Control**: Application management, device control (volume, brightness, WiFi, Bluetooth), power management
- **Task Management**: Todoist integration via MCP (Model Context Protocol)

---

## Technology Stack

| Category | Technologies |
|----------|-------------|
| **Framework** | .NET 9.0 |
| **UI Framework** | Avalonia UI 12.0.3 with ReactiveUI (cross-platform desktop) |
| **AI/ML** | Microsoft.Extensions.AI 10.5 (OpenAI-compatible and Anthropic chat clients) |
| **MCP** | ModelContextProtocol 1.4.0 |
| **CQRS** | Cortex.Mediator 3.1.2 |
| **Validation** | FluentValidation 12.1.1 |
| **Audio** | NAudio 2.2.1, Whisper.net 1.9.0 (CPU and Vulkan runtimes) |
| **OCR** | Tesseract 5.2.0 |
| **Logging** | Serilog 4.3 with a local file sink; MongoDB when configured |
| **Testing** | xUnit 2.9.3, Moq 4.20.72, FluentAssertions 8.8.0 |
| **Benchmarking** | BenchmarkDotNet 0.15.8 |

---

## Project Structure

The solution follows **Clean Architecture** with clear layer separation:

```
Kam.sln
├── src/
│   ├── SmartVoiceAgent.Core/               # Domain layer
│   ├── SmartVoiceAgent.Application/        # Application layer (CQRS)
│   ├── SmartVoiceAgent.Infrastructure/     # Infrastructure layer
│   ├── SmartVoiceAgent.CrossCuttingConcerns/ # Logging, exceptions
│   ├── SmartVoiceAgent.AgentHost.ConsoleApp/ # Console entry point
│   ├── SmartVoiceAgent.Benchmarks/         # Performance benchmarks
│   ├── SmartVoiceAgent.Mailing/            # Email/SMS services
│   └── Ui/SmartVoiceAgent.Ui/              # Avalonia desktop UI
├── tests/
│   └── SmartVoiceAgent.Tests/              # Unit & integration tests
└── assets/                                  # Images, icons, resources
```

### Layer Details

#### 1. SmartVoiceAgent.Core (Domain Layer)
- **Purpose**: Domain entities, interfaces, DTOs, enums, models
- **Dependencies**: Minimal (Cortex.Mediator, Microsoft.Extensions.AI.Abstractions)
- **Key Folders**:
  - `Entities/` - Domain entities (CommandResult, AppInfo, etc.)
  - `Dtos/` - Data transfer objects
  - `Interfaces/` - Service interfaces (defined here, implemented in Infrastructure)
  - `Enums/` - Enumeration types
  - `Models/` - Domain models (IntentResult, ConversationContext, etc.)
  - `Config/` - Configuration classes
  - `Contracts/` - ICommand, IQuery interfaces

#### 2. SmartVoiceAgent.Application (Application Layer)
- **Purpose**: CQRS commands, queries, handlers, pipelines, validators
- **Dependencies**: Core, CrossCuttingConcerns, Cortex.Mediator, FluentValidation
- **Key Folders**:
  - `Commands/` - Command records (e.g., `OpenApplicationCommand`)
  - `Handlers/CommandHandlers/` - Command handlers
  - `Handlers/QueryHandlers/` - Query handlers
  - `Pipelines/` - Cortex.Mediator command/query pipeline behaviors
    - `Caching/` - Request caching behavior
    - `Logging/` - Request logging behavior
    - `Performance/` - Performance monitoring behavior
    - `Validation/` - FluentValidation behavior
  - `Validators/` - FluentValidation validators
  - `Notifications/` - Cortex.Mediator notifications
  - `NotificationHandlers/` - Notification handlers

#### 3. SmartVoiceAgent.Infrastructure (Infrastructure Layer)
- **Purpose**: External services, AI agents, platform-specific implementations
- **Dependencies**: Core, Application
- **Key Folders**:
  - `Agent/` - Agent runtime
    - `Runtime/` - Agent loop, tool permissions, sessions and context window
    - `Mcp/` - MCP host and server sources
    - `Extensions/` - Agent Skills, plugins and Markdown commands
    - `Agents/` - Task subagents (`RuntimeAgentFactory`)
    - `Tools/` - Tool classes behind the built-in skills (SystemAgentTools, FileAgentTools, WebSearchAgentTools)
  - `Services/` - Service implementations
  - `Helpers/` - Helper classes (CircularAudioBuffer, AudioProcessingService)
  - `Factories/` - Factory pattern implementations
  - `DependencyInjection/` - Service registration

#### 4. SmartVoiceAgent.CrossCuttingConcerns
- **Purpose**: Logging infrastructure and exceptions
- **Dependencies**: Serilog, MongoDB.Driver
- **Key Components**:
  - `Logging/` - Serilog configuration and sinks
  - `Exceptions/` - Custom exception types

#### 5. SmartVoiceAgent.Ui (Presentation Layer)
- **Purpose**: Avalonia-based desktop UI
- **Dependencies**: Avalonia 12.0.3, ReactiveUI, Application, Infrastructure
- **Key Folders**:
  - `Views/` - XAML views (MainWindow.axaml, etc.)
  - `ViewModels/` - ViewModels (MVVM pattern)
  - `Services/` - UI-specific services (UiLogService, TrayIconService)
  - `Converters/` - XAML value converters

---

## Architecture Patterns

### 1. CQRS (Command Query Responsibility Segregation)
All business operations are implemented as commands or queries handled through Cortex.Mediator:

```csharp
// Command definition in Core layer
public record OpenApplicationCommand(string ApplicationName) 
    : ICommand<CommandResultDTO>, ICachableRequest, IIntervalRequest;

// Handler in Application layer
public sealed class OpenApplicationCommandHandler : 
    ICommandHandler<OpenApplicationCommand, CommandResultDTO>
{
    public async Task<CommandResultDTO> Handle(OpenApplicationCommand request, 
        CancellationToken cancellationToken)
    {
        // Implementation
    }
}
```

### 2. Pipeline Behaviors
Cross-cutting concerns are handled via Cortex.Mediator pipeline behaviors:

- **CachingBehavior** - Automatic caching for `ICachableRequest`
- **RequestValidationBehavior** - FluentValidation integration
- **PerformanceBehavior** - Performance logging for `IIntervalRequest`
- **LoggingBehavior** - Request/response logging

### 3. Factory Pattern for Platform-Specific Services
Platform-specific implementations use factory pattern:

```csharp
// Registration
services.AddSingleton<IApplicationServiceFactory, ApplicationServiceFactory>();
services.AddSingleton<IApplicationService>(sp => 
    sp.GetRequiredService<IApplicationServiceFactory>().Create());
```

### 4. Agent Runtime
Chat, voice and the tray's "New task" run through `IAgentRuntime`:

```
User Input → AgentRuntime → model ⇄ tools (IAgentToolProvider) → Response
                                      ├─ built-in skills (SkillToolProvider)
                                      ├─ MCP servers (McpToolProvider)
                                      └─ Agent Skills (AgentSkillToolProvider)
```

Every tool call passes `IToolPermissionService`. With `AgentRuntime:Enabled=false`, commands go through `ICommandRuntimeService` (the single-skill planner) instead.

---

## Build and Run Commands

### Prerequisites
- .NET 9.0 SDK or later
- Windows 10/11, Ubuntu 20.04+, or macOS 11+
- API keys stored in User Secrets (see Configuration section)

### Build Commands

```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Build in Release mode
dotnet build --configuration Release

# Run tests
dotnet test tests/SmartVoiceAgent.Tests

# Run benchmarks
dotnet run --project src/SmartVoiceAgent.Benchmarks --configuration Release

# Run console app
dotnet run --project src/SmartVoiceAgent.AgentHost.ConsoleApp

# Run UI (main application)
dotnet run --project src/Ui/SmartVoiceAgent.Ui
```

### Platform-Specific Notes
- **UI Project**: Targets `win-x64` runtime identifier by default
- **Unsafe Code**: Infrastructure project allows unsafe blocks for audio processing
- **Tesseract**: Requires `tessdata/eng.traineddata` for OCR

---

## Configuration

### User Secrets Configuration
API keys and sensitive configuration are stored in User Secrets (NOT in appsettings.json):

```bash
# Set User Secrets
dotnet user-secrets set "AIService:ApiKey" "your-api-key"
dotnet user-secrets set "AIService:Endpoint" "https://openrouter.ai/api/v1"
dotnet user-secrets set "AIService:ModelId" "microsoft/wizardlm-2-8x22b"
dotnet user-secrets set "Mcpverse:TodoistApiKey" "your-todoist-key"
```

### Required Configuration Sections

```json
{
  "AIService": {
    "Provider": "OpenRouter",
    "ApiKey": "",
    "Endpoint": "",
    "ModelId": ""
  },
  "VoiceRecognition": {
    "Provider": "HuggingFace",
    "SampleRate": 16000,
    "Channels": 1
  },
  "MongoDbConfiguration": {
    "ConnectionString": "mongodb://localhost:27017",
    "Database": "SmartVoiceAgentLogs"
  }
}
```

---

## Code Style Guidelines

### C# Conventions
- **File-scoped namespaces** are used throughout
- **Implicit usings** enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Nullable reference types** enabled (`<Nullable>enable</Nullable>`)
- **Records** for DTOs and commands: `public record OpenApplicationCommand(string ApplicationName)`
- **Primary constructors** where appropriate

### Naming Conventions
- Interfaces prefixed with `I`: `IApplicationService`
- Async methods suffixed with `Async`: `OpenApplicationAsync`
- Commands end with `Command`: `OpenApplicationCommand`
- Handlers end with `Handler`: `OpenApplicationCommandHandler`
- DTOs end with `DTO`: `CommandResultDTO`

### Documentation
- XML documentation comments required for public APIs
- `<summary>` tags for classes and methods
- `<param>` tags for method parameters

---

## Testing Strategy

### Test Project Structure
```
tests/SmartVoiceAgent.Tests/
├── Application/
│   ├── Handlers/          # Command handler tests
│   └── Pipelines/         # Pipeline behavior tests
├── Infrastructure/
│   ├── Helpers/           # Helper class tests
│   └── Services/          # Service tests
└── SecurityUtilitiesTests.cs
```

### Testing Framework
- **xUnit** for test framework
- **Moq** for mocking
- **FluentAssertions** for assertions

### Running Tests
```bash
# Run all tests
dotnet test tests/SmartVoiceAgent.Tests

# Run with verbose output
dotnet test tests/SmartVoiceAgent.Tests --verbosity normal

# Run specific test
dotnet test tests/SmartVoiceAgent.Tests --filter "FullyQualifiedName~PlayMusicCommand"
```

---

## Security Considerations

### SecurityUtilities Class
Located in `SmartVoiceAgent.Infrastructure/Security/SecurityUtilities.cs`. Shell commands on Linux/macOS go through `PosixShell.Run`, and every interpolated value must be wrapped with `PosixShell.Quote`:

```csharp
// Path validation - prevents path traversal
bool isSafe = SecurityUtilities.IsSafeFilePath(path, allowedBaseDir);

// Application name validation - prevents command injection
bool isSafe = SecurityUtilities.IsSafeApplicationName(appName);

// URL validation - prevents open redirect
bool isSafe = SecurityUtilities.IsSafeUrl(url);

// Data masking for logging
string masked = SecurityUtilities.MaskSensitiveData(apiKey);
```

### Security Measures
1. **Path Traversal Protection**: Validates file paths, blocks `..`, `//`, URL-encoded traversal
2. **Command Injection Prevention**: Blocks dangerous characters (`;`, `|`, `&`, `>`, `<`, etc.)
3. **Sensitive Data Protection**: API keys never logged, authorization headers masked
4. **URL Validation**: Only `http://` and `https://` protocols allowed
5. **File Extension Validation**: Dangerous extensions (`.exe`, `.bat`, `.ps1`) blocked

---

## Responsive Design

The UI implements responsive design with three breakpoints:

| Breakpoint | Width | Behavior |
|------------|-------|----------|
| Compact | < 1024px | Single column, log panel hidden |
| Medium | 1024-1440px | Two columns, 320px log panel |
| Expanded | > 1440px | Three columns, 450px log panel |

Key files:
- `WindowStateManager.cs` - Tracks window state
- `ResponsiveConverters.cs` - XAML converters for responsive bindings
- Accessibility support with `ReducedMotion` preference

See `RESPONSIVE_DESIGN.md` for full details.

---

## Key Interfaces for Extension

### Adding a New Command
1. Create command record in `SmartVoiceAgent.Core/Commands/`
2. Create handler in `SmartVoiceAgent.Application/Handlers/CommandHandlers/`
3. Create validator in `SmartVoiceAgent.Application/Validators/` (optional)
4. Add notification if needed

### Adding a New Service
1. Define interface in `SmartVoiceAgent.Core/Interfaces/`
2. Implement in `SmartVoiceAgent.Infrastructure/Services/`
3. Register in `ServiceRegistration.cs`
4. Add tests in `tests/SmartVoiceAgent.Tests/`

### Adding a New Agent Tool
1. Add a built-in skill: a manifest in `BuiltInSkillManifestCatalog` and an `ISkillExecutor`; `SkillToolProvider` exposes it to the model
2. Or implement `IAgentToolProvider` and register it as scoped in `ServiceCollectionExtensions.cs`

---

## Troubleshooting

### Common Issues

**Voice Recognition Not Working**
- Check microphone permissions (Windows: Settings → Privacy → Microphone) and the microphone picked in Settings > Voice
- Whisper models download to `%LocalAppData%/Kam/Models` on first use; the composer shows download progress and failures
- The activity log has `VOICE_PROBLEM:` lines with the technical reason

**API Connection Issues**
- Verify API keys in User Secrets: `dotnet user-secrets list`
- Check network connectivity
- Review logs in `%LocalAppData%/Kam/Logs` (MongoDB when `MongoDbConfiguration:ConnectionString` is set)

**Build Errors**
- Ensure .NET 9.0 SDK is installed: `dotnet --version`
- Restore packages: `dotnet restore`
- Clean and rebuild: `dotnet clean && dotnet build`

---

## Additional Resources

- `README.md` - User-facing documentation
- `SECURITY.md` - Security policy and vulnerability reporting
- `RESPONSIVE_DESIGN.md` - UI responsive design documentation
- `Test.html` - Concept design HTML file

---

*Last Updated: 2026-10-06*

## Current Project Status

### Recent Improvements (January 2026)

#### Performance Optimizations
- **VoiceRecognitionServiceBase**: ArrayPool integration, Span<T>, removed forced GC
- **CircularAudioBuffer**: Bulk memory operations with Span.CopyTo/Buffer.BlockCopy  
- **DynamicAppExtractionService**: Optimized Levenshtein distance (two-row algorithm)
- **PerformanceBehavior**: Fixed thread-safety bug (shared Stopwatch → per-request)

#### Agent Runtime (October 2026)
- **Chat runs a tool-calling agent loop**: `IAgentRuntime` (`Infrastructure/Agent/Runtime/AgentRuntime.cs`) streams `AgentEvent`s; the model calls built-in skills natively through `SkillToolProvider`, and every call passes `IToolPermissionService` (Ask / Auto-edit / Full auto plus "always allow" rules)
- **Threads persist** as `%AppData%/Kam/sessions/<id>.json` via `IAgentSessionStore`
- **Context**: `AgentContextWindow` keeps requests under `AgentRuntime:ContextTokenBudget` by shortening old tool results, then inserting a summary message into the thread (`/compact` forces it); the model sees only the newest summary and what follows
- **Shell**: `ShellCommandGuard` matches destructive commands by command position, not substring; `shell.run` timeout is up to 10 minutes
- **Fallback**: `AgentRuntime:Enabled=false` keeps the legacy single-skill planner for chat and voice
- **Permission rules**: `shell_run(git status:*)`, `files_write(*/docs/*)`, `mcp__github__*`; deny rules win, and allow rules never match chained shell commands (`ToolPermissionRule`)
- **MCP host**: `%AppData%/Kam/mcp.json` (Claude Desktop format) plus plugin `.mcp.json` and Todoist; servers start on the first turn and their tools are `mcp__{server}__{tool}` (`Infrastructure/Agent/Mcp`)
- **Extensions** (`Infrastructure/Agent/Extensions`): Agent Skills (`SKILL.md`, loaded on demand with `load_skill`), Claude Code layout plugins in `%AppData%/Kam/plugins`, and Markdown slash commands; managed on the Extensions page
- **Voice and tray**: voice commands and the tray's "New task" run in the agent chat
- **Roadmap**: `docs/architecture/agent-platform.md` (MCP host, Agent Skills, plugins, coding mode, subagents)

#### Reliability and performance (October 2026)
- **Missing config never breaks startup**: `LoggerServiceBase` is MongoDB only when `MongoDbConfiguration:ConnectionString` is set, otherwise `LocalFileLogger` (`%LocalAppData%/Kam/Logs/pipeline-*.log`); HuggingFace reports a missing key when used. `CompositionRootTests` resolves every registered service, so a throwing constructor fails CI
- **Chat**: messages sent during a turn queue (`QueuedAgentMessages`) and run after it; streamed text refreshes at most every 50 ms; the message list uses `VirtualizingStackPanel`
- **Startup**: MCP servers warm up in the background and a turn waits at most 5 s for them (`McpHost` `turnWait`); Whisper loads on first transcription
- **JSONL stores** read from the end with `JsonLinesFile.ReadLinesNewestFirst`

#### Dependency cleanup (October 2026)
- **One AI stack**: AutoGen, Semantic Kernel, Microsoft.Agents.AI and AgentFrameworkToolkit are gone; the OpenAI-compatible client comes from `Microsoft.Extensions.AI.OpenAI`, referenced directly
- **Removed dead code**: the legacy agents (`AgentFactory`, `AgentRegistry`, `SmartAgentOrchestrator`), intent detection services, `CommandHandlerService`, unused system CQRS commands and the Elasticsearch, Graylog, SQL Server, PostgreSQL and RabbitMQ log sinks
- **Output**: `SatelliteResourceLanguages=en;tr` in the UI and console projects; the installer publishes ReadyToRun without single-file compression

#### UI Redesign (October 2026)
- **Design system**: violet accent tokens (`Accent`, `AccentStrong`, `AccentSubtle`, `AccentOn`) in `Themes/Colors.*.axaml`, Lucide-style icon geometries (`Icon*`) and `MonoFontFamily` in `Themes/AppTheme.axaml`, shared control classes in `Themes/Controls.axaml` (`PrimaryAction`, `SecondaryAction`, `Pill`, `Card`, `PageTitle`, `SectionTitle`, `Overline`)
- **Shell**: icon sidebar that collapses at compact width, per-page title bar, chat workbench with bubbles, suggestion cards and a floating composer
- **WindowStateManager**: use `{x:Static services:WindowStateManager.Instance}` in XAML; never declare a new instance as a resource
- **XAML metadata tests** in `tests/SmartVoiceAgent.Tests/Ui/` parse `.axaml` text and pin copy and structure, so update them together with markup changes

#### Chat experience (October 2026)
- **Markdown**: agent replies render through `Controls/MarkdownView` (Markdig): headings, lists, task lists, code blocks with a copy button, tables and links. Links open only for http, https and mailto (`TryGetSafeLink`). Parsing happens during layout, and a block whose text did not change keeps its controls, so streaming rebuilds only the last block
- **Threads**: search, rename (F2 or the row menu) and delete with an inline confirmation in the sidebar. `JsonAgentSessionStore` writes title, custom title, model and message count before `messages`, so listing reads only the first 8 KB of each file; older files are read in full once
- **Model per thread**: the chat header picker saves `ModelId` with the thread (`IAgentSessionStore.SetModelAsync`); `AgentRuntime` reads it each turn and runs that model on the chat profile's connection (`ChatClientCache.WithModel`)
- **Model lists**: `ModelCatalogDefaults` is the short per-provider list Settings shows before a live list loads, and the chat header picker's list; refreshed lists from the provider and models.dev sort newest first by release date (`ModelCatalogOrdering`)
- **Settings without restart**: Settings and Integrations reload `SettingsConfigurationProvider` (debounced in `App`); `ConfiguredChatClient` resolves the client per request through `ChatClientCache`, so models, web search and Todoist apply to the next message. Email, SMS and GitHub App settings still apply after a restart
- **Approvals**: when a tool call waits and its chat is not on screen, `ApprovalToastNotifier` shows a corner card and changes the tray tooltip
- **Shortcuts**: Enter sends, Shift+Enter adds a line, Esc cancels voice or stops the turn, Ctrl+N new chat, Ctrl+K search chats, Ctrl+L composer, F2 rename, Ctrl+, Settings, Ctrl+Alt+Space talk (configurable)
- **Web search**: without keys `AiWebResearchService` searches DuckDuckGo's HTML page (`DuckDuckGoHtmlSearch`, at least 2 s between searches) and falls back to Bing's RSS feed (`BingRssSearch`); Integrations > Web search can add an optional Google Custom Search key (`WebResearch:SearchApiKey`, secret store) and `WebResearch:SearchEngineId`

#### Turkish and voice (October 2026)
- **Languages**: interface text lives in `Assets/Lang/{code}.{Area}.json` (en-US, tr-TR), embedded as `Kam.Lang.{code}.{Area}.json`. XAML uses `{DynamicResource Lang.Key}`, code uses `Loc.Get`/`Loc.Format`, and English fills any missing key. Long-lived view models refresh code-built text on `LocalizationService.Instance.LanguageChanged`; tests never call `Instance.SetLanguage`. `LanguageResourceProductCopyTests` requires every English key in Turkish with the same placeholders. Logs, diagnostics and tool names stay English
- **Voice in the UI**: `Services/VoiceAssistant` runs push to talk (mic buttons, tray "Talk", and the talk shortcut, which `GlobalTalkShortcut` registers with Windows through `RegisterHotKey`), the wake phrase, and spoken replies (Settings: off, replies to voice commands, all). Commands go to the agent chat, or to the command loop when `AgentRuntime:Enabled=false`
- **Capture**: `VoiceRecognitionServiceBase` converts any device format to 16 kHz mono (`PcmConverter`) and `VoiceActivityDetector` ends an utterance after 800 ms of silence. Windows records with WASAPI on the selected microphone, Linux with `arecord`, macOS with `rec`
- **Speech-to-text**: `MultiSTTService` follows `Voice:SpeechEngine`: local Whisper (`WhisperSTTService`, models from `SpeechModelCatalog` downloaded by `WhisperModelStore`) or an OpenAI-compatible `/audio/transcriptions` endpoint (`OpenAiTranscriptionService`); HuggingFace is a fallback when its key is set. Ollama STT was removed. `TranscriptCleaner` drops Whisper's invented lines for silence. The spoken language defaults to detection (`Voice:Language=auto`), because a fixed language makes Whisper translate other speech into it. Whisper runs on the GPU through the Vulkan runtime when a driver is present and falls back to the CPU; large-v3-turbo is the accurate choice for Turkish but needs the GPU (about 0.4 s a sentence on an RTX 4070 Ti, 19 s on the CPU). Recordings reach Whisper unprocessed: spectral noise suppression garbled Turkish speech
- **Wake phrase**: `WhisperWakeWordDetector` transcribes short utterances with the tiny model and matches them with `WakePhraseMatcher` (Turkish letters folded); "Hey Kam, open Spotify" runs in one breath
- **Spoken replies**: `TextToSpeechService` reads plain text (`SpeechTextFormatter`) sentence by sentence through SAPI/OneCore voices on Windows (`WindowsSpeechSynthesizer`) or `say`/`spd-say`/`espeak` elsewhere
- **Settings**: the Voice section writes `ISettingsService` and `AiRuntimeConfigurationMapper` maps it to `Voice:*`; services read `VoiceSettings.Read(configuration)` on each use, so changes apply without a restart. `SettingsViewModel.UseSpeechServices` swaps the model store and speech service in tests
- **Composer**: voice status (listening, transcribing, model download, problems) shows in a strip above the prompt next to the queued-message strip; Esc cancels voice before it stops a turn
- **Title bar status**: shows the agent (Ready, Working, Set up a model). With `AgentRuntime:Enabled=false` it shows the command loop, and a click pauses or resumes it

### Test Status
```
Build: ✅ Success (CI runs on windows-2025)
Tests: 1470 total; on Linux 13 fail because they assume Windows paths or tessdata
Run locally on Linux: DOTNET_ROLL_FORWARD=Major dotnet test tests/SmartVoiceAgent.Tests -p:EnableWindowsTargeting=true
```

### Known Issues
- One flaky timing-dependent test (`PerformanceBehavior_SlowRequest_LogsWarning`)
- Function calling reliability depends on AI model (Claude 3.5 Sonnet recommended for 90%+ reliability)
*Project Language: English (code and documentation)*
