using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.ViewModels;
using SmartVoiceAgent.Ui.Views;
using SmartVoiceAgent.Infrastructure.Skills.Policy;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration.Json;
using System.Reflection;

namespace SmartVoiceAgent.Ui
{
    public partial class App : Avalonia.Application
    {
        private TrayIconService? _trayIconService;
        private MainWindowViewModel? _mainViewModel;
        private IHost? _host;
        private UiLogService? _uiLogService;
        private ErrorHandlingService? _errorHandlingService;

        /// <summary>
        /// Gets the service provider for dependency injection access from ViewModels
        /// </summary>
        public static IServiceProvider? Services => (Current as App)?._host?.Services;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Setup global exception handlers
            SetupGlobalExceptionHandling();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Build the host with backend services
                _host = BuildHost();

                // Validate critical configuration
                ValidateConfiguration();

                // Load startup settings
                var settingsService = new JsonSettingsService();
                
                // Initialize ViewModel
                _mainViewModel = new MainWindowViewModel();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = _mainViewModel
                };

                // Set main window reference for error handling service
                _errorHandlingService?.SetMainWindow(desktop.MainWindow);
                _errorHandlingService?.LogInformation("Main window initialized");

                // Setup Tray Icon
                _trayIconService = new TrayIconService();
                _trayIconService.Initialize();
                _mainViewModel.SetTrayIconService(_trayIconService);

                // Connect UI Log Service to ViewModel
                _uiLogService = (UiLogService?)_host.Services.GetService<IUiLogService>();
                _uiLogService?.SetViewModel(_mainViewModel);

                // Connect Command Input Service to ViewModel
                var commandInput = _host.Services.GetRequiredService<ICommandInputService>();
                _mainViewModel.SetCommandInputService(commandInput);

                // Connect VoiceAgent Host Control to ViewModel
                var hostControl = _host.Services.GetRequiredService<IVoiceAgentHostControl>();
                _mainViewModel.SetVoiceAgentHostControl(hostControl);

                // Connect Skill Health Service to the skills dashboard
                var skillHealthService = _host.Services.GetRequiredService<ISkillHealthService>();
                _mainViewModel.SetSkillHealthService(skillHealthService);

                var skillEvalHarness = _host.Services.GetRequiredService<ISkillEvalHarness>();
                var skillEvalCaseCatalog = _host.Services.GetRequiredService<ISkillEvalCaseCatalog>();
                _mainViewModel.SetSkillEvalServices(skillEvalHarness, skillEvalCaseCatalog);

                var skillPolicyManager = _host.Services.GetRequiredService<ISkillPolicyManager>();
                _mainViewModel.SetSkillPolicyManager(skillPolicyManager);

                var skillConfirmationService = _host.Services.GetRequiredService<ISkillConfirmationService>();
                _mainViewModel.SetSkillConfirmationService(skillConfirmationService);

                // Setup Voice Command Service
                SetupVoiceCommandService(_mainViewModel);

                // Apply startup behavior settings
                ApplyStartupBehavior(desktop, settingsService);

                // Start the host
                _host.StartAsync();

                desktop.ShutdownRequested += async (s, e) =>
                {
                    _errorHandlingService?.LogInformation("Application shutting down...");
                    settingsService.Dispose();
                    _mainViewModel.Cleanup();
                    _trayIconService?.Dispose();
                    
                    if (_host != null)
                    {
                        await _host.StopAsync();
                        _host.Dispose();
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
            // Set environment to Development to enable User Secrets
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");

            return Host.CreateDefaultBuilder()
                .UseEnvironment("Development")
                .ConfigureAppConfiguration((context, config) =>
                {
                    // CreateDefaultBuilder already adds:
                    // - appsettings.json
                    // - UserSecrets (when Environment is Development)
                    // - EnvironmentVariables
                    AddUserAiRuntimeConfiguration(config);
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;

                    // Debug: Log configuration
                    LogConfigurationDebugInfo(configuration);

                    // Register Application services
                    services.AddApplicationServices();

                    // Register Infrastructure services
                    services.AddInfrastructureServices(configuration);

                    // Register Smart Voice Agent services
                    services.AddSmartVoiceAgent(configuration);

                    // Register our custom UI Log Service (replaces the dummy one)
                    services.AddSingleton<IUiLogService>(sp => new UiLogService());
                })
                .Build();
        }

        private static void AddUserAiRuntimeConfiguration(IConfigurationBuilder config)
        {
            try
            {
                using var settingsService = new JsonSettingsService();
                var overrides = AiRuntimeConfigurationMapper.CreateAiServiceOverrides(
                    settingsService.ModelProviderProfiles,
                    settingsService.ActivePlannerProfileId);

                if (overrides.Count > 0)
                {
                    config.AddInMemoryCollection(overrides);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AI runtime settings could not be loaded: {ex.Message}");
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

        /// <summary>
        /// Sets up global exception handling for the application
        /// </summary>
        private void SetupGlobalExceptionHandling()
        {
            // Initialize error handling service early
            _errorHandlingService = new ErrorHandlingService();
            _errorHandlingService.LogInformation("Application starting - Global exception handling initialized");

            // Handle UI thread exceptions
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

        /// <summary>
        /// Sets up the voice command service
        /// </summary>
        private void SetupVoiceCommandService(MainWindowViewModel viewModel)
        {
            if (_host == null) return;

            try
            {
                _errorHandlingService?.LogInformation("Initializing Voice Command Service...");
                
                var wakeWordService = _host.Services.GetRequiredService<IWakeWordDetectionService>();
                var voiceRecognitionFactory = _host.Services.GetRequiredService<IVoiceRecognitionFactory>();
                var sttService = _host.Services.GetRequiredService<IMultiSTTService>();
                var noiseSuppression = _host.Services.GetRequiredService<INoiseSuppressionService>();
                var commandInput = _host.Services.GetRequiredService<ICommandInputService>();
                var uiLogService = _host.Services.GetRequiredService<IUiLogService>();

                var voiceCommandService = new VoiceCommandService(
                    wakeWordService,
                    voiceRecognitionFactory,
                    sttService,
                    noiseSuppression,
                    commandInput,
                    uiLogService);

                viewModel.SetVoiceCommandService(voiceCommandService);
                _errorHandlingService?.LogInformation("Voice Command Service initialized successfully");
                Console.WriteLine("✅ Voice Command Service initialized");
            }
            catch (Exception ex)
            {
                _errorHandlingService?.LogError(ex, "Failed to initialize Voice Command Service");
                Console.WriteLine($"⚠️ Failed to initialize Voice Command Service: {ex.Message}");
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
                Console.WriteLine("   Run: dotnet user-secrets set \"AIService:ApiKey\" \"your-key\"");
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
                Console.WriteLine("   Run: dotnet user-secrets set \"HuggingFaceConfig:ApiKey\" \"your-hf-key\"");
            }
            else
            {
                _errorHandlingService?.LogInformation("HuggingFace configuration validated");
            }
        }
    }
}
