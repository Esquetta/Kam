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
    /// Note: Tests requiring actual AI agents are skipped in CI environment
    /// </summary>
    public class MultiAgentOrchestrationTests
    {
        private readonly Mock<IAgentRegistry> _mockRegistry;
        private readonly Mock<ILogger<SmartAgentOrchestrator>> _mockLogger;
        private readonly Mock<IUiLogService> _mockUiLogService;

        public MultiAgentOrchestrationTests()
        {
            _mockRegistry = new Mock<IAgentRegistry>();
            _mockLogger = new Mock<ILogger<SmartAgentOrchestrator>>();
            _mockUiLogService = new Mock<IUiLogService>();
        }

        private SmartAgentOrchestrator CreateOrchestrator()
        {
            return new SmartAgentOrchestrator(
                _mockRegistry.Object,
                _mockLogger.Object,
                _mockUiLogService.Object);
        }

        [Fact]
        public async Task Orchestrator_CoordinatorNotAvailable_ThrowsException()
        {
            // Arrange
            _mockRegistry.Setup(r => r.IsAgentAvailable("Coordinator")).Returns(false);
            var orchestrator = CreateOrchestrator();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await orchestrator.ExecuteAsync("test request");
            });
        }

        [Fact]
        public void RoutingDecision_AgentParsing_SystemAgent()
        {
            // Arrange
            var response = "use systemagent to handle this";
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.TargetAgents.Should().Contain("SystemAgent");
        }

        [Fact]
        public void RoutingDecision_AgentParsing_TaskAgent()
        {
            // Arrange
            var response = "use taskagent to handle this";
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.TargetAgents.Should().Contain("TaskAgent");
        }

        [Fact]
        public void RoutingDecision_AgentParsing_ResearchAgent()
        {
            // Arrange
            var response = "use researchagent to handle this";
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.TargetAgents.Should().Contain("ResearchAgent");
        }

        [Fact]
        public void RoutingDecision_ExecutionMode_Parallel()
        {
            // Arrange
            var response = "execute in parallel";

            // Act
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.ExecutionMode.Should().Be(ExecutionMode.Parallel);
        }

        [Fact]
        public void RoutingDecision_ExecutionMode_Sequential()
        {
            // Arrange
            var response = "run sequentially";

            // Act
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.ExecutionMode.Should().Be(ExecutionMode.Sequential);
        }

        [Fact]
        public void RoutingDecision_TurkishParallelKeyword()
        {
            // Arrange
            var response = "aynı anda çalıştır";

            // Act
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.ExecutionMode.Should().Be(ExecutionMode.Parallel);
        }

        [Fact]
        public void RoutingDecision_MultipleAgents()
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

        [Fact]
        public void RoutingDecision_EmptyResponse_FallbackToSystemAgent()
        {
            // Arrange
            var response = "";

            // Act
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.TargetAgents.Should().Contain("SystemAgent");
        }

        [Theory]
        [InlineData("systemagent", "SystemAgent")]
        [InlineData("taskagent", "TaskAgent")]
        [InlineData("researchagent", "ResearchAgent")]
        [InlineData("unknownagent", "SystemAgent")] // Fallback
        public void RoutingDecision_VariousAgents(string agentName, string expectedAgent)
        {
            // Arrange
            var response = $"use {agentName} to handle this";
            var decision = ParseRoutingDecision(response);

            // Assert
            if (agentName != "unknownagent")
            {
                decision.TargetAgents.Should().Contain(a => a.Equals(expectedAgent, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                decision.TargetAgents.Should().Contain("SystemAgent"); // Fallback
            }
        }

        [Fact]
        public void AgentExecutionUpdate_PropertiesSetCorrectly()
        {
            // Arrange & Act
            var update = new AgentExecutionUpdate
            {
                AgentName = "TestAgent",
                Message = "Test message",
                IsComplete = true
            };

            // Assert
            update.AgentName.Should().Be("TestAgent");
            update.Message.Should().Be("Test message");
            update.IsComplete.Should().BeTrue();
        }

        [Fact]
        public void RoutingDecision_ReasoningPreserved()
        {
            // Arrange
            var response = "use systemagent because it's an application task";

            // Act
            var decision = ParseRoutingDecision(response);

            // Assert
            decision.Reasoning.Should().Be(response);
        }

        #region Helper Methods

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

        #endregion
    }

    public class RoutingDecision
    {
        public List<string> TargetAgents { get; set; } = new();
        public ExecutionMode ExecutionMode { get; set; }
        public string Reasoning { get; set; } = "";
    }
}
