using System.Text.Json.Nodes;

namespace SmartVoiceAgent.Infrastructure.Agent.Extensions;

/// <summary>
/// What an extension from the catalog needs on this computer.
/// </summary>
public enum ExtensionRuntime
{
    /// <summary>Nothing: a hosted server reached over HTTP.</summary>
    None,

    /// <summary>Node.js, for servers started with <c>npx</c>.</summary>
    Node,

    /// <summary>uv, for Python servers started with <c>uvx</c>.</summary>
    Uv,

    /// <summary>git, to clone a plugin repository.</summary>
    Git
}

/// <summary>
/// A well-known MCP server the Extensions page adds to mcp.json in one click. None of them needs a key.
/// </summary>
/// <param name="Name">The entry name in mcp.json; its tools appear as <c>mcp__{Name}__{tool}</c>.</param>
/// <param name="Title">The display name.</param>
/// <param name="Runtime">What it needs on this computer.</param>
/// <param name="Target">The address of a hosted server, or the package <c>npx</c> or <c>uvx</c> runs.</param>
/// <param name="Arguments">Arguments after the package; <see cref="FolderPlaceholder"/> marks a folder the user picks.</param>
/// <param name="Icon">The icon resource shown on its card.</param>
/// <param name="Homepage">Where the server is documented.</param>
public sealed record McpCatalogEntry(
    string Name,
    string Title,
    ExtensionRuntime Runtime,
    string Target,
    IReadOnlyList<string> Arguments,
    string Icon,
    string Homepage)
{
    /// <summary>The argument replaced with the folder the user picks.</summary>
    public const string FolderPlaceholder = "{folder}";

    /// <summary>Gets whether adding the server asks for a folder.</summary>
    public bool NeedsFolder => Arguments.Contains(FolderPlaceholder);

    /// <summary>
    /// Returns the entry to write under <c>mcpServers</c>.
    /// </summary>
    /// <param name="folder">The folder the user picked, when <see cref="NeedsFolder"/>.</param>
    public JsonObject ToConfigEntry(string? folder = null)
    {
        if (NeedsFolder && string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException($"{Title} needs a folder.", nameof(folder));
        }

        if (Runtime == ExtensionRuntime.None)
        {
            return new JsonObject { ["type"] = "http", ["url"] = Target };
        }

        var arguments = new JsonArray();
        if (Runtime == ExtensionRuntime.Node)
        {
            arguments.Add("-y");
        }

        arguments.Add(Target);
        foreach (var argument in Arguments)
        {
            arguments.Add(argument == FolderPlaceholder ? folder : argument);
        }

        return new JsonObject
        {
            ["command"] = Runtime == ExtensionRuntime.Node ? "npx" : "uvx",
            ["args"] = arguments
        };
    }
}

/// <summary>
/// A plugin repository the Extensions page installs in one click.
/// </summary>
/// <param name="Id">A stable id, used for its copy in the language files.</param>
/// <param name="Title">The display name.</param>
/// <param name="Source">The <c>owner/repo</c> to install from.</param>
/// <param name="InstalledPlugin">A plugin the repository installs; when it is present the card shows as added.</param>
/// <param name="Icon">The icon resource shown on its card.</param>
/// <param name="Homepage">Where the repository lives.</param>
public sealed record PluginCatalogEntry(string Id, string Title, string Source, string InstalledPlugin, string Icon, string Homepage);

/// <summary>
/// The extensions offered on the Extensions page's Discover tab.
/// </summary>
public static class ExtensionCatalog
{
    private const string ReferenceServers = "https://github.com/modelcontextprotocol/servers/tree/main/src/";

    /// <summary>
    /// Gets the MCP servers, most broadly useful first.
    /// </summary>
    public static IReadOnlyList<McpCatalogEntry> McpServers { get; } =
    [
        new("fetch", "Fetch", ExtensionRuntime.Uv, "mcp-server-fetch", [], "IconGlobe", ReferenceServers + "fetch"),
        new("filesystem", "Filesystem", ExtensionRuntime.Node, "@modelcontextprotocol/server-filesystem", [McpCatalogEntry.FolderPlaceholder], "IconFolder", ReferenceServers + "filesystem"),
        new("git", "Git", ExtensionRuntime.Uv, "mcp-server-git", ["--repository", McpCatalogEntry.FolderPlaceholder], "IconGitBranch", ReferenceServers + "git"),
        new("playwright", "Playwright", ExtensionRuntime.Node, "@playwright/mcp@latest", [], "IconAppWindow", "https://github.com/microsoft/playwright-mcp"),
        new("context7", "Context7", ExtensionRuntime.None, "https://mcp.context7.com/mcp", [], "IconBlocks", "https://github.com/upstash/context7"),
        new("microsoft-learn", "Microsoft Learn", ExtensionRuntime.None, "https://learn.microsoft.com/api/mcp", [], "IconGraduationCap", "https://github.com/MicrosoftDocs/mcp"),
        new("deepwiki", "DeepWiki", ExtensionRuntime.None, "https://mcp.deepwiki.com/mcp", [], "IconBookOpen", "https://docs.devin.ai/work-with-devin/deepwiki-mcp"),
        new("memory", "Memory", ExtensionRuntime.Node, "@modelcontextprotocol/server-memory", [], "IconNetwork", ReferenceServers + "memory"),
        new("sequential-thinking", "Sequential Thinking", ExtensionRuntime.Node, "@modelcontextprotocol/server-sequential-thinking", [], "IconListTree", ReferenceServers + "sequentialthinking"),
        new("chrome-devtools", "Chrome DevTools", ExtensionRuntime.Node, "chrome-devtools-mcp@latest", [], "IconCode", "https://github.com/ChromeDevTools/chrome-devtools-mcp"),
        new("time", "Time", ExtensionRuntime.Uv, "mcp-server-time", [], "IconClock", ReferenceServers + "time")
    ];

    /// <summary>
    /// Gets the plugin repositories. Both are marketplaces, so their plugins install turned off.
    /// </summary>
    public static IReadOnlyList<PluginCatalogEntry> Plugins { get; } =
    [
        new("anthropic-skills", "Anthropic skills", "anthropics/skills", "document-skills", "IconSparkles", "https://github.com/anthropics/skills"),
        new("claude-code-plugins", "Claude Code plugins", "anthropics/claude-code", "commit-commands", "IconPuzzle", "https://github.com/anthropics/claude-code/tree/main/plugins")
    ];

    /// <summary>
    /// Gets where to download what a runtime needs.
    /// </summary>
    /// <param name="runtime">The runtime.</param>
    public static string? InstallPage(ExtensionRuntime runtime) => runtime switch
    {
        ExtensionRuntime.Node => "https://nodejs.org/en/download",
        ExtensionRuntime.Uv => "https://docs.astral.sh/uv/getting-started/installation/",
        ExtensionRuntime.Git => "https://git-scm.com/downloads",
        _ => null
    };

    /// <summary>
    /// Returns whether this computer has what a runtime needs on its PATH.
    /// </summary>
    /// <param name="runtime">The runtime.</param>
    public static bool IsAvailable(ExtensionRuntime runtime) => runtime switch
    {
        ExtensionRuntime.Node => CommandLocator.Find("npx") is not null,
        ExtensionRuntime.Uv => CommandLocator.Find("uvx") is not null,
        ExtensionRuntime.Git => CommandLocator.Find("git") is not null,
        _ => true
    };
}

/// <summary>
/// Finds programs on the PATH the way a shell would.
/// </summary>
public static class CommandLocator
{
    /// <summary>
    /// Returns the full path of a program on the PATH, or <c>null</c>.
    /// </summary>
    /// <param name="command">The program name, such as <c>npx</c>.</param>
    /// <param name="path">The PATH to search; defaults to the process PATH.</param>
    /// <param name="extensions">On Windows, the extensions to try (PATHEXT); defaults to the process value.</param>
    /// <param name="windows">Whether to search like Windows; defaults to the current platform.</param>
    public static string? Find(string command, string? path = null, string? extensions = null, bool? windows = null)
    {
        var isWindows = windows ?? OperatingSystem.IsWindows();
        path ??= Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var suffixes = isWindows
            ? (extensions ?? Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Prepend(string.Empty)
            : [string.Empty];
        var separator = isWindows ? ';' : Path.PathSeparator;

        foreach (var directory in path.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var suffix in suffixes)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory.Trim('"'), command + suffix);
                }
                catch (ArgumentException)
                {
                    break;
                }

                if (File.Exists(candidate) && (suffix.Length > 0 || !isWindows || Path.HasExtension(command)))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
