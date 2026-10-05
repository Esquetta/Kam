using FluentAssertions;
using SmartVoiceAgent.Application.Validators;
using SmartVoiceAgent.Core.Commands;

namespace SmartVoiceAgent.Tests.Application.Validators;

public sealed class OpenApplicationCommandValidatorTests
{
    private readonly OpenApplicationCommandValidator _validator = new();

    [Theory]
    [InlineData("firefox")]
    [InlineData("Visual Studio Code")]
    [InlineData("code-insiders")]
    [InlineData("chrome.exe")]
    public void Validate_CommonApplicationNames_AreAccepted(string name)
    {
        _validator.Validate(new OpenApplicationCommand(name)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("firefox; rm -rf ~")]
    [InlineData("app && reboot")]
    [InlineData("$(whoami)")]
    [InlineData("`id`")]
    [InlineData("app | nc host 1")]
    [InlineData("../../bin/sh")]
    public void Validate_ShellMetacharacters_AreRejected(string name)
    {
        _validator.Validate(new OpenApplicationCommand(name)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_OverlongName_IsRejected()
    {
        _validator.Validate(new OpenApplicationCommand(new string('a', 101))).IsValid.Should().BeFalse();
    }
}
