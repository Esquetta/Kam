using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Security;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Ui.Services;

public sealed class GitHubCliDesktopConnector : IGitHubDesktopConnector
{
    public async Task<GitHubDesktopConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var status = await RunGhAsync(["auth", "status", "--hostname", "github.com"], cancellationToken);
        if (status.ExitCode != 0)
        {
            return GitHubDesktopConnectionResult.Failed(
                "GitHub CLI is not signed in. Run `gh auth login --web` once, then connect again.");
        }

        return await ListRepositoriesAsync(cancellationToken);
    }

    public async Task<GitHubDesktopConnectionResult> ListRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync(
            ["repo", "list", "--limit", "25", "--json", "nameWithOwner,isPrivate,defaultBranchRef,url"],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.Error)
                ? result.Output
                : result.Error;
            return GitHubDesktopConnectionResult.Failed(
                $"GitHub repository listing failed: {SecretRedactor.Redact(message)}");
        }

        try
        {
            var repositories = ParseRepositories(result.Output);
            return GitHubDesktopConnectionResult.Connected(
                $"{repositories.Count} repositories visible through your GitHub sign-in.",
                repositories);
        }
        catch (JsonException ex)
        {
            return GitHubDesktopConnectionResult.Failed(
                $"GitHub repository response could not be parsed: {ex.Message}");
        }
    }

    private static IReadOnlyList<GitHubRepositorySummary> ParseRepositories(string json)
    {
        using var document = JsonDocument.Parse(json);
        var repositories = new List<GitHubRepositorySummary>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var fullName = item.GetProperty("nameWithOwner").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var defaultBranch = "main";
            if (item.TryGetProperty("defaultBranchRef", out var branchRef)
                && branchRef.ValueKind == JsonValueKind.Object
                && branchRef.TryGetProperty("name", out var branchName)
                && !string.IsNullOrWhiteSpace(branchName.GetString()))
            {
                defaultBranch = branchName.GetString()!;
            }

            repositories.Add(new GitHubRepositorySummary(
                fullName,
                item.TryGetProperty("isPrivate", out var isPrivate) && isPrivate.GetBoolean(),
                defaultBranch,
                item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                string.Empty));
        }

        return repositories;
    }

    private static async Task<ProcessResult> RunGhAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "gh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            var error = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    output.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    error.AppendLine(e.Data);
                }
            };

            if (!process.Start())
            {
                return new ProcessResult(1, string.Empty, "GitHub CLI could not be started.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessResult(1, string.Empty, $"GitHub CLI is unavailable: {ex.Message}");
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
