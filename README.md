# 🤖 Smart Voice Agent

> An advanced AI-powered voice assistant with multi-agent collaboration, system control, and intelligent task management capabilities.

[![.NET Version](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey.svg)](https://github.com/yourusername/SmartVoiceAgent)

## 🌟 Features

### 🎙️ Voice Recognition & Processing
- **Multi-Platform Voice Input**: Windows (NAudio), Linux (arecord), macOS (sox)
- **Advanced Voice Detection**: Adaptive threshold with noise filtering
- **Multiple STT Providers**: HuggingFace, OpenAI Whisper, Ollama
- **Real-time Audio Processing**: Circular buffer with intelligent speech detection

### 🤖 Multi-Agent AI System
- **Coordinator Agent**: Intelligent request routing and workflow orchestration
- **SystemAgent**: Application control, device management, system operations
- **TaskAgent**: Task management with Todoist integration via MCP
- **WebSearchAgent**: AI-powered web research and information retrieval
- **AnalyticsAgent**: Performance monitoring and usage insights

### 💡 Advanced AI Capabilities
- **Hybrid Intent Detection**: AI + Semantic + Context-aware + Pattern-based
- **Context Management**: Conversation history and user preference learning
- **Dynamic Command Handling**: Flexible entity extraction and execution
- **Multi-Model Support**: OpenRouter, HuggingFace, local Ollama integration

### 🖥️ System Control
- **Application Management**: Open, close, list installed applications
- **Device Control**: Volume, brightness, WiFi, Bluetooth
- **Power Management**: Shutdown, restart, sleep, lock
- **Screen Capture**: Multi-monitor support with OCR and object detection

### 🔧 Enterprise Features
- **CQRS Pattern**: MediatR-based command/query separation
- **Caching Layer**: Distributed caching with smart invalidation
- **Logging**: MongoDB, Serilog with multiple sinks
- **Validation**: FluentValidation pipeline
- **Performance Monitoring**: Built-in metrics and analytics

## 📋 Prerequisites

- **.NET 9.0 SDK** or later
- **Operating System**: Windows 10/11, Ubuntu 20.04+, or macOS 11+
- **API Keys** (optional but recommended):
  - OpenRouter API key for AI features
  - HuggingFace API key for advanced models
  - Google Custom Search API for web research
  - Todoist API for task management

## 🚀 Quick Start

### 1. Clone the Repository

```bash
git clone https://github.com/yourusername/SmartVoiceAgent.git
cd SmartVoiceAgent
```

### 2. Configure API Keys

Create or edit `appsettings.json`:

```json
{
  "OpenRouter": {
    "ApiKey": "your-openrouter-api-key",
    "Model": "microsoft/wizardlm-2-8x22b",
    "EndPoint": "https://openrouter.ai/api/v1"
  },
  "HuggingFaceConfig": {
    "ApiKey": "your-huggingface-api-key",
    "ModelName": "openai/whisper-large-v3",
    "MaxConcurrentRequests": 3,
    "MaxAudioSizeBytes": 26214400
  },
  "WebResearch": {
    "SearchApiKey": "your-google-api-key",
    "SearchEngineId": "your-search-engine-id"
  },
  "Mcpverse": {
    "TodoistApiKey": "your-todoist-api-key",
    "TodoistServerLink": "https://todoist.mcpverse.dev/mcp"
  },
  "MongoDbConfiguration": {
    "ConnectionString": "mongodb://localhost:27017",
    "Database": "SmartVoiceAgentLogs",
    "Collection": "Logs"
  }
}
```

### 3. Install Dependencies

```bash
dotnet restore
```

### 4. Build the Project

```bash
dotnet build
```

### 5. Run the Application

```bash
dotnet run --project src/SmartVoiceAgent.Presentation/SmartVoiceAgent.Presentation.csproj
```

## 🏗️ Architecture

### Project Structure

```
SmartVoiceAgent/
├── src/
│   ├── SmartVoiceAgent.Core/           # Domain entities, interfaces, enums
│   ├── SmartVoiceAgent.Application/    # CQRS commands, queries, handlers
│   ├── SmartVoiceAgent.Infrastructure/ # External services, AI agents, platforms
│   ├── SmartVoiceAgent.CrossCuttingConcerns/ # Logging, exceptions
│   └── SmartVoiceAgent.Presentation/   # Entry point, configuration
├── tests/                               # Unit and integration tests
└── docs/                                # Documentation
```

### Key Components

#### 1. Multi-Agent System
```
User Input → Coordinator Agent → [SystemAgent | TaskAgent | WebSearchAgent] → Response
                    ↓
            AnalyticsAgent (monitoring)
```

#### 2. Intent Detection Pipeline
```
User Speech → STT → Hybrid Intent Detection → Dynamic Command Handler → Execution
                     ├─ AI-based
                     ├─ Semantic similarity
                     ├─ Context-aware
                     └─ Pattern matching
```

#### 3. Command Processing
```
Voice Input → Audio Processing → Speech-to-Text → Intent Detection → 
Command Routing → MediatR Pipeline → Handler → Response
```

## 📚 Usage Examples

### Voice Commands

```typescript
// Application Control
"Spotify'ı aç"           → Opens Spotify
"Chrome'u kapat"         → Closes Chrome
"Uygulamaları listele"   → Lists installed apps

// Task Management
"Yarın saat 9'a toplantı ekle"  → Creates task in Todoist
"Görevlerimi göster"             → Lists tasks
"Alışveriş görevini sil"        → Deletes task

// Web Research
"Python öğrenmek için kaynaklar ara"  → AI-powered web research
"Son teknoloji haberlerini bul"       → Opens relevant news

// System Control
"Sesi artır"              → Increases volume
"Ekranı kapat"            → Turns off screen
"WiFi'yi aç"              → Enables WiFi
```

### Programmatic Usage

```csharp
// Using the Multi-Agent System
var groupChat = await GroupChatAgentFactory.CreateGroupChatAsync(
    apiKey: configuration["AiAgent:Apikey"],
    model: configuration["AiAgent:Model"],
    serviceProvider: serviceProvider,
    endpoint: configuration["AiAgent:EndPoint"],
    configuration
);

await groupChat.SendWithAnalyticsAsync("Spotify'ı aç");

// Using Intent Detection
var intentResult = await intentDetectionService.DetectIntentAsync(
    "Yarına toplantı ekle", 
    "tr"
);

// Using Command Handler
var command = new OpenApplicationCommand("Chrome");
var result = await mediator.Send(command);

// Using Web Research
var research = await webResearchService.SearchAndOpenAsync(
    new WebResearchRequest 
    { 
        Query = "machine learning tutorials",
        Language = "en",
        MaxResults = 5 
    }
);
```

## 🔧 Configuration

### Voice Recognition

```json
{
  "VoiceRecognition": {
    "Provider": "HuggingFace",
    "SampleRate": 16000,
    "Channels": 1,
    "BitsPerSample": 16,
    "BufferCapacitySeconds": 30
  }
}
```

### Agent System

```json
{
  "AiAgent": {
    "Model": "microsoft/wizardlm-2-8x22b",
    "Temperature": 0.7,
    "MaxTokens": 1000,
    "EnableAnalyticsAgent": true,
    "EnableWebSearchAgent": true,
    "EnableContextMemory": true
  }
}
```

### Caching

```json
{
  "CacheSettings": {
    "SlidingExpiration": 5
  }
}
```

## 🧪 Testing

### Run Unit Tests

```bash
dotnet test tests/SmartVoiceAgent.UnitTests
```

### Run Integration Tests

```bash
dotnet test tests/SmartVoiceAgent.IntegrationTests
```

### Test Voice Recognition

```csharp
var voiceService = serviceProvider.GetService<IVoiceRecognitionService>();
voiceService.StartListening();
// Speak into microphone
voiceService.StopListening();
```

## 🤝 Contributing

We welcome contributions! Please follow these steps:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

### Code Standards

- Follow C# coding conventions
- Write unit tests for new features
- Update documentation
- Use meaningful commit messages

## 📖 Documentation

- [Architecture Overview](docs/ARCHITECTURE.md)
- [API Reference](docs/API.md)
- [Agent System Guide](docs/AGENTS.md)
- [Intent Detection](docs/INTENT_DETECTION.md)
- [Deployment Guide](docs/DEPLOYMENT.md)

## 🐛 Troubleshooting

### Voice Recognition Not Working

```bash
# Check microphone permissions
# Windows: Settings → Privacy → Microphone
# Linux: pulseaudio -k && pulseaudio --start
# macOS: System Preferences → Security & Privacy → Microphone
```

### API Connection Issues

```bash
# Verify API keys in appsettings.json
# Check network connectivity
# Review logs in MongoDB or console
```

### Application Not Opening

```bash
# Check if application is installed
# Verify application path in registry/shortcuts
# Run with elevated permissions if needed
```

## 📊 Performance

- **Voice Detection Latency**: <100ms
- **Intent Detection**: 200-500ms (hybrid mode)
- **Command Execution**: 100-1000ms (depends on operation)
- **Memory Usage**: ~150-300MB
- **CPU Usage**: 5-15% idle, 30-50% during voice processing

## 🔐 Security

- API keys stored in user secrets or environment variables
- No sensitive data logging
- Process isolation for system commands
- Secure HTTP communication with external APIs

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 👥 Authors

- **Your Name** - *Initial work* - [YourGitHub](https://github.com/yourusername)

## 🙏 Acknowledgments

- OpenRouter for AI model access
- HuggingFace for STT and language models
- AutoGen for multi-agent framework
- MediatR for CQRS implementation
- Todoist for task management integration

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/SmartVoiceAgent/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/SmartVoiceAgent/discussions)
- **Email**: your.email@example.com

## 🗺️ Roadmap

- [ ] Mobile app support (iOS/Android)
- [ ] Custom wake word detection
- [ ] Plugin system for extensibility
- [ ] Multi-language UI
- [ ] Voice cloning for responses
- [ ] Smart home device integration
- [ ] Cloud synchronization
- [ ] Offline mode improvements

---

**Made with ❤️ by the Smart Voice Agent Team**
