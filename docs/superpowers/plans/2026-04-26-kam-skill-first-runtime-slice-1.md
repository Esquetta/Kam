# Kam Skill-First Runtime Slice 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first production slice of Kam's skill-first runtime: unified model provider profiles, planner/skill contracts, a deterministic built-in app skill executor, and Settings persistence for model selection.

**Architecture:** Add stable Core contracts first, then Infrastructure implementations, then UI settings bindings. The model layer produces structured plans, but the new skill runtime owns validation and execution. Existing agent behavior remains available while the first built-in skills are introduced behind new interfaces.

**Tech Stack:** .NET 9, C#, xUnit, Moq, FluentAssertions, Microsoft.Extensions.AI, OpenAI-compatible client, Avalonia/ReactiveUI settings view model.

---

### Task 1: Model Provider Profile Domain Contract

**Files:**
- Create: `src/SmartVoiceAgent.Core/Models/AI/ModelProviderProfile.cs`
- Create: `src/SmartVoiceAgent.Core/Models/AI/ModelProviderRole.cs`
- Create: `src/SmartVoiceAgent.Core/Models/AI/ModelProviderType.cs`
- Create: `src/SmartVoiceAgent.Core/Models/AI/ModelProviderValidationResult.cs`
- Test: `tests/SmartVoiceAgent.Tests/Core/Models/AI/ModelProviderProfileTests.cs`

- [ ] **Step 1: Write failing tests for profile validation**

```csharp
using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Tests.Core.Models.AI;

public class ModelProviderProfileTests
{
    [Fact]
    public void Validate_EnabledOpenRouterProfileWithPlannerRole_ReturnsSuccess()
    {
        var profile = new ModelProviderProfile
        {
            Id = "openrouter-primary",
            Provider = ModelProviderType.OpenRouter,
            DisplayName = "OpenRouter Primary",
            Endpoint = "https://openrouter.ai/api/v1",
            ApiKey = "sk-test",
            ModelId = "openai/gpt-4.1-mini",
            Roles = [ModelProviderRole.Planner],
            Enabled = true
        };

        var result = profile.Validate();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EnabledProfileWithoutModelId_ReturnsActionableError()
    {
        var profile = new ModelProviderProfile
        {
            Id = "openrouter-primary",
            Provider = ModelProviderType.OpenRouter,
            Endpoint = "https://openrouter.ai/api/v1",
            ApiKey = "sk-test",
            Roles = [ModelProviderRole.Planner],
            Enabled = true
        };

        var result = profile.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Model id is required.");
    }

    [Fact]
    public void MaskedApiKey_DoesNotExposeSecret()
    {
        var profile = new ModelProviderProfile { ApiKey = "sk-1234567890abcdef" };

        profile.MaskedApiKey.Should().Be("sk-1***********cdef");
    }
}
```

- [ ] **Step 2: Run test to verify RED**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~ModelProviderProfileTests"`

Expected: fail because `SmartVoiceAgent.Core.Models.AI` types do not exist.

- [ ] **Step 3: Implement minimal model profile contract**

Create the files listed above with these public types:

```csharp
namespace SmartVoiceAgent.Core.Models.AI;

public enum ModelProviderType
{
    OpenRouter = 0,
    OpenAICompatible = 1,
    Ollama = 2
}

public enum ModelProviderRole
{
    Planner = 0,
    Chat = 1,
    Summarizer = 2
}

public sealed record ModelProviderValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ModelProviderValidationResult Success() => new(true, Array.Empty<string>());
    public static ModelProviderValidationResult Failure(IEnumerable<string> errors) => new(false, errors.ToArray());
}
```

```csharp
namespace SmartVoiceAgent.Core.Models.AI;

public sealed class ModelProviderProfile
{
    public string Id { get; set; } = string.Empty;
    public ModelProviderType Provider { get; set; } = ModelProviderType.OpenRouter;
    public string DisplayName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public List<ModelProviderRole> Roles { get; set; } = [ModelProviderRole.Planner];
    public float Temperature { get; set; } = 0.2f;
    public int MaxTokens { get; set; } = 1200;
    public bool Enabled { get; set; }

    public string MaskedApiKey => MaskSecret(ApiKey);

    public ModelProviderValidationResult Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id)) errors.Add("Profile id is required.");
        if (string.IsNullOrWhiteSpace(Endpoint) || !Uri.TryCreate(Endpoint, UriKind.Absolute, out _)) errors.Add("A valid endpoint is required.");
        if (Enabled && string.IsNullOrWhiteSpace(ApiKey)) errors.Add("API key is required for enabled profiles.");
        if (string.IsNullOrWhiteSpace(ModelId)) errors.Add("Model id is required.");
        if (Roles.Count == 0) errors.Add("At least one model role is required.");
        if (MaxTokens <= 0) errors.Add("Max tokens must be greater than zero.");
        if (Temperature < 0 || Temperature > 2) errors.Add("Temperature must be between 0 and 2.");
        return errors.Count == 0 ? ModelProviderValidationResult.Success() : ModelProviderValidationResult.Failure(errors);
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= 8) return new string('*', value.Length);
        return $"{value[..4]}{new string('*', value.Length - 8)}{value[^4..]}";
    }
}
```

- [ ] **Step 4: Run test to verify GREEN**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~ModelProviderProfileTests"`

Expected: all `ModelProviderProfileTests` pass.

### Task 2: Settings Persistence For AI Profiles

**Files:**
- Modify: `src/Ui/SmartVoiceAgent.Ui/Services/ISettingsService.cs`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Services/JsonSettingsService.cs`
- Test: `tests/SmartVoiceAgent.Tests/Ui/Services/JsonSettingsServiceAiProfileTests.cs`

- [ ] **Step 1: Write failing tests for saving and loading profiles**

```csharp
using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public class JsonSettingsServiceAiProfileTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(Path.GetTempPath(), "kam-settings-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoadModelProviderProfiles_PreservesActivePlannerProfile()
    {
        using (var service = new JsonSettingsService(_settingsDirectory))
        {
            service.ModelProviderProfiles = [
                new ModelProviderProfile
                {
                    Id = "openrouter-primary",
                    Provider = ModelProviderType.OpenRouter,
                    Endpoint = "https://openrouter.ai/api/v1",
                    ApiKey = "sk-test",
                    ModelId = "openai/gpt-4.1-mini",
                    Roles = [ModelProviderRole.Planner],
                    Enabled = true
                }
            ];
            service.ActivePlannerProfileId = "openrouter-primary";
            service.Save();
        }

        using var reloaded = new JsonSettingsService(_settingsDirectory);

        reloaded.ActivePlannerProfileId.Should().Be("openrouter-primary");
        reloaded.ModelProviderProfiles.Should().ContainSingle(p => p.Id == "openrouter-primary" && p.ModelId == "openai/gpt-4.1-mini");
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, true);
        }
    }
}
```

- [ ] **Step 2: Run test to verify RED**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~JsonSettingsServiceAiProfileTests"`

Expected: fail because `ModelProviderProfiles` and `ActivePlannerProfileId` do not exist on `ISettingsService`/`JsonSettingsService`.

- [ ] **Step 3: Add settings properties**

Add to `ISettingsService`:

```csharp
IReadOnlyList<ModelProviderProfile> ModelProviderProfiles { get; set; }
string ActivePlannerProfileId { get; set; }
```

Add matching properties and serialized `SettingsData` fields in `JsonSettingsService`. Use `List<ModelProviderProfile>` internally and clone lists when setting to avoid external mutation.

- [ ] **Step 4: Run test to verify GREEN**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~JsonSettingsServiceAiProfileTests"`

Expected: all settings AI profile tests pass.

### Task 3: Planner Contract Parser

**Files:**
- Create: `src/SmartVoiceAgent.Core/Models/Skills/SkillPlan.cs`
- Create: `src/SmartVoiceAgent.Core/Models/Skills/SkillPlanParseResult.cs`
- Create: `src/SmartVoiceAgent.Infrastructure/Skills/Planning/SkillPlanParser.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/Planning/SkillPlanParserTests.cs`

- [ ] **Step 1: Write failing parser tests**

```csharp
using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Skills.Planning;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Planning;

public class SkillPlanParserTests
{
    [Fact]
    public void Parse_JsonInsideMarkdownFence_ReturnsPlan()
    {
        const string response = """
        ```json
        {
          "skillId": "apps.open",
          "arguments": { "applicationName": "Spotify" },
          "confidence": 0.91,
          "requiresConfirmation": false,
          "reasoning": "Open Spotify"
        }
        ```
        """;

        var result = SkillPlanParser.Parse(response);

        result.IsValid.Should().BeTrue();
        result.Plan!.SkillId.Should().Be("apps.open");
        result.Plan.Arguments["applicationName"].GetString().Should().Be("Spotify");
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsActionableError()
    {
        var result = SkillPlanParser.Parse("I will open Spotify.");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("valid JSON");
    }
}
```

- [ ] **Step 2: Run test to verify RED**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SkillPlanParserTests"`

Expected: fail because parser and skill plan types do not exist.

- [ ] **Step 3: Implement minimal parser**

Implement `SkillPlan` using `Dictionary<string, JsonElement>` for arguments and a parser that removes markdown fences, extracts the outer JSON object, deserializes case-insensitively, and validates `skillId`.

- [ ] **Step 4: Run test to verify GREEN**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SkillPlanParserTests"`

Expected: all parser tests pass.

### Task 4: Minimum Skill Runtime Contracts and Registry

**Files:**
- Create: `src/SmartVoiceAgent.Core/Models/Skills/KamSkillManifest.cs`
- Create: `src/SmartVoiceAgent.Core/Models/Skills/SkillPermission.cs`
- Create: `src/SmartVoiceAgent.Core/Models/Skills/SkillRiskLevel.cs`
- Create: `src/SmartVoiceAgent.Core/Models/Skills/SkillResult.cs`
- Create: `src/SmartVoiceAgent.Core/Interfaces/ISkillRegistry.cs`
- Create: `src/SmartVoiceAgent.Core/Interfaces/ISkillExecutor.cs`
- Create: `src/SmartVoiceAgent.Infrastructure/Skills/InMemorySkillRegistry.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/InMemorySkillRegistryTests.cs`

- [ ] **Step 1: Write failing registry tests**

```csharp
using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills;

public class InMemorySkillRegistryTests
{
    [Fact]
    public void Register_EnabledBuiltInSkill_CanResolveById()
    {
        var registry = new InMemorySkillRegistry();
        var manifest = new KamSkillManifest
        {
            Id = "apps.open",
            DisplayName = "Open Application",
            Source = "builtin",
            ExecutorType = "builtin",
            Enabled = true,
            RiskLevel = SkillRiskLevel.High,
            Permissions = [SkillPermission.ProcessLaunch]
        };

        registry.Register(manifest);

        registry.TryGet("apps.open", out var resolved).Should().BeTrue();
        resolved!.Permissions.Should().Contain(SkillPermission.ProcessLaunch);
    }
}
```

- [ ] **Step 2: Run test to verify RED**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~InMemorySkillRegistryTests"`

Expected: fail because registry and skill contracts do not exist.

- [ ] **Step 3: Implement registry contracts**

Implement contracts with no external dependencies. `InMemorySkillRegistry` should use a case-insensitive `ConcurrentDictionary<string, KamSkillManifest>`.

- [ ] **Step 4: Run test to verify GREEN**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~InMemorySkillRegistryTests"`

Expected: registry tests pass.

### Task 5: Built-In App Skill Executor

**Files:**
- Create: `src/SmartVoiceAgent.Infrastructure/Skills/BuiltIn/AppSkills/AppSkillExecutor.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/Skills/BuiltIn/AppSkillExecutorTests.cs`

- [ ] **Step 1: Write failing app skill executor tests**

```csharp
using FluentAssertions;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AppSkills;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class AppSkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AppsStatus_ReturnsRunningStatus()
    {
        var executor = new AppSkillExecutor(new FakeApplicationService());
        var plan = SkillPlan.FromObject("apps.status", new { applicationName = "Spotify" });

        var result = await executor.ExecuteAsync(plan);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Running");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedSkill_ReturnsFailure()
    {
        var executor = new AppSkillExecutor(new FakeApplicationService());
        var result = await executor.ExecuteAsync(SkillPlan.FromObject("apps.delete", new { applicationName = "Spotify" }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported app skill");
    }

    private sealed class FakeApplicationService : IApplicationService
    {
        public Task OpenApplicationAsync(string appName) => Task.CompletedTask;
        public Task<AppStatus> GetApplicationStatusAsync(string appName) => Task.FromResult(AppStatus.Running);
        public Task CloseApplicationAsync(string appName) => Task.CompletedTask;
        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync() => Task.FromResult<IEnumerable<AppInfoDTO>>([new("Spotify", "spotify.exe", true)]);
        public Task<ApplicationInstallInfo> CheckApplicationInstallationAsync(string appName) => Task.FromResult(new ApplicationInstallInfo(true, "spotify.exe", appName));
        public Task<string> GetApplicationExecutablePathAsync(string appName) => Task.FromResult("spotify.exe");
    }
}
```

- [ ] **Step 2: Run test to verify RED**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~AppSkillExecutorTests"`

Expected: fail because `AppSkillExecutor` and helper APIs do not exist.

- [ ] **Step 3: Implement minimal app executor**

Implement `apps.open`, `apps.close`, `apps.status`, and `apps.list`. Extract `applicationName` from `SkillPlan.Arguments`; return failed `SkillResult` when required args are missing.

- [ ] **Step 4: Run test to verify GREEN**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~AppSkillExecutorTests"`

Expected: app skill tests pass.

### Task 6: Dependency Injection Wiring

**Files:**
- Modify: `src/SmartVoiceAgent.Infrastructure/DependencyInjection/ServiceRegistration.cs`
- Modify: `src/SmartVoiceAgent.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/SmartVoiceAgent.Tests/Infrastructure/DependencyInjection/SkillRuntimeRegistrationTests.cs`

- [ ] **Step 1: Write failing DI test**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.DependencyInjection;

namespace SmartVoiceAgent.Tests.Infrastructure.DependencyInjection;

public class SkillRuntimeRegistrationTests
{
    [Fact]
    public void AddInfrastructureServices_RegistersSkillRegistryAndExecutors()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddLogging();
        services.AddInfrastructureServices(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetService<ISkillRegistry>().Should().NotBeNull();
        provider.GetServices<ISkillExecutor>().Should().NotBeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify RED**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SkillRuntimeRegistrationTests"`

Expected: fail until DI registers skill runtime contracts.

- [ ] **Step 3: Register services**

Register:

```csharp
services.AddSingleton<ISkillRegistry, InMemorySkillRegistry>();
services.AddScoped<ISkillExecutor, AppSkillExecutor>();
```

Also seed built-in app manifests during startup or in an `IBuiltInSkillCatalog` if the implementation needs cleaner separation.

- [ ] **Step 4: Run test to verify GREEN**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SkillRuntimeRegistrationTests"`

Expected: DI test passes.

### Task 7: Settings ViewModel AI Profile Surface

**Files:**
- Modify: `src/Ui/SmartVoiceAgent.Ui/ViewModels/PageModels/SettingsViewModel.cs`
- Modify: `src/Ui/SmartVoiceAgent.Ui/Views/SettingsView.axaml`
- Test: `tests/SmartVoiceAgent.Tests/Ui/ViewModels/SettingsViewModelAiProfileTests.cs`

- [ ] **Step 1: Write failing ViewModel test**

```csharp
using FluentAssertions;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public class SettingsViewModelAiProfileTests
{
    [Fact]
    public void DefaultAiSettings_AreOpenRouterCompatible()
    {
        var viewModel = new SettingsViewModel();

        viewModel.AiProvider.Should().Be("OpenRouter");
        viewModel.AiEndpoint.Should().Be("https://openrouter.ai/api/v1");
    }
}
```

- [ ] **Step 2: Run test to verify RED**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SettingsViewModelAiProfileTests"`

Expected: fail because `AiProvider` and `AiEndpoint` properties do not exist.

- [ ] **Step 3: Add ViewModel properties and XAML controls**

Add properties for:

- `AiProvider`
- `AiEndpoint`
- `AiModelId`
- `AiApiKey`
- `MaskedAiApiKey`
- `ActivePlannerProfileId`

Add a Settings section named `AI RUNTIME` with provider, endpoint, model id, masked API key/password box, and a placeholder test connection button. Do not call external APIs in the first ViewModel test.

- [ ] **Step 4: Run test to verify GREEN**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release --filter "FullyQualifiedName~SettingsViewModelAiProfileTests"`

Expected: ViewModel AI settings test passes.

### Task 8: Full Verification

**Files:**
- No new files.

- [ ] **Step 1: Run targeted tests**

Run each targeted test group from Tasks 1-7.

Expected: all targeted tests pass.

- [ ] **Step 2: Run full test suite**

Run: `dotnet test tests\SmartVoiceAgent.Tests\SmartVoiceAgent.Tests.csproj --configuration Release`

Expected: all tests pass.

- [ ] **Step 3: Run build**

Run: `dotnet build Kam.sln --configuration Release --no-restore`

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Run package vulnerability check**

Run: `dotnet list Kam.sln package --vulnerable --include-transitive`

Expected: every project reports no vulnerable packages for the configured sources.

- [ ] **Step 5: Commit implementation**

Commit code separately from the design/plan docs:

```powershell
git add src tests
git commit -m "feat: add skill-first runtime foundation"
```

