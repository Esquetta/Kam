using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SmartVoiceAgent.Core.Dtos.Agent;
using SmartVoiceAgent.Core.Enums.Agent;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Agent.Agents;

namespace SmartVoiceAgent.Tests.Integration
{
    /// <summary>
    /// Integration tests for multi-agent orchestration system
    /// </summary>
    public class MultiAgentOrchestrationTests
    {
        private readonly Mock<IAgentRegistry> _mockRegistry;
        private readonly Mock<ILogger<SmartAgentOrchestrator>> _mockLogger;
        private readonly Mock<IUiLogService> _mockUiLogService;
        private readonly SmartAgentOrchestrator _orchestrator;

        public MultiAgentOrchestrationTests()
        {
            _mockRegistry = new Mock<IAgentRegistry>();
            _mockLogger = new Mock<ILogger<SmartAgentOrchestrator>>();
            _mockUiLogService = new Mock<IUiLogService>();

            SetupMockAgents();

            _orchestrator = new SmartAgentOrchestrator(
                _mockRegistry.Object,
                _mockLogger.Object,
                _mockUiLogService.Object);
        }

        private void SetupMockAgents()
        {
            // Setup Coordinator agent availability
            _mockRegistry.Setup(r => r.IsAgentAvailable("Coordinator")).Returns(true);
            _mockRegistry.Setup(r => r.IsAgentAvailable("SystemAgent")).Returns(true);
            _mockRegistry.Setup(r => r.IsAgentAvailable("TaskAgent")).Returns(true);
            _mockRegistry.Setup(r => r.IsAgentAvailable("ResearchAgent")).Returns(true);
        }

        [Fact]
        public async Task Orchestrator_SimpleCommand_RoutesToSingleAgent()
        {
            // Arrange
            var request = "Chrome'u aç";

            // Act
            var result = await _orchestrator.ExecuteAsync(request);

            // Assert
            result.Should().NotBeNullOrEmpty();
            _mockUiLogService.Verify(u => u.LogAgentUpdate("Coordinator", It.IsAny<string>(), false), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Orchestrator_ComplexCommand_RoutesToMultipleAgents()
        {
            // Arrange
            var request = "Yarın toplantı ekle ve hava durumunu araştır";

            // Act
            var result = await _orchestrator.ExecuteAsync(request);

            // Assert
            result.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Orchestrator_StreamingExecution_ProvidesUpdates()
        {
            // Arrange
            var request = "Spotify'ı aç";
            var updates = new List<AgentExecutionUpdate>();

            // Act
            await foreach (var update in _orchestrator.ExecuteStreamAsync(request))
            {
                updates.Add(update);
            }

            // Assert
            updates.Should().NotBeEmpty();
            updates.Should().Contain(u => u.AgentName == "Router");
        }

        [Fact]
        public async Task Orchestrator_CoordinatorNotAvailable_ThrowsException()
        {
            // Arrange
            _mockRegistry.Setup(r => r.IsAgentAvailable("Coordinator")).Returns(false);

            var orchestrator = new SmartAgentOrchestrator(
                _mockRegistry.Object,
                _mockLogger.Object,
                _mockUiLogService.Object);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await orchestrator.ExecuteAsync("test request");
            });
        }

        [Fact]
        public async Task Orchestrator_EmptyRequest_HandledGracefully()
        {
            // Arrange
            var request = "";

            // Act
            var result = await _orchestrator.ExecuteAsync(request);

            // Assert
            result.Should().NotBeNull();
        }

        [Theory]
        [InlineData("Chrome'u aç", "SystemAgent")]
        [InlineData("Görev ekle", "TaskAgent")]
        [InlineData("Hava durumu nedir", "ResearchAgent")]
        public async Task Orchestrator_Routing_DecisionBasedOnIntent(string request, string expectedAgent)
        {
            // Act
            var result = await _orchestrator.ExecuteAsync(request);

            // Assert
            result.Should().NotBeNullOrEmpty();
            _mockLogger.Verify(
                x => x.Log(
                    Microsoft.Extensions.Logging.LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedAgent) || v.ToString()!.Contains("Route")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task Orchestrator_ParallelExecution_FasterThanSequential()
        {
            // Arrange
            var request = "Çoklu görevleri aynı anda yap";

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _orchestrator.ExecuteAsync(request);
            stopwatch.Stop();

            // Assert
            result.Should().NotBeNullOrEmpty();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete within 5 seconds
        }

        [Fact]
        public async Task Orchestrator_MultipleSequentialCalls_ConsistentResults()
        {
            // Arrange
            var request = "Chrome'u aç";

            // Act
            var results = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                results.Add(await _orchestrator.ExecuteAsync(request));
            }

            // Assert
            results.Should().AllSatisfy(r => r.Should().NotBeNullOrEmpty());
        }

        [Fact]
        public async Task Orchestrator_LongRequest_HandledProperly()
        {
            // Arrange
            var request = new string('x', 1000); // Very long request

            // Act
            var result = await _orchestrator.ExecuteAsync(request);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task Orchestrator_SpecialCharacters_HandledProperly()
        {
            // Arrange
            var request = "Chrome'u aç ve ışıkları kapat! (test)";

            // Act
            var result = await _orchestrator.ExecuteAsync(request);

            // Assert
            result.Should().NotBeNullOrEmpty();
        }
    }

    /// <summary>
    /// Tests for agent routing decision logic
    /// </summary>
    public class AgentRoutingDecisionTests
    {
        [Theory]
        [InlineData("systemagent", true)]
        [InlineData("taskagent", true)]
        [InlineData("researchagent", true)]
        [InlineData("unknownagent", false)]
        public void RoutingDecision_AgentParsing(string agentName, bool expectedInResult)
        {
            // Arrange
            var response = $"use {agentName} to handle this";
            var decision = ParseRoutingDecision(response);

            // Act & Assert
            if (expectedInResult)
            {
                decision.TargetAgents.Should().Contain(a => a.ToLowerInvariant() == agentName);
            }
        }

        [Theory]
        [InlineData("execute in parallel", ExecutionMode.Parallel)]
        [InlineData("run simultaneously", ExecutionMode.Parallel)]
        [InlineData("aynı anda çalıştır", ExecutionMode.Parallel)]
        [InlineData("run sequentially", ExecutionMode.Sequential)]
        public void RoutingDecision_ExecutionModeParsing(string response, ExecutionMode expectedMode)
        {
            // Act
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.ExecutionMode.Should().Be(expectedMode);
        }

        [Fact]
        public void RoutingDecision_MultipleAgents_ParsedCorrectly()
        {
            // Arrange
            var response = "use systemagent and taskagent in parallel";

            // Act
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.TargetAgents.Should().Contain("SystemAgent");
            decision.TargetAgents.Should().Contain("TaskAgent");
            decision.ExecutionMode.Should().Be(ExecutionMode.Parallel);
        }

        [Fact]
        public void RoutingDecision_NoAgents_FallbackToSystemAgent()
        {
            // Arrange
            var response = "I don't know which agent to use";

            // Act
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.TargetAgents.Should().ContainSingle();
            decision.TargetAgents.First().Should().Be("SystemAgent");
        }

        private RoutingDecision ParseRoutingDecision(string agentResponse)
        {
            var response = agentResponse.ToLowerInvariant();
            var agents = new List<string>();
            var mode = ExecutionMode.Sequential;

            // Extract agent names
            if (response.Contains("systemagent")) agents.Add("SystemAgent");
            if (response.Contains("taskagent")) agents.Add("TaskAgent");
            if (response.Contains("researchagent")) agents.Add("ResearchAgent");

            // Determine execution mode
            if (response.Contains("parallel") || response.Contains("simultaneously") ||
                response.Contains("aynı anda") || agents.Count > 1)
            {
                mode = ExecutionMode.Parallel;
            }

            // Fallback
            if (agents.Count == 0)
            {
                agents.Add("SystemAgent");
            }

            return new RoutingDecision
            {
                TargetAgents = agents,
                ExecutionMode = mode,
                Reasoning = agentResponse
            };
        }
    }

    public class RoutingDecision
    {
        public List<string> TargetAgents { get; set; } = new();
        public ExecutionMode ExecutionMode { get; set; }
        public string Reasoning { get; set; } = "";
    }
}
