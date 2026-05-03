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
            "files.search_content",
            "files.tree",
            "files.read_lines",
            "file.read",
            "file.read_range",
            "file.replace_range",
            "file.patch",
            "files.open",
            "files.show_in_explorer",
            "workspace.tree",
            "workspace.find_files",
            "workspace.search_text",
            "workspace.map",
            "workspace.diff_preview",
            "code.search",
            "code.outline",
            "directories.create",
            "directories.open",
            "web.search",
            "communication.email.send",
            "communication.email.template.send",
            "communication.email.validate",
            "communication.sms.send",
            "communication.sms.validate",
            "communication.sms.status",
            "clipboard.get",
            "clipboard.peek",
            "clipboard.set",
            "clipboard.clear",
            "shell.run",
            "web.fetch",
            "web.read_page",
            "window.active",
            "window.list",
            "accessibility.tree",
            "system.info",
            "system.cpu",
            "system.memory",
            "system.disk",
            "system.battery",
            "system.processes.list",
            "system.process.kill"
        ]);
    }

    [Fact]
    public void CreateAll_AssignsPolicyBoundariesToWorkspaceAgentLikeSkills()
    {
        var manifests = BuiltInSkillManifestCatalog.CreateAll()
            .ToDictionary(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase);

        manifests["shell.run"].RiskLevel.Should().Be(SmartVoiceAgent.Core.Models.Skills.SkillRiskLevel.High);
        manifests["shell.run"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.ProcessLaunch);
        manifests["shell.run"].TimeoutMilliseconds.Should().BeLessThanOrEqualTo(15000);

        manifests["apps.open"].RiskLevel.Should().Be(SmartVoiceAgent.Core.Models.Skills.SkillRiskLevel.Medium);
        manifests["apps.open"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.ProcessLaunch);
        manifests["web.fetch"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.Network);
        manifests["web.read_page"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.Network);
        manifests["window.active"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.SystemInformation);
        manifests["accessibility.tree"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.SystemInformation);
        manifests["clipboard.peek"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.ClipboardRead);
        manifests["file.read"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.FileSystemRead);
        manifests["file.replace_range"].RiskLevel.Should().Be(SmartVoiceAgent.Core.Models.Skills.SkillRiskLevel.High);
        manifests["file.replace_range"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.FileSystemWrite);
        manifests["file.patch"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.FileSystemWrite);
        manifests["workspace.diff_preview"].RiskLevel.Should().Be(SmartVoiceAgent.Core.Models.Skills.SkillRiskLevel.Low);
        manifests["workspace.map"].RiskLevel.Should().Be(SmartVoiceAgent.Core.Models.Skills.SkillRiskLevel.Low);
        manifests["workspace.map"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.FileSystemRead);
        manifests["code.outline"].Permissions.Should().Contain(SmartVoiceAgent.Core.Models.Skills.SkillPermission.FileSystemRead);
    }
}
