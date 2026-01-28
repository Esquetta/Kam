using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.ViewModels;
using SmartVoiceAgent.Ui.Views;
using System;
using System.IO;
using System.Linq;
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

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                DisableAvaloniaDataAnnotationValidation();

                // Build the host with backend services
                _host = BuildHost();

                // Initialize ViewModel
                _mainViewModel = new MainWindowViewModel();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = _mainViewModel
                };

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

                // Start the host
                _host.StartAsync();

                desktop.ShutdownRequested += async (s, e) =>
                {
                    _mainViewModel.Cleanup();
                    _trayIconService?.Dispose();
                    
                    if (_host != null)
                    {
                        await _host.StopAsync();
                        _host.Dispose();
                    }
                };
            }

            base.OnFrameworkInitializationCompleted();
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
                    // Just add additional config if needed
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

        private void DisableAvaloniaDataAnnotationValidation()
        {
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
    }
}
