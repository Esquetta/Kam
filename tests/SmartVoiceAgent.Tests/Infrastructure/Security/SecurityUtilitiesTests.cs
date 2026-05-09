using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Security;

namespace SmartVoiceAgent.Tests.Infrastructure.Security;

public sealed class SecurityUtilitiesTests
{
    [Fact]
    public void IsSafeFilePath_WithBaseDirectory_RejectsSiblingPrefixEscape()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"kam-secure-root-{Guid.NewGuid():N}");
        var siblingDirectory = baseDirectory + "-other";
        var siblingFile = Path.Combine(siblingDirectory, "secret.txt");

        SecurityUtilities.IsSafeFilePath(siblingFile, baseDirectory).Should().BeFalse();
    }

    [Fact]
    public void IsSafeFilePath_WithBaseDirectory_AllowsChildPath()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"kam-secure-root-{Guid.NewGuid():N}");
        var childFile = Path.Combine(baseDirectory, "src", "Program.cs");

        SecurityUtilities.IsSafeFilePath(childFile, baseDirectory).Should().BeTrue();
    }
}
