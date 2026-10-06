using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class ShellCommandGuardTests
{
    [Theory]
    [InlineData("dotnet test --verbosity normal")]
    [InlineData("cat src/SmartVoiceAgent.Core/Models/AppInfo.cs")]
    [InlineData("git log --format oneline")]
    [InlineData("dotnet format whitespace")]
    [InlineData("dotnet build --platform x64")]
    [InlineData("Get-Process | Format-Table -AutoSize")]
    [InlineData("git status && git diff --stat")]
    [InlineData("npm rm left-pad")]
    [InlineData("docker rm old-container")]
    [InlineData("git clean -n")]
    [InlineData("git reset --soft HEAD~1")]
    [InlineData("grep -rn \"shutdown\" docs/")]
    [InlineData("echo firmware > notes.txt")]
    [InlineData("cmd /c dotnet format")]
    [InlineData("bash -c \"echo rm is a command\"")]
    [InlineData("git commit -m \"rm old file\"")]
    [InlineData("dotnet test --filter rm")]
    public void FindBlockedRule_EverydayCommands_AreAllowed(string command)
    {
        ShellCommandGuard.FindBlockedRule(command).Should().BeNull();
    }

    [Theory]
    [InlineData("rm -rf build", "rm")]
    [InlineData("rm file.txt", "rm")]
    [InlineData("/bin/rm file.txt", "rm")]
    [InlineData("sudo rm -rf /var/x", "rm")]
    [InlineData("FOO=1 rm x", "rm")]
    [InlineData("bash -c \"rm -rf x\"", "rm")]
    [InlineData("bash -lc 'cd src && rm -rf x'", "rm")]
    [InlineData("cmd /c del /q \"C:\\temp\\a.txt\"", "del")]
    [InlineData("cmd.exe /c rd /s /q build", "rd")]
    [InlineData("powershell -NoProfile -Command Remove-Item -Recurse build", "remove-item")]
    [InlineData("Get-ChildItem *.log | Remove-Item", "remove-item")]
    [InlineData("ls | % { rm $_ }", "rm")]
    [InlineData("ri -Recurse build", "ri")]
    [InlineData("pwsh Remove-Item build", "remove-item")]
    [InlineData("\\rm file.txt", "rm")]
    [InlineData("npx rimraf build", "rimraf")]
    [InlineData("find . -name '*.tmp' -delete", "find -delete")]
    [InlineData("find . -name x -exec rm {} \\;", "rm")]
    [InlineData("ls *.tmp | xargs rm", "rm")]
    [InlineData("echo done; rm -rf out", "rm")]
    [InlineData("echo $(rm -rf out)", "rm")]
    [InlineData("git reset --hard", "git reset --hard")]
    [InlineData("git reset --hard origin/main", "git reset --hard")]
    [InlineData("git clean -fd", "git clean -f")]
    [InlineData("git clean -xdf", "git clean -f")]
    [InlineData("git clean --force", "git clean -f")]
    [InlineData("mkfs.ext4 /dev/sdb1", "mkfs")]
    [InlineData("dd if=/dev/zero of=/dev/sda", "dd if=")]
    [InlineData("format C:", "format")]
    [InlineData("shutdown /s /t 0", "shutdown")]
    [InlineData("Stop-Computer -Force", "stop-computer")]
    [InlineData(":(){ :|:& };:", "fork bomb")]
    public void FindBlockedRule_DestructiveCommands_AreBlocked(string command, string rule)
    {
        ShellCommandGuard.FindBlockedRule(command).Should().Be(rule);
    }
}
