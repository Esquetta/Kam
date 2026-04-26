using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class BuiltInSkillManifestCatalogTests
{
    [Fact]
    public void CreateAll_IncludesMigratedAgentToolSkills()
    {
        var manifests = BuiltInSkillManifestCatalog.CreateAll();
        var skillIds = manifests.Select(manifest => manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        skillIds.Should().Contain(
        [
            "apps.open",
            "apps.close",
            "apps.check",
            "apps.path",
            "apps.running",
            "apps.installed.list",
            "media.play",
            "system.device.control",
            "files.read",
            "files.write",
            "files.create",
            "files.delete",
            "files.copy",
            "files.move",
            "files.exists",
            "files.info",
            "files.list",
            "files.search",
            "files.read_lines",
            "files.open",
            "files.show_in_explorer",
            "directories.create",
            "directories.open",
            "web.search",
            "communication.email.send",
            "communication.email.template.send",
            "communication.email.validate",
            "communication.sms.send",
            "communication.sms.validate",
            "communication.sms.status"
        ]);
    }
}
