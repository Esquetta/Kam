using SmartVoiceAgent.Core.Models.Agents.Extensions;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Finds Agent Skills in the user's skills folder, the workspace, enabled plugins and imported skills.
/// </summary>
public interface IAgentSkillCatalog
{
    /// <summary>Gets the user's skills folder, <c>%AppData%/Kam/skills</c>.</summary>
    string UserSkillsDirectory { get; }

    /// <summary>
    /// Returns every skill. When two share a name, the user's own wins, then the workspace, then plugins.
    /// </summary>
    IReadOnlyList<AgentSkillInfo> GetSkills();

    /// <summary>
    /// Copies a skill folder into the user's skills folder.
    /// </summary>
    /// <param name="sourceDirectory">A folder containing <c>SKILL.md</c>.</param>
    /// <returns>The installed skill.</returns>
    AgentSkillInfo Install(string sourceDirectory);

    /// <summary>
    /// Deletes a skill from the user's skills folder. Skills from other places are left alone.
    /// </summary>
    /// <param name="name">The skill name.</param>
    /// <returns><c>true</c> when a skill was deleted.</returns>
    bool Uninstall(string name);
}

/// <summary>
/// Installs and switches plugins in <c>%AppData%/Kam/plugins</c>.
/// </summary>
public interface IAgentPluginCatalog
{
    /// <summary>Gets the plugins folder.</summary>
    string PluginsDirectory { get; }

    /// <summary>Raised after a plugin is installed, removed, enabled or disabled.</summary>
    event EventHandler? Changed;

    /// <summary>Returns every installed plugin.</summary>
    IReadOnlyList<AgentPluginInfo> GetPlugins();

    /// <summary>
    /// Turns a plugin on or off.
    /// </summary>
    /// <param name="name">The plugin name.</param>
    /// <param name="enabled">Whether it contributes to the agent.</param>
    void SetEnabled(string name, bool enabled);

    /// <summary>
    /// Installs a plugin, or every plugin in a marketplace, from a folder, an https git URL or <c>owner/repo</c>.
    /// Plugins from a marketplace are installed turned off.
    /// </summary>
    /// <param name="source">The folder or repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AgentPluginInstallResult> InstallAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an installed plugin.
    /// </summary>
    /// <param name="name">The plugin name.</param>
    /// <returns><c>true</c> when it was deleted.</returns>
    bool Uninstall(string name);
}

/// <summary>
/// Finds Markdown slash commands and turns <c>/name arguments</c> into the prompt the agent runs.
/// </summary>
public interface IAgentCommandCatalog
{
    /// <summary>Gets the user's commands folder, <c>%AppData%/Kam/commands</c>.</summary>
    string UserCommandsDirectory { get; }

    /// <summary>Returns every command.</summary>
    IReadOnlyList<AgentCommandInfo> GetCommands();

    /// <summary>
    /// Expands <c>/name arguments</c> into the command's prompt, filling in <c>$ARGUMENTS</c> and <c>$1</c>…<c>$9</c>.
    /// </summary>
    /// <param name="input">What the user typed.</param>
    /// <param name="prompt">The prompt to send to the agent.</param>
    /// <returns><c>true</c> when the input names a command.</returns>
    bool TryExpand(string input, out string prompt);
}

/// <summary>
/// Adds a section to the agent's system prompt each turn, such as the list of available skills.
/// </summary>
public interface IAgentPromptContributor
{
    /// <summary>
    /// Returns the section, or <c>null</c> when there is nothing to add.
    /// </summary>
    string? GetPromptSection();
}
