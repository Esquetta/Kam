using SmartVoiceAgent.Core.Contracts;

public record PlayMusicCommand(string TrackName) : ICommand<CommandResultDTO>;

