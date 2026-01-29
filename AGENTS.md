# Smart Voice Agent (KAM Neural Core) - Agent Guide

> **For AI Coding Agents**: This document provides essential information about the project structure, architecture, and development conventions. Read this first before making any changes.

## Project Overview

**Smart Voice Agent** (also known as KAM Neural Core) is an advanced AI-powered voice assistant with multi-agent collaboration, system control, and intelligent task management capabilities. It supports voice recognition, natural language processing, and can control system applications and devices.

### Key Features
- **Voice Recognition & Processing**: Multi-platform voice input with STT (Speech-to-Text) providers (HuggingFace, OpenAI Whisper, Ollama)
- **Multi-Agent AI System**: Coordinator, SystemAgent, TaskAgent, WebSearchAgent, and AnalyticsAgent
- **System Control**: Application management, device control (volume, brightness, WiFi, Bluetooth), power management
- **Task Management**: Todoist integration via MCP (Model Context Protocol)
- **Hybrid Intent Detection**: AI-based + Semantic + Context-aware + Pattern-based detection

---

## Technology Stack

| Category | Technologies |
|----------|-------------|
| **Framework** | .NET 9.0 |
| **UI Framework** | Avalonia UI 11.3.10 (cross-platform desktop) |
| **AI/ML** | AutoGen 0.2.3, Microsoft.SemanticKernel 1.67.1, Microsoft.Agents.AI |
| **MCP** | ModelContextProtocol 0.5.0-preview.1 |
| **CQRS** | MediatR 12.5.0 |
| **Validation** | FluentValidation 12.1.1 |
| **Audio** | NAudio 2.2.1, Whisper.net 1.9.0 |
| **OCR** | Tesseract 5.2.0 |
| **Logging** | Serilog 4.3.0 with MongoDB, Elasticsearch, PostgreSQL sinks |
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
│   └── Ui/SmartVoiceAgent.Ui/              # Avalonia desktop UI
├── tests/
│   └── SmartVoiceAgent.Tests/              # Unit & integration tests
└── assets/                                  # Images, icons, resources
```

### Layer Details

#### 1. SmartVoiceAgent.Core (Domain Layer)
- **Purpose**: Domain entities, interfaces, DTOs, enums, models
- **Dependencies**: Minimal (AutoGen.Core, MediatR, Microsoft.Agents.AI)
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
- **Dependencies**: Core, CrossCuttingConcerns, MediatR, FluentValidation, AutoGen
- **Key Folders**:
  - `Commands/` - Command records (e.g., `OpenApplicationCommand`)
  - `Handlers/CommandHandlers/` - Command handlers
  - `Handlers/QueryHandlers/` - Query handlers
  - `Pipelines/` - MediatR pipeline behaviors
    - `Caching/` - Request caching behavior
    - `Logging/` - Request logging behavior
    - `Performance/` - Performance monitoring behavior
    - `Validation/` - FluentValidation behavior
  - `Validators/` - FluentValidation validators
  - `Notifications/` - MediatR notifications
  - `NotificationHandlers/` - Notification handlers

#### 3. SmartVoiceAgent.Infrastructure (Infrastructure Layer)
- **Purpose**: External services, AI agents, platform-specific implementations
- **Dependencies**: Core, Application
- **Key Folders**:
  - `Agent/` - Multi-agent AI system
    - `Agents/` - Agent builders, factories, orchestrators
    - `Tools/` - Agent tools (SystemAgentTools, TaskAgentTools, WebSearchAgentTools)
    - `Thread/` - Agent thread implementations
  - `Services/` - Service implementations
  - `Helpers/` - Helper classes (CircularAudioBuffer, AudioProcessingService)
  - `Factories/` - Factory pattern implementations
  - `DependencyInjection/` - Service registration

#### 4. SmartVoiceAgent.CrossCuttingConcerns
- **Purpose**: Logging infrastructure, exceptions, security utilities
- **Dependencies**: MongoDB.Driver, Serilog, Npgsql
- **Key Components**:
  - `Logging/` - Serilog configuration and sinks
  - `Exceptions/` - Custom exception types
  - `Security/` - SecurityUtilities class (path validation, command injection prevention)

#### 5. SmartVoiceAgent.Ui (Presentation Layer)
- **Purpose**: Avalonia-based desktop UI
- **Dependencies**: Avalonia 11.3.10, ReactiveUI, Application, Infrastructure
- **Key Folders**:
  - `Views/` - XAML views (MainWindow.axaml, etc.)
  - `ViewModels/` - ViewModels (MVVM pattern)
  - `Services/` - UI-specific services (UiLogService, TrayIconService)
  - `Converters/` - XAML value converters

---

## Architecture Patterns

### 1. CQRS (Command Query Responsibility Segregation)
All business operations are implemented as commands or queries handled through MediatR:

```csharp
// Command definition in Core layer
public record OpenApplicationCommand(string ApplicationName) 
    : IRequest<CommandResultDTO>, ICachableRequest, IIntervalRequest;

// Handler in Application layer
public sealed class OpenApplicationCommandHandler : 
    IRequestHandler<OpenApplicationCommand, CommandResultDTO>
{
    public async Task<CommandResultDTO> Handle(OpenApplicationCommand request, 
        CancellationToken cancellationToken)
    {
        // Implementation
    }
}
```

### 2. Pipeline Behaviors
Cross-cutting concerns are handled via MediatR pipeline behaviors:

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

### 4. Multi-Agent System
AI agents are orchestrated through `SmartAgentOrchestrator`:

```
User Input → Coordinator Agent → [SystemAgent | TaskAgent | ResearchAgent] → Response
                    ↓
            AnalyticsAgent (monitoring)
```

Agents:
- **CoordinatorAgent** - Routes requests to appropriate agents
- **SystemAgent** - Application control, system operations
- **TaskAgent** - Task management (Todoist via MCP)
- **ResearchAgent** - Web research and information retrieval

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
Located in `SmartVoiceAgent.CrossCuttingConcerns/Security/SecurityUtilities.cs`:

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

### Adding a New Agent
1. Define agent tools in `SmartVoiceAgent.Infrastructure/Agent/Tools/`
2. Add agent thread in `SmartVoiceAgent.Infrastructure/Agent/Thread/`
3. Register in `AgentRegistry`
4. Update `SmartAgentOrchestrator` routing logic

---

## Troubleshooting

### Common Issues

**Voice Recognition Not Working**
- Check microphone permissions (Windows: Settings → Privacy → Microphone)
- Verify Whisper/NAudio native dependencies are present

**API Connection Issues**
- Verify API keys in User Secrets: `dotnet user-secrets list`
- Check network connectivity
- Review logs in MongoDB or console output

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

*Last Updated: 2026-01-29*

## Current Project Status

### Recent Improvements (January 2026)

#### Performance Optimizations
- **VoiceRecognitionServiceBase**: ArrayPool integration, Span<T>, removed forced GC
- **CircularAudioBuffer**: Bulk memory operations with Span.CopyTo/Buffer.BlockCopy  
- **DynamicAppExtractionService**: Optimized Levenshtein distance (two-row algorithm)
- **PerformanceBehavior**: Fixed thread-safety bug (shared Stopwatch → per-request)

#### Agent System Improvements
- **AgentFactory.cs**: Optimized instructions for reliable function calling with explicit examples
- **TaskAgentTools.cs**: Production-ready error handling with retry logic, timeout protection, thread safety
- **AgentBuilder.cs**: Fixed reflection parameter count mismatch for InitializeAsync with CancellationToken

#### UI Improvements
- **Kernel Log Panel**: Increased width (320→420px, 450→600px) and font size (11→13px)
- **Application Identity**: Changed display name to "Kam" in Task Manager, added embedded icon

#### Testing Infrastructure
- **Total Tests**: 179 (178 passing)
- **Unit Tests**: 119 tests for optimized components
- **Integration Tests**: 60 tests for voice pipeline, multi-agent orchestration, error handling

### Test Status
```
Build: ✅ Success
Tests: 178/179 passing (99.4%)
Coverage: Unit + Integration tests for core components
```

### Known Issues
- One flaky timing-dependent test (`PerformanceBehavior_SlowRequest_LogsWarning`)
- Function calling reliability depends on AI model (Claude 3.5 Sonnet recommended for 90%+ reliability)
*Project Language: English (code and documentation)*
