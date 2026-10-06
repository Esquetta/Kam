namespace SmartVoiceAgent.Core.Models.Agents.Extensions;

/// <summary>How Kam talks to an MCP server.</summary>
public enum McpTransportKind
{
    /// <summary>Kam starts the server as a process and talks over stdin and stdout.</summary>
    Stdio = 0,

    /// <summary>Streamable HTTP, falling back to SSE.</summary>
    Http = 1
}

/// <summary>
/// One MCP server entry, in the same shape as the <c>mcpServers</c> map Claude Desktop and Claude Code use.
/// Values may still contain <c>${VAR}</c> or <c>${secret:NAME}</c> references; they are resolved when connecting.
/// </summary>
public sealed record McpServerDefinition
{
    /// <summary>Gets the server name; tool names become <c>mcp__{name}__{tool}</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the transport.</summary>
    public McpTransportKind Transport { get; init; }

    /// <summary>Gets the program to start, for stdio servers.</summary>
    public string? Command { get; init; }

    /// <summary>Gets the program arguments, for stdio servers.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets extra environment variables, for stdio servers.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the working directory, for stdio servers.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets the endpoint, for HTTP servers.</summary>
    public string? Url { get; init; }

    /// <summary>Gets extra request headers, for HTTP servers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets where the entry came from: <c>user</c>, <c>project</c>, <c>plugin:{name}</c> or <c>built-in</c>.</summary>
    public string Source { get; init; } = "user";

    /// <summary>Gets whether the entry is switched off.</summary>
    public bool Disabled { get; init; }
}

/// <summary>Connection state of an MCP server.</summary>
public enum McpServerStatus
{
    /// <summary>Not started yet; servers start on the first agent turn that needs tools.</summary>
    Idle = 0,

    /// <summary>Starting or connecting.</summary>
    Connecting = 1,

    /// <summary>Connected; its tools are offered to the agent.</summary>
    Ready = 2,

    /// <summary>Could not start or connect; see the error.</summary>
    Failed = 3,

    /// <summary>Switched off in the configuration.</summary>
    Disabled = 4
}

/// <summary>
/// An MCP server and its current state.
/// </summary>
/// <param name="Definition">The configured entry.</param>
/// <param name="Status">Connection state.</param>
/// <param name="ToolCount">Tools the server offers once ready.</param>
/// <param name="Error">Why it failed, when it did.</param>
public sealed record McpServerState(
    McpServerDefinition Definition,
    McpServerStatus Status,
    int ToolCount,
    string? Error);
