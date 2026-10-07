using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;

namespace SmartVoiceAgent.Infrastructure.Agent.Extensions;

/// <summary>
/// Installs plugins in the Claude Code layout into <c>%AppData%/Kam/plugins</c>: <c>.claude-plugin/plugin.json</c>,
/// <c>skills/</c>, <c>commands/</c>, <c>agents/</c> and <c>.mcp.json</c>. A repository with
/// <c>.claude-plugin/marketplace.json</c> installs each plugin it contains, turned off.
/// </summary>
public sealed class AgentPluginCatalog : IAgentPluginCatalog
{
    private static readonly string[] ListedComponents = ["skills", "commands", "agents"];
    private static readonly Regex GitHubShorthand = new(@"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]+$", RegexOptions.Compiled);
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _statePath;
    private readonly ILogger<AgentPluginCatalog>? _logger;
    private readonly Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> _gitClone;
    private readonly object _gate = new();

    /// <summary>
    /// Creates the catalog.
    /// </summary>
    /// <param name="pluginsDirectory">Where plugins are installed.</param>
    /// <param name="statePath">The file that remembers which plugins are turned off.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="gitClone">Clones a repository URL into a folder; tests replace it.</param>
    public AgentPluginCatalog(
        string pluginsDirectory,
        string statePath,
        ILogger<AgentPluginCatalog>? logger = null,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>>? gitClone = null)
    {
        PluginsDirectory = pluginsDirectory;
        _statePath = statePath;
        _logger = logger;
        _gitClone = gitClone ?? CloneWithGitAsync;
    }

    /// <inheritdoc />
    public string PluginsDirectory { get; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<AgentPluginInfo> GetPlugins()
    {
        if (!Directory.Exists(PluginsDirectory))
        {
            return [];
        }

        var disabled = LoadDisabled();
        return Directory.EnumerateDirectories(PluginsDirectory)
            .Where(directory => !Path.GetFileName(directory).StartsWith('.') && !directory.EndsWith(".installing", StringComparison.Ordinal))
            .Select(directory => ReadPlugin(directory))
            .Select(plugin => plugin with { Enabled = !disabled.Contains(plugin.Name) })
            .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public void SetEnabled(string name, bool enabled)
    {
        lock (_gate)
        {
            var disabled = LoadDisabled();
            var changed = enabled ? disabled.Remove(name) : disabled.Add(name);
            if (!changed)
            {
                return;
            }

            SaveDisabled(disabled);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task<AgentPluginInstallResult> InstallAsync(string source, CancellationToken cancellationToken = default)
    {
        source = source.Trim().Trim('"');
        if (source.Length == 0)
        {
            return new AgentPluginInstallResult(false, "Enter a folder, a git URL or owner/repo.", []);
        }

        if (Directory.Exists(source))
        {
            return Finish(InstallFromDirectory(Path.GetFullPath(source), Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)))));
        }

        var url = ToCloneUrl(source);
        if (url is null)
        {
            return new AgentPluginInstallResult(false, $"'{source}' is not a folder, an https git URL or owner/repo.", []);
        }

        var checkout = Path.Combine(Path.GetTempPath(), "kam-plugin-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var (exitCode, output) = await _gitClone(url, checkout, timeout.Token);
            if (exitCode != 0 || !Directory.Exists(checkout))
            {
                return new AgentPluginInstallResult(false, $"git clone failed: {output.Trim()}", []);
            }

            var repositoryName = url.TrimEnd('/').Split('/').Last();
            if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repositoryName = repositoryName[..^4];
            }

            return Finish(InstallFromDirectory(checkout, repositoryName));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AgentPluginInstallResult(false, "git clone took longer than 3 minutes.", []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new AgentPluginInstallResult(false, ex is System.ComponentModel.Win32Exception
                ? "Installing from a repository needs git on PATH."
                : ex.Message, []);
        }
        finally
        {
            try
            {
                ExtensionPaths.DeleteDirectory(checkout);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogDebug(ex, "Could not delete {Checkout}", checkout);
            }
        }
    }

    /// <inheritdoc />
    public bool Uninstall(string name)
    {
        var plugin = GetPlugins().FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (plugin is null || !ExtensionPaths.IsInside(PluginsDirectory, plugin.Directory))
        {
            return false;
        }

        ExtensionPaths.DeleteDirectory(plugin.Directory);
        lock (_gate)
        {
            var disabled = LoadDisabled();
            if (disabled.Remove(plugin.Name))
            {
                SaveDisabled(disabled);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Reads a plugin folder.
    /// </summary>
    /// <param name="directory">The plugin folder.</param>
    /// <param name="fallbackName">The name to use when the manifest has none; defaults to the folder name.</param>
    public static AgentPluginInfo ReadPlugin(string directory, string? fallbackName = null)
    {
        directory = Path.GetFullPath(directory);
        fallbackName ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        JsonObject? manifest = null;
        string? error = null;

        var manifestPath = new[] { Path.Combine(directory, ".claude-plugin", "plugin.json"), Path.Combine(directory, "plugin.json") }
            .FirstOrDefault(File.Exists);
        if (manifestPath is not null)
        {
            try
            {
                manifest = JsonNode.Parse(File.ReadAllText(manifestPath), documentOptions: DocumentOptions) as JsonObject;
                error = manifest is null ? "plugin.json is not a JSON object." : null;
            }
            catch (JsonException ex)
            {
                error = $"plugin.json is not valid JSON: {ex.Message}";
            }
        }

        var name = ReadText(manifest, "name") is { Length: > 0 } declared ? declared : fallbackName;

        var skillDirectories = new List<string>();
        foreach (var root in ComponentPaths(directory, manifest, "skills"))
        {
            if (File.Exists(Path.Combine(root, "SKILL.md")))
            {
                skillDirectories.Add(root);
            }
            else if (Directory.Exists(root))
            {
                skillDirectories.AddRange(Directory.EnumerateDirectories(root)
                    .Where(child => File.Exists(Path.Combine(child, "SKILL.md")))
                    .OrderBy(child => child, StringComparer.OrdinalIgnoreCase));
            }
        }

        var commandFiles = ComponentPaths(directory, manifest, "commands")
            .SelectMany(path => File.Exists(path) && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? new[] { path }
                : Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*.md", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray()
                    : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var agentCount = ComponentPaths(directory, manifest, "agents")
            .Sum(path => File.Exists(path) ? 1 : Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.md").Count() : 0);

        var variables = new Dictionary<string, string> { ["CLAUDE_PLUGIN_ROOT"] = directory, ["KAM_PLUGIN_ROOT"] = directory };
        var mcpServers = new List<McpServerDefinition>();
        var mcpNode = manifest?["mcpServers"];
        string? mcpJson = null;
        if (mcpNode is JsonObject inline)
        {
            mcpJson = inline.ToJsonString();
        }
        else
        {
            var mcpPath = mcpNode is JsonValue value && value.TryGetValue<string>(out var relative)
                ? ResolveInside(directory, relative)
                : Path.Combine(directory, ".mcp.json");
            if (mcpPath is not null && File.Exists(mcpPath))
            {
                mcpJson = File.ReadAllText(mcpPath);
            }
        }

        if (mcpJson is not null)
        {
            var (servers, errors) = McpConfigParser.Parse(mcpJson, $"plugin:{name}", variables);
            mcpServers.AddRange(servers);
            error ??= errors.FirstOrDefault();
        }

        return new AgentPluginInfo
        {
            Name = name,
            Version = ReadText(manifest, "version"),
            Description = ReadText(manifest, "description"),
            Author = manifest?["author"] switch
            {
                JsonObject author => ReadText(author, "name"),
                JsonValue author when author.TryGetValue<string>(out var text) => text,
                _ => string.Empty
            },
            Directory = directory,
            Enabled = true,
            SkillDirectories = skillDirectories,
            CommandFiles = commandFiles,
            AgentCount = agentCount,
            McpServers = mcpServers,
            Error = error
        };
    }

    private AgentPluginInstallResult Finish(AgentPluginInstallResult result)
    {
        if (result.Installed.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    private AgentPluginInstallResult InstallFromDirectory(string directory, string fallbackName)
    {
        var marketplacePath = Path.Combine(directory, ".claude-plugin", "marketplace.json");
        if (File.Exists(marketplacePath))
        {
            return InstallMarketplace(directory, marketplacePath);
        }

        var plugin = ReadPlugin(directory, fallbackName);
        if (plugin.SkillDirectories.Count == 0 && plugin.CommandFiles.Count == 0 && plugin.McpServers.Count == 0 && plugin.AgentCount == 0)
        {
            return new AgentPluginInstallResult(false, plugin.Error ?? "No plugin was found: the folder has no skills, commands, agents or MCP servers.", []);
        }

        var name = InstallPlugin(directory, plugin.Name, enabled: true);
        return new AgentPluginInstallResult(true, $"Installed {name}.", [name]);
    }

    private AgentPluginInstallResult InstallMarketplace(string directory, string marketplacePath)
    {
        JsonObject? marketplace;
        try
        {
            marketplace = JsonNode.Parse(File.ReadAllText(marketplacePath), documentOptions: DocumentOptions) as JsonObject;
        }
        catch (JsonException ex)
        {
            return new AgentPluginInstallResult(false, $"marketplace.json is not valid JSON: {ex.Message}", []);
        }

        var pluginRoot = ReadText(marketplace?["metadata"] as JsonObject, "pluginRoot");
        var installed = new List<string>();
        var skipped = 0;
        foreach (var entry in (marketplace?["plugins"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var source = entry["source"] switch
            {
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                _ => null
            };
            var path = source is null ? null : ResolveInside(directory, Path.Combine(pluginRoot, source));
            if (path is null || !Directory.Exists(path))
            {
                skipped++;
                continue;
            }

            var fallback = ReadText(entry, "name") is { Length: > 0 } entryName ? entryName : Path.GetFileName(path);
            if (!HasManifest(path) && ListsComponents(entry))
            {
                installed.Add(InstallFromEntry(path, entry, fallback));
                continue;
            }

            var plugin = ReadPlugin(path, fallback);
            installed.Add(InstallPlugin(path, plugin.Name, enabled: false));
        }

        if (installed.Count == 0)
        {
            return new AgentPluginInstallResult(false, "The marketplace has no plugins inside the repository.", []);
        }

        var message = $"Added {installed.Count} plugin{(installed.Count == 1 ? string.Empty : "s")} from the marketplace, turned off. Turn on the ones you want.";
        if (skipped > 0)
        {
            message += $" Skipped {skipped} that live in other repositories.";
        }

        return new AgentPluginInstallResult(true, message, installed);
    }

    /// <summary>
    /// Installs a plugin that a marketplace entry defines by listing its skills, commands and agents, as in
    /// anthropics/skills where several plugins share the repository root. Only the listed parts are copied, so
    /// each plugin holds just its own skills.
    /// </summary>
    private string InstallFromEntry(string root, JsonObject entry, string name)
    {
        var staging = Path.Combine(Path.GetTempPath(), "kam-plugin-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var component in ListedComponents)
            {
                foreach (var relative in ListedPaths(entry, component))
                {
                    var path = ResolveInside(root, relative);
                    if (path is null)
                    {
                        continue;
                    }

                    var target = Path.Combine(staging, component, Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
                    if (Directory.Exists(path))
                    {
                        ExtensionPaths.CopyDirectory(path, target);
                    }
                    else if (File.Exists(path))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(path, target, overwrite: true);
                    }
                }
            }

            var manifest = new JsonObject { ["name"] = name };
            foreach (var key in new[] { "version", "description", "author" })
            {
                if (entry[key] is { } value)
                {
                    manifest[key] = value.DeepClone();
                }
            }

            if (entry["mcpServers"] is JsonObject servers)
            {
                manifest["mcpServers"] = servers.DeepClone();
            }

            Directory.CreateDirectory(Path.Combine(staging, ".claude-plugin"));
            File.WriteAllText(Path.Combine(staging, ".claude-plugin", "plugin.json"), manifest.ToJsonString());
            return InstallPlugin(staging, name, enabled: false);
        }
        finally
        {
            ExtensionPaths.DeleteDirectory(staging);
        }
    }

    private static bool HasManifest(string directory) =>
        File.Exists(Path.Combine(directory, ".claude-plugin", "plugin.json")) || File.Exists(Path.Combine(directory, "plugin.json"));

    private static bool ListsComponents(JsonObject entry) =>
        ListedComponents.Any(component => ListedPaths(entry, component).Count > 0);

    private static IReadOnlyList<string> ListedPaths(JsonObject entry, string component) => entry[component] switch
    {
        JsonValue value when value.TryGetValue<string>(out var single) => [single],
        JsonArray array => array.OfType<JsonValue>().Select(item => item.TryGetValue<string>(out var text) ? text : null).OfType<string>().ToArray(),
        _ => []
    };

    private string InstallPlugin(string source, string name, bool enabled)
    {
        var folder = ExtensionPaths.ToFolderName(name);
        var destination = Path.Combine(PluginsDirectory, folder);
        if (ExtensionPaths.IsInside(destination, source))
        {
            throw new IOException("The plugin is already installed from this folder.");
        }

        Directory.CreateDirectory(PluginsDirectory);
        ExtensionPaths.CopyDirectory(source, destination);
        var installedName = ReadPlugin(destination).Name;
        lock (_gate)
        {
            var disabled = LoadDisabled();
            var changed = enabled ? disabled.Remove(installedName) : disabled.Add(installedName);
            if (changed)
            {
                SaveDisabled(disabled);
            }
        }

        _logger?.LogInformation("Installed plugin {Plugin} into {Directory}", installedName, destination);
        return installedName;
    }

    private static IEnumerable<string> ComponentPaths(string directory, JsonObject? manifest, string component)
    {
        var defaultPath = Path.Combine(directory, component);
        if (Directory.Exists(defaultPath))
        {
            yield return defaultPath;
        }

        var declared = manifest?[component] switch
        {
            JsonValue value when value.TryGetValue<string>(out var single) => [single],
            JsonArray array => array.OfType<JsonValue>().Select(item => item.TryGetValue<string>(out var text) ? text : null).OfType<string>().ToArray(),
            _ => Array.Empty<string>()
        };

        foreach (var relative in declared)
        {
            var path = ResolveInside(directory, relative);
            if (path is not null && !path.Equals(defaultPath, StringComparison.OrdinalIgnoreCase) && (Directory.Exists(path) || File.Exists(path)))
            {
                yield return path;
            }
        }
    }

    private static string? ResolveInside(string directory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(directory, relative));
        return ExtensionPaths.IsInside(directory, path) ? path : null;
    }

    private static string ReadText(JsonObject? node, string key) =>
        node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;

    private static string? ToCloneUrl(string source)
    {
        if (GitHubShorthand.IsMatch(source))
        {
            return $"https://github.com/{source}.git";
        }

        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !source.Any(char.IsWhiteSpace)
            && string.IsNullOrEmpty(uri.UserInfo)
                ? uri.ToString()
                : null;
    }

    private HashSet<string> LoadDisabled()
    {
        try
        {
            if (File.Exists(_statePath)
                && JsonNode.Parse(File.ReadAllText(_statePath), documentOptions: DocumentOptions) is JsonObject state
                && state["disabled"] is JsonArray disabled)
            {
                return disabled.OfType<JsonValue>()
                    .Select(item => item.TryGetValue<string>(out var name) ? name : null)
                    .OfType<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not read {Path}", _statePath);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveDisabled(HashSet<string> disabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var state = new JsonObject
        {
            ["disabled"] = new JsonArray(disabled.Order(StringComparer.OrdinalIgnoreCase).Select(name => (JsonNode)JsonValue.Create(name)!).ToArray())
        };
        var temporary = _statePath + ".tmp";
        File.WriteAllText(temporary, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _statePath, overwrite: true);
    }

    private static async Task<(int ExitCode, string Output)> CloneWithGitAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "clone", "--depth", "1", "--", url, destination })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(startInfo) ?? throw new IOException("git could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, (await output) + (await error));
    }
}

/// <summary>
/// The MCP servers of enabled plugins.
/// </summary>
public sealed class PluginMcpServerSource : IMcpServerSource
{
    private readonly IAgentPluginCatalog _plugins;

    /// <summary>
    /// Creates the source.
    /// </summary>
    /// <param name="plugins">The installed plugins.</param>
    public PluginMcpServerSource(IAgentPluginCatalog plugins)
    {
        _plugins = plugins;
    }

    /// <inheritdoc />
    public IReadOnlyList<McpServerDefinition> GetServers() =>
        _plugins.GetPlugins().Where(plugin => plugin.Enabled).SelectMany(plugin => plugin.McpServers).ToList();
}
