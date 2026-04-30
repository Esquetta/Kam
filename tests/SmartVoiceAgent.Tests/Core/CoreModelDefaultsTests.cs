using FluentAssertions;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Dtos.Screen;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.EventArgs;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Core.Models.Intent;

namespace SmartVoiceAgent.Tests.Core;

public class CoreModelDefaultsTests
{
    [Fact]
    public void CoreModels_WithParameterlessConstructors_HaveNonNullCollectionAndTextDefaults()
    {
        new WebResearchResult().Should().BeEquivalentTo(new
        {
            Title = string.Empty,
            Url = string.Empty,
            Description = string.Empty
        });
        new WebResearchRequest().Query.Should().BeEmpty();

        new VoiceProcessedEventArgs().Intent.Should().NotBeNull();
        new VoiceProcessedEventArgs().Speech.Should().NotBeNull();
        new VoiceProcessedEventArgs().Language.Should().NotBeNull();

        var pipelineError = new PipelineErrorEventArgs();
        pipelineError.Exception.Should().NotBeNull();
        pipelineError.Stage.Should().BeEmpty();
        pipelineError.Context.Should().BeEmpty();

        var availableCommand = new AvailableCommand();
        availableCommand.Intent.Should().BeEmpty();
        availableCommand.Description.Should().BeEmpty();
        availableCommand.Examples.Should().BeEmpty();
        availableCommand.Category.Should().BeEmpty();

        var dynamicRequest = new DynamicCommandRequest();
        dynamicRequest.Intent.Should().BeEmpty();
        dynamicRequest.Entities.Should().BeEmpty();
        dynamicRequest.OriginalText.Should().BeEmpty();
        dynamicRequest.Language.Should().BeEmpty();
        dynamicRequest.Context.Should().BeEmpty();

        var commandResult = new CommandResult();
        commandResult.Message.Should().BeEmpty();
        commandResult.Data.Should().NotBeNull();
        commandResult.Error.Should().BeEmpty();
        commandResult.OriginalInput.Should().BeEmpty();

        new ScreenInfo().DeviceName.Should().BeEmpty();
        new MonitorInfo().DeviceName.Should().BeEmpty();
        new ObjectDetectionItem().Label.Should().BeEmpty();

        var screenContext = new ScreenContext();
        screenContext.DeviceName.Should().BeEmpty();
        screenContext.ScreenshotHash.Should().BeEmpty();

        var frame = new ScreenCaptureFrame();
        frame.PngImage.Should().BeEmpty();
        frame.DeviceName.Should().BeEmpty();

        var intentResponse = new AiIntentResponse();
        intentResponse.Intent.Should().BeEmpty();
        intentResponse.Entities.Should().BeEmpty();
        intentResponse.Reasoning.Should().BeEmpty();

        var healthChanged = new ProviderHealthChangedEventArgs();
        healthChanged.OldStatus.Should().NotBeNull();
        healthChanged.NewStatus.Should().NotBeNull();
    }
}
