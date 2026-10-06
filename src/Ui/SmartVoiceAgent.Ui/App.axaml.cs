using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartVoiceAgent.Application.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.ViewModels;
using SmartVoiceAgent.Ui.Views;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
using SmartVoiceAgent.Infrastructure.Skills.Policy;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration.Json;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;

namespace SmartVoiceAgent.Ui
{
    public partial class App : Avalonia.Application
    {
        private TrayIconService? _trayIconService;
        private MainWindowViewModel? _mainViewModel;
        private IHost? _host;
        private IServiceScope? _applicationScope;
        private UiLogService? _uiLogService;
        private ErrorHandlingService? _errorHandlingService;
        private SettingsConfigurationProvider? _settingsConfiguration;
        private CancellationTokenSource? _settingsApplyDelay;

        /// <summary>
        /// Gets the service provider for dependency injection access from ViewModels
        /// </summary>
        public static IServiceProvider? Services => (Current as App)?.GetCurrentServiceProvider();

        public static IServiceScope CreateApplicationServiceScope(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return services.CreateScope();
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            LocalizationService.Instance.Attach(Resources);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Setup global exception handlers
            SetupGlobalExceptionHandling();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Build the host with backend services
                _host = BuildHost();
                _applicationScope = CreateApplicationServiceScope(_host.Services);
                var services = _applicationScope.ServiceProvider;

                // Validate critical configuration
                ValidateConfiguration();

                // Load startup settings
                var settingsService = new JsonSettingsService();
                LocalizationService.Instance.SetLanguage(string.IsNullOrWhiteSpace(settingsService.Language)
                    ? LocalizationService.ResolveDefault(CultureInfo.CurrentUICulture)
                    : settingsService.Language);

                // Initialize ViewModel
                _mainViewModel = new MainWindowViewModel();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = _mainViewModel
                };

                // Settings and Integrations changes reach running services without a restart.
                _mainViewModel.SettingsChanged += OnSettingsChanged;

                // Set main window reference for error handling service
                _errorHandlingService?.SetMainWindow(desktop.MainWindow);
                _errorHandlingService?.LogInformation("Main window initialized");

                // Setup Tray Icon
                _trayIconService = new TrayIconService();
                _trayIconService.Initialize();
                _mainViewModel.SetTrayIconService(_trayIconService);
                _mainViewModel.SetApprovalNotifier(new ApprovalToastNotifier(desktop.MainWindow, _trayIconService));

                // Connect UI Log Service to ViewModel
                _uiLogService = (UiLogService?)services.GetService<IUiLogService>();
                _uiLogService?.SetViewModel(_mainViewModel);

                // Connect Command Input Service to ViewModel
                var commandInput = services.GetRequiredService<ICommandInputService>();
                _mainViewModel.SetCommandInputService(commandInput);

                var slashCommandService = services.GetRequiredService<ISlashCommandService>();
                _mainViewModel.SetSlashCommandService(slashCommandService);

                var runtimeAgentRunStore = services.GetRequiredService<IRuntimeAgentRunStore>();
                _mainViewModel.SetRuntimeAgentRunStore(runtimeAgentRunStore);

                var applicationUpdateService = services.GetRequiredService<IApplicationUpdateService>();
                var applicationRestartPlanner = services.GetRequiredService<IApplicationRestartPlanner>();
                var applicationVersionProvider = services.GetRequiredService<IApplicationVersionProvider>();
                var applicationUpdateSession = services.GetRequiredService<IApplicationUpdateSession>();
                _mainViewModel.SetApplicationUpdateServices(
                    applicationUpdateService,
                    applicationRestartPlanner,
                    applicationVersionProvider,
                    applicationUpdateSession);

                var githubAppClient = services.GetRequiredService<IGitHubAppClient>();
                _mainViewModel.SetGitHubAppClient(githubAppClient);
                var githubAppClientFactory = services.GetRequiredService<IGitHubAppClientFactory>();
                _mainViewModel.SetGitHubAppClientFactory(githubAppClientFactory);

                // Connect VoiceAgent Host Control to ViewModel
                var hostControl = services.GetRequiredService<IVoiceAgentHostControl>();
                _mainViewModel.SetVoiceAgentHostControl(hostControl);

                // Connect Skill Health Service to the skills dashboard
                var skillHealthService = services.GetRequiredService<ISkillHealthService>();
                _mainViewModel.SetSkillHealthService(skillHealthService);

                var skillEvalHarness = services.GetRequiredService<ISkillEvalHarness>();
                var skillEvalCaseCatalog = services.GetRequiredService<ISkillEvalCaseCatalog>();
                _mainViewModel.SetSkillEvalServices(skillEvalHarness, skillEvalCaseCatalog);

                var skillPolicyManager = services.GetRequiredService<ISkillPolicyManager>();
                _mainViewModel.SetSkillPolicyManager(skillPolicyManager);

                var skillImportService = services.GetRequiredService<ISkillImportService>();
                _mainViewModel.SetSkillImportService(skillImportService);

                var skillTestService = services.GetRequiredService<ISkillTestService>();
                _mainViewModel.SetSkillTestService(skillTestService);

                var skillConfirmationService = services.GetRequiredService<ISkillConfirmationService>();
                _mainViewModel.SetSkillConfirmationService(skillConfirmationService);

                var skillExecutionHistoryService = services.GetRequiredService<ISkillExecutionHistoryService>();
                _mainViewModel.SetSkillExecutionHistoryService(skillExecutionHistoryService);

                var skillPlannerTraceStore = services.GetRequiredService<ISkillPlannerTraceStore>();
                _mainViewModel.SetSkillPlannerTraceStore(skillPlannerTraceStore);

                var skillExecutionPipeline = services.GetRequiredService<ISkillExecutionPipeline>();
                _mainViewModel.SetSkillExecutionPipeline(skillExecutionPipeline);

                // Chat runs through the tool-calling agent unless AgentRuntime:Enabled is false,
                // which keeps the legacy single-skill planner for one release.
                var agentRuntimeOptions = services.GetRequiredService<IOptions<AgentRuntimeOptions>>().Value;
                if (agentRuntimeOptions.Enabled)
                {
                    _mainViewModel.SetAgentRuntime(
                        services.GetRequiredService<IAgentRuntime>(),
                        services.GetRequiredService<IToolPermissionService>(),
                        services.GetRequiredService<IAgentSessionStore>(),
                        services.GetService<IAgentCommandCatalog>());
                    _mainViewModel.SetExtensionServices(
                        services.GetService<IMcpHost>(),
                        services.GetService<IAgentPluginCatalog>(),
                        services.GetService<IAgentSkillCatalog>(),
                        services.GetService<IAgentCommandCatalog>());

                    // Start MCP servers now, so the first message does not wait for them.
                    _ = WarmUpMcpServersAsync(services.GetService<IMcpHost>());
                }

                // Voice: the talk button and shortcut, the wake phrase and spoken replies.
                SetupVoiceAssistant(_mainViewModel, services);

                // Apply startup behavior settings
                ApplyStartupBehavior(desktop, settingsService);

                // Start the host
                _ = StartHostAsync(_host);

                desktop.ShutdownRequested += (s, e) =>
                {
                    _errorHandlingService?.LogInformation("Application shutting down...");
                    settingsService.Dispose();
                    _mainViewModel.Cleanup();
                    _trayIconService?.Dispose();

                    if (_host != null)
                    {
                        // Block until hosted services stop: an async void handler let the process
                        // exit mid-shutdown. Stop on the thread pool so services that marshal to the
                        // UI thread cannot deadlock, and cap the wait so a stuck service can't hang exit.
                        var host = _host;
                        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        try
                        {
                            Task.Run(() => host.StopAsync(stopTimeout.Token)).Wait(TimeSpan.FromSeconds(6));
                        }
                        catch (Exception ex)
                        {
                            _errorHandlingService?.LogError(ex, "Host failed to stop cleanly");
                        }

                        _applicationScope?.Dispose();
                        _applicationScope = null;
                        _host.Dispose();
                        _host = null;
                    }
                    _errorHandlingService?.LogInformation("Application shutdown complete");
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Applies startup behavior settings (minimized, tray only, etc.)
        /// </summary>
        private void ApplyStartupBehavior(IClassicDesktopStyleApplicationLifetime desktop, ISettingsService settings)
        {
            if (desktop.MainWindow == null) return;

            switch (settings.StartupBehavior)
            {
                case 1: // Minimized
                    desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
                    desktop.MainWindow.Show();
                    break;
                    
                case 2: // Tray only (hide window)
                    desktop.MainWindow.Hide();
                    break;
                    
                default: // Normal (0 or any other value)
                    if (settings.ShowOnStartup)
                    {
                        desktop.MainWindow.Show();
                        desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                        desktop.MainWindow.Activate();
                    }
                    else
                    {
                        desktop.MainWindow.Hide();
                    }
                    break;
            }

            // Also check for command line arguments that might indicate startup
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--minimized") || args.Contains("-m"))
            {
                desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
            }
            else if (args.Contains("--tray") || args.Contains("-t"))
            {
                desktop.MainWindow.Hide();
            }
        }

        private IHost BuildHost()
        {
            return Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    // CreateDefaultBuilder already adds:
                    // - appsettings.json
                    // - UserSecrets when the caller explicitly runs in Development
                    // - EnvironmentVariables
                    AddUserRuntimeConfiguration(config);
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;

                    if (ShouldLogConfigurationDebugInfo(configuration))
                    {
                        LogConfigurationDebugInfo(configuration);
                    }

                    // Register Application services
                    services.AddApplicationServices();

                    // Register Infrastructure services
                    services.AddInfrastructureServices(configuration);

                    // Register Smart Voice Agent services
                    services.AddSmartVoiceAgent(configuration);

                    // Lets ${secret:NAME} in mcp.json read secrets saved in Settings.
                    services.AddSingleton<ISecretValueProvider>(_ =>
                        new JsonFileSettingsSecretStore(JsonSettingsService.GetDefaultSettingsDirectory()));

                    // Register our custom UI Log Service (replaces the dummy one)
                    services.AddSingleton<IUiLogService>(sp => new UiLogService());
                })
                .Build();
        }

        private void AddUserRuntimeConfiguration(IConfigurationBuilder config)
        {
            IReadOnlyDictionary<string, string?> values = new Dictionary<string, string?>();
            try
            {
                using var settingsService = new JsonSettingsService();
                values = AiRuntimeConfigurationMapper.CreateOverrides(settingsService);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AI runtime settings could not be loaded: {ex.Message}");
            }

            // Reloaded from the Settings page while the app runs; see ApplySettingsToRuntime.
            var source = new SettingsConfigurationSource(values);
            _settingsConfiguration = source.Provider;
            config.Add(source);
        }

        private async void OnSettingsChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnSettingsChanged(sender, e));
                return;
            }

            // Text fields save on every keystroke, so apply once typing pauses.
            _settingsApplyDelay?.Cancel();
            var delay = _settingsApplyDelay = new CancellationTokenSource();
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), delay.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            ApplySettingsToRuntime();
        }

        /// <summary>
        /// Pushes saved Settings into configuration. Chat clients are rebuilt on their next request,
        /// options monitors see the change, and MCP servers reload when the Todoist key changed.
        /// </summary>
        private void ApplySettingsToRuntime()
        {
            if (_settingsConfiguration is null || _mainViewModel is null)
            {
                return;
            }

            IReadOnlyList<string> changed;
            try
            {
                changed = _settingsConfiguration.Reload(AiRuntimeConfigurationMapper.CreateOverrides(_mainViewModel.SettingsService));
            }
            catch (Exception ex)
            {
                _errorHandlingService?.LogError(ex, "Settings could not be applied");
                return;
            }

            if (changed.Count == 0)
            {
                return;
            }

            _mainViewModel.OnRuntimeSettingsApplied(changed);
            if (changed.Any(key => key.StartsWith("McpOptions:", StringComparison.OrdinalIgnoreCase))
                && GetCurrentServiceProvider()?.GetService<IMcpHost>() is { } mcpHost)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await mcpHost.ReloadAsync();
                    }
                    catch (Exception ex)
                    {
                        _errorHandlingService?.LogError(ex, "MCP servers failed to reload after a Settings change");
                    }
                });
            }
        }

        private void LogConfigurationDebugInfo(IConfiguration configuration)
        {
            Console.WriteLine("📋 Configuration Sources:");
            if (configuration is ConfigurationRoot root)
            {
                foreach (var provider in root.Providers)
                {
                    Console.WriteLine($"  - {provider.GetType().Name}");
                }
            }

            // Check assembly for UserSecretsId attribute
            var assembly = typeof(App).Assembly;
            var userSecretsAttr = assembly.GetCustomAttribute<Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute>();
            Console.WriteLine($"📋 Assembly UserSecretsId: {userSecretsAttr?.UserSecretsId ?? "(not set)"}");

            // Check UserSecrets file directly
            var userSecretsId = userSecretsAttr?.UserSecretsId ?? "c596b7d6-7516-451c-b4fc-598ea1e7ddc6";
            var userSecretsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "UserSecrets", userSecretsId, "secrets.json");
            
            Console.WriteLine($"📋 UserSecrets path: {userSecretsPath}");
            Console.WriteLine($"📋 UserSecrets exists: {File.Exists(userSecretsPath)}");
            
            if (File.Exists(userSecretsPath))
            {
                try
                {
                    var content = File.ReadAllText(userSecretsPath);
                    Console.WriteLine($"📋 UserSecrets content length: {content.Length}");
                    // Check if AIService is in the content
                    if (content.Contains("AIService"))
                    {
                        Console.WriteLine("📋 AIService found in secrets.json content");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ AIService NOT found in secrets.json content");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Error reading secrets.json: {ex.Message}");
                }
            }

            // List all sections
            Console.WriteLine("📋 All configuration sections:");
            foreach (var section in configuration.GetChildren())
            {
                Console.WriteLine($"  - {section.Key}");
            }

            var aiServiceSection = configuration.GetSection("AIService");
            Console.WriteLine($"📋 AIService section exists: {aiServiceSection.Exists()}");
            if (aiServiceSection.Exists())
            {
                Console.WriteLine($"  Provider: {aiServiceSection["Provider"] ?? "(null)"}");
                Console.WriteLine($"  Endpoint: {aiServiceSection["Endpoint"] ?? "(null)"}");
                Console.WriteLine($"  ModelId: {aiServiceSection["ModelId"] ?? "(null)"}");
                Console.WriteLine($"  ApiKey: {(string.IsNullOrEmpty(aiServiceSection["ApiKey"]) ? "(not set)" : "(set)")}");
            }
            else
            {
                Console.WriteLine("⚠️ AIService configuration not found in loaded configuration!");
            }
        }

        private static bool ShouldLogConfigurationDebugInfo(IConfiguration configuration)
        {
            return IsEnabled(configuration["Kam:Diagnostics:LogConfiguration"])
                || IsEnabled(Environment.GetEnvironmentVariable("KAM_LOG_CONFIGURATION"));
        }

        private static bool IsEnabled(string? value)
        {
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets up global exception handling for the application
        /// </summary>
        private void SetupGlobalExceptionHandling()
        {
            // Initialize error handling service early
            _errorHandlingService = new ErrorHandlingService();
            _errorHandlingService.LogInformation("Application starting - Global exception handling initialized");

            // Handle UI thread exceptions
            Dispatcher.UIThread.UnhandledException += (sender, e) =>
            {
                Console.WriteLine($"UI THREAD ERROR: {e.Exception.Message}");
                _errorHandlingService?.LogError(e.Exception, "Unhandled Avalonia UI thread exception");
            };

            AppDomain.CurrentDomain.UnhandledException += async (sender, e) =>
            {
                var exception = e.ExceptionObject as Exception;
                Console.WriteLine($"💥 FATAL ERROR: {exception?.Message}");
                Console.WriteLine(exception?.StackTrace);
                
                if (exception != null && _errorHandlingService != null)
                {
                    await _errorHandlingService.HandleFatalExceptionAsync(exception);
                }
            };

            // Handle task exceptions
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.WriteLine($"💥 UNOBSERVED TASK ERROR: {e.Exception.Message}");
                _errorHandlingService?.HandleUnobservedException(e.Exception);
                e.SetObserved(); // Prevent crash
            };
        }

        private async Task StartHostAsync(IHost host)
        {
            try
            {
                await host.StartAsync();
            }
            catch (Exception ex)
            {
                _errorHandlingService?.LogError(ex, "Host failed to start");
                _mainViewModel?.ReportHostStartFailure(ex.Message);
            }
        }

        /// <summary>
        /// Sets up the voice command service
        /// </summary>
        private IServiceProvider? GetCurrentServiceProvider()
        {
            return _applicationScope?.ServiceProvider ?? _host?.Services;
        }

        private async Task WarmUpMcpServersAsync(IMcpHost? mcpHost)
        {
            if (mcpHost is null)
            {
                return;
            }

            try
            {
                await Task.Run(() => mcpHost.GetToolsAsync());
            }
            catch (Exception ex)
            {
                _errorHandlingService?.LogError(ex, "MCP servers failed to start in the background");
            }
        }

        private void SetupVoiceAssistant(MainWindowViewModel viewModel, IServiceProvider services)
        {
            try
            {
                var voice = new VoiceAssistant(
                    services.GetRequiredService<IVoiceRecognitionFactory>(),
                    services.GetRequiredService<IMultiSTTService>(),
                    services.GetRequiredService<IWakeWordDetectionService>(),
                    services.GetRequiredService<ISpeechModelStore>(),
                    services.GetRequiredService<ITextToSpeechService>(),
                    services.GetRequiredService<IConfiguration>(),
                    services.GetService<INoiseSuppressionService>(),
                    services.GetService<IUiLogService>());

                viewModel.SetVoiceAssistant(voice);
                _errorHandlingService?.LogInformation("Voice assistant initialized");
            }
            catch (Exception ex)
            {
                // Chat works without voice; the talk button explains that voice is unavailable.
                _errorHandlingService?.LogError(ex, "Failed to initialize the voice assistant");
            }
        }

        private void ValidateConfiguration()
        {
            if (_host == null) return;

            _errorHandlingService?.LogInformation("Validating configuration...");
            
            var configuration = _host.Services.GetRequiredService<IConfiguration>();
            
            // Check AIService configuration
            var aiServiceSection = configuration.GetSection("AIService");
            if (!aiServiceSection.Exists())
            {
                _errorHandlingService?.LogWarning("AIService configuration not found");
                Console.WriteLine("⚠️ WARNING: AIService configuration not found!");
                Console.WriteLine("   AI features will not work without API configuration.");
                Console.WriteLine("   Configure an AI provider in Settings before using AI features.");
            }
            else if (string.IsNullOrEmpty(aiServiceSection["ApiKey"]))
            {
                _errorHandlingService?.LogWarning("AIService:ApiKey is not set");
                Console.WriteLine("⚠️ WARNING: AIService:ApiKey is not set!");
                Console.WriteLine("   AI features will not work without an API key.");
            }
            else
            {
                _errorHandlingService?.LogInformation("AIService configuration validated");
            }

            // Check Voice Recognition configuration
            var voiceSection = configuration.GetSection("VoiceRecognition");
            if (!voiceSection.Exists())
            {
                _errorHandlingService?.LogWarning("VoiceRecognition configuration not found. Using defaults");
                Console.WriteLine("⚠️ WARNING: VoiceRecognition configuration not found. Using defaults.");
            }

            // Check HuggingFace configuration for STT
            var hfSection = configuration.GetSection("HuggingFaceConfig");
            if (!hfSection.Exists() || string.IsNullOrEmpty(hfSection["ApiKey"]))
            {
                _errorHandlingService?.LogWarning("HuggingFaceConfig:ApiKey not found. Voice transcription will not work");
                Console.WriteLine("⚠️ WARNING: HuggingFaceConfig:ApiKey not found!");
                Console.WriteLine("   Voice transcription will not work without API key.");
                Console.WriteLine("   Configure HuggingFaceConfig:ApiKey through your local secret/configuration source.");
            }
            else
            {
                _errorHandlingService?.LogInformation("HuggingFace configuration validated");
            }
        }
    }
}
