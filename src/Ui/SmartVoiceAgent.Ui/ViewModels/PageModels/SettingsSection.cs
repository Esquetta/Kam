namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// The sections of the Settings page, in the order its sub-navigation lists them.
    /// </summary>
    public enum SettingsSection
    {
        /// <summary>The agent model, its key and the command mode planner.</summary>
        Model,

        /// <summary>Theme, accent color, language and motion.</summary>
        Appearance,

        /// <summary>Speech recognition, microphone, hands-free and spoken replies.</summary>
        Voice,

        /// <summary>What happens when Kam or Windows starts.</summary>
        Startup,

        /// <summary>Version, diagnostics and about.</summary>
        Advanced
    }
}
