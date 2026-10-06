using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// Voice in the main window: the talk button and shortcut, the wake phrase, the composer's voice status
    /// and replies read aloud.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>
        /// The <see cref="ISettingsService.SpokenReplies"/> value that reads no replies aloud.
        /// </summary>
        public const string SpokenRepliesOff = "Off";

        /// <summary>
        /// The <see cref="ISettingsService.SpokenReplies"/> value that reads replies to voice commands aloud.
        /// </summary>
        public const string SpokenRepliesVoice = "Voice";

        /// <summary>
        /// The <see cref="ISettingsService.SpokenReplies"/> value that reads every reply aloud.
        /// </summary>
        public const string SpokenRepliesAll = "All";

        private static readonly TimeSpan VoiceProblemDisplayTime = TimeSpan.FromSeconds(6);
        private static readonly IBrush s_voiceIdleColor = new ImmutableSolidColorBrush(Color.Parse("#8A8A99"));
        private static readonly IBrush s_voiceWakeColor = new ImmutableSolidColorBrush(Color.Parse("#10B981"));
        private static readonly IBrush s_voiceListeningColor = new ImmutableSolidColorBrush(Color.Parse("#EF4444"));
        private static readonly IBrush s_voiceWorkingColor = new ImmutableSolidColorBrush(Color.Parse("#8B7CFF"));
        private static readonly IBrush s_voiceProblemColor = new ImmutableSolidColorBrush(Color.Parse("#F59E0B"));

        private VoiceAssistant? _voice;
        private VoiceState _voiceState = VoiceState.Off;
        private string? _voiceProblemText;
        private double _voiceProgress;
        private double _voiceLevel;
        private int _voiceProblemVersion;
        private int _pendingVoiceCommands;
        private bool _isSavingWakeWordSetting;
        private string _spokenRepliesOnMode = SpokenRepliesVoice;

        /// <summary>
        /// Gets whether voice is set up on this computer.
        /// </summary>
        public bool IsVoiceAvailable => _voice is not null;

        /// <summary>
        /// Gets what voice is doing.
        /// </summary>
        public VoiceState VoiceState => _voiceState;

        /// <summary>
        /// Gets whether a command is being recorded.
        /// </summary>
        public bool IsVoiceListening => _voiceState == VoiceState.Listening;

        /// <summary>
        /// Gets whether a recording, transcription, model download or spoken reply is in progress.
        /// </summary>
        public bool IsVoiceBusy => _voiceState is VoiceState.Preparing or VoiceState.Listening
            or VoiceState.Transcribing or VoiceState.Speaking;

        /// <summary>
        /// Gets whether the wake phrase listener is on.
        /// </summary>
        public bool IsWakeWordEnabled => _voice?.IsWakeWordEnabled == true;

        /// <summary>
        /// Gets whether the composer shows the voice status: while voice is busy, listening for the wake
        /// phrase, or for a few seconds after a command fails.
        /// </summary>
        public bool IsVoiceStatusVisible => _voice is not null
            && (IsVoiceBusy || _voiceState == VoiceState.WakeListening || _voiceProblemText is not null);

        /// <summary>
        /// Gets whether the composer shows the microphone level meter.
        /// </summary>
        public bool IsVoiceMeterVisible => _voiceState is VoiceState.Listening or VoiceState.Preparing;

        /// <summary>
        /// Gets the meter value: the microphone level while recording, the download progress while preparing.
        /// </summary>
        public double VoiceMeterValue => _voiceState == VoiceState.Preparing ? _voiceProgress : _voiceLevel;

        /// <summary>
        /// Gets whether the composer offers to cancel the recording or the spoken reply.
        /// </summary>
        public bool IsVoiceCancelVisible => _voiceState is VoiceState.Listening or VoiceState.Speaking;

        /// <summary>
        /// Gets the voice status line in the interface language.
        /// </summary>
        public string VoiceStatusText => _voiceProblemText ?? FormatVoiceState(_voiceState, _voiceProgress, WakePhrase);

        /// <summary>
        /// Gets the color of the voice status dot and the talk button icon.
        /// </summary>
        public IBrush VoiceStatusColor => _voiceProblemText is not null
            ? s_voiceProblemColor
            : _voiceState switch
            {
                VoiceState.WakeListening => s_voiceWakeColor,
                VoiceState.Listening => s_voiceListeningColor,
                VoiceState.Preparing or VoiceState.Transcribing or VoiceState.Speaking => s_voiceWorkingColor,
                _ => s_voiceIdleColor
            };

        /// <summary>
        /// Gets the talk shortcut as saved in Settings, such as <c>Ctrl+Alt+Space</c>; empty when it is off.
        /// </summary>
        public string TalkShortcut => _pageSettingsService.TalkShortcut?.Trim() ?? string.Empty;

        /// <summary>
        /// Gets the talk button's tooltip, which names the shortcut and what a click does now.
        /// </summary>
        public string TalkToolTip => _voice is null
            ? Loc.Get("Voice.Unavailable")
            : _voiceState switch
            {
                VoiceState.Listening => Loc.Get("Voice.StopAndSend"),
                VoiceState.Speaking => Loc.Get("Voice.StopReading"),
                _ => string.IsNullOrEmpty(TalkShortcut)
                    ? Loc.Get("Voice.Talk")
                    : Loc.Format("Voice.TalkWithShortcut", TalkShortcut)
            };

        /// <summary>
        /// Gets whether replies are read aloud: <see cref="ISettingsService.SpokenReplies"/> is anything but Off.
        /// </summary>
        public bool IsSpokenRepliesOn => !string.Equals(
            _pageSettingsService.SpokenReplies, SpokenRepliesOff, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the read-aloud button's tooltip, which says what a click does.
        /// </summary>
        public string SpokenRepliesToolTip => Loc.Get(IsSpokenRepliesOn ? "Voice.SpokenReplies.TurnOff" : "Voice.SpokenReplies.TurnOn");

        /// <summary>
        /// Starts recording a command, or stops and sends it; while a reply is read aloud, stops reading.
        /// </summary>
        public ICommand TalkCommand { get; private set; } = null!;

        /// <summary>
        /// Turns reading replies aloud off, or back on in the mode it had before.
        /// </summary>
        public ICommand ToggleSpokenRepliesCommand { get; private set; } = null!;

        /// <summary>
        /// Stops recording without sending, and stops reading aloud.
        /// </summary>
        public ICommand CancelVoiceCommand { get; private set; } = null!;

        /// <summary>
        /// Turns the wake phrase listener on or off and saves the choice.
        /// </summary>
        public ICommand ToggleWakeWordCommand { get; private set; } = null!;

        /// <summary>
        /// Raised when the talk shortcut in Settings changes, so the window can register the new one.
        /// </summary>
        public event EventHandler? TalkShortcutChanged;

        private string WakePhrase => string.IsNullOrWhiteSpace(_pageSettingsService.WakeWord)
            ? "Hey Kam"
            : _pageSettingsService.WakeWord.Trim();

        /// <summary>
        /// Connects the voice assistant: the talk button, the wake phrase, and routing of recognized commands.
        /// Starts the wake phrase listener when Settings have it on.
        /// </summary>
        /// <param name="voice">The assistant.</param>
        public void SetVoiceAssistant(VoiceAssistant voice)
        {
            ArgumentNullException.ThrowIfNull(voice);

            if (_voice is not null)
            {
                _voice.StateChanged -= OnVoiceStateChanged;
                _voice.LevelChanged -= OnVoiceLevelChanged;
                _voice.CommandRecognized -= OnVoiceCommandRecognized;
            }

            _voice = voice;
            _voice.CommandRouter = RouteVoiceCommand;
            _voice.UseNoiseSuppression = _pageSettingsService.IsNoiseSuppressionEnabled;
            _voice.StateChanged += OnVoiceStateChanged;
            _voice.LevelChanged += OnVoiceLevelChanged;
            _voice.CommandRecognized += OnVoiceCommandRecognized;
            _voiceState = voice.State;
            RaiseVoiceStateChanged();

            if (_pageSettingsService.WakeWordEnabled)
            {
                RunVoiceTask(() => voice.SetWakeWordEnabledAsync(true));
            }
        }

        /// <summary>
        /// Sends a recognized voice command to the agent chat, or to the command loop when the agent is off.
        /// </summary>
        /// <param name="text">The transcribed command.</param>
        /// <returns><c>true</c> when the chat or the command loop takes the command.</returns>
        public bool RouteVoiceCommand(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (_agentRuntime is null && _commandInput is null)
            {
                return false;
            }

            var command = text.Trim();
            void Submit()
            {
                if (_agentRuntime is not null)
                {
                    _ = SubmitVoiceCommandAsync(command);
                }
                else
                {
                    SubmitVoiceCommandToCommandLoop(command);
                }
            }

            // The assistant recognizes commands on a background thread.
            if (Dispatcher.UIThread.CheckAccess())
            {
                Submit();
            }
            else
            {
                Dispatcher.UIThread.Post(Submit);
            }

            return true;
        }

        /// <summary>
        /// Runs a voice command as a turn in the selected chat, starting a chat when none is open.
        /// While another turn runs, the command waits for it. The reply is read aloud when Settings say so.
        /// </summary>
        /// <param name="text">The transcribed command.</param>
        public async Task SubmitVoiceCommandAsync(string text)
        {
            if (SelectedAgentChatSession is null)
            {
                CreateNewAgentChat();
            }

            await StartOrQueueAgentTurnAsync(SelectedAgentChatSession!, text, text, fromVoice: true);
        }

        /// <summary>
        /// Opens a new agent chat on the chat page, as the tray's "New task" does.
        /// </summary>
        public void StartNewTask()
        {
            NavigateTo(NavView.Coordinator);
            CreateNewAgentChat();
        }

        /// <summary>
        /// Shows that the talk shortcut couldn't be registered because another app holds it.
        /// </summary>
        /// <param name="shortcut">The shortcut that failed.</param>
        public void ReportTalkShortcutUnavailable(string shortcut)
        {
            AddLog($"VOICE_SHORTCUT_UNAVAILABLE: {shortcut}");
            ShowVoiceProblem(Loc.Format("Voice.Problem.ShortcutInUse", shortcut));
        }

        /// <summary>
        /// Returns whether a finished reply is read aloud under a <see cref="ISettingsService.SpokenReplies"/> mode.
        /// </summary>
        /// <param name="mode">The saved mode: Off, Voice or All.</param>
        /// <param name="fromVoice">Whether the turn started from a voice command.</param>
        public static bool ShouldReadReplyAloud(string? mode, bool fromVoice)
        {
            if (string.Equals(mode, SpokenRepliesAll, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return fromVoice && !string.Equals(mode, SpokenRepliesOff, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the voice status line for a state in the current interface language.
        /// </summary>
        /// <param name="state">What voice is doing.</param>
        /// <param name="progress">The model download progress, 0 to 1.</param>
        /// <param name="wakePhrase">The wake phrase.</param>
        public static string FormatVoiceState(VoiceState state, double progress, string wakePhrase)
        {
            return state switch
            {
                VoiceState.Off => Loc.Get("Voice.State.Off"),
                VoiceState.WakeListening => Loc.Format("Voice.State.WakeListening", wakePhrase),
                VoiceState.Preparing => Loc.Format("Voice.State.Preparing", progress),
                VoiceState.Listening => Loc.Get("Voice.State.Listening"),
                VoiceState.Transcribing => Loc.Get("Voice.State.Transcribing"),
                VoiceState.Speaking => Loc.Get("Voice.State.Speaking"),
                _ => Loc.Get("Voice.State.Ready")
            };
        }

        /// <summary>
        /// Returns the text shown when a voice command fails, or <c>null</c> for <see cref="VoiceProblem.None"/>.
        /// </summary>
        /// <param name="problem">What went wrong.</param>
        public static string? FormatVoiceProblem(VoiceProblem problem)
        {
            return problem switch
            {
                VoiceProblem.None => null,
                _ => Loc.Get($"Voice.Problem.{problem}")
            };
        }

        private void InitializeVoice()
        {
            TalkCommand = ReactiveCommand.Create(ToggleTalk);
            CancelVoiceCommand = ReactiveCommand.Create(CancelVoice);
            ToggleWakeWordCommand = ReactiveCommand.Create(ToggleWakeWord);
            ToggleSpokenRepliesCommand = ReactiveCommand.Create(ToggleSpokenReplies);
            _pageSettingsService.SettingChanged += OnVoiceSettingChanged;
        }

        private void ToggleSpokenReplies()
        {
            if (IsSpokenRepliesOn)
            {
                _spokenRepliesOnMode = _pageSettingsService.SpokenReplies;
                _pageSettingsService.SpokenReplies = SpokenRepliesOff;
                if (_voice?.State == VoiceState.Speaking)
                {
                    _voice.Cancel();
                }

                return;
            }

            _pageSettingsService.SpokenReplies = _spokenRepliesOnMode;
        }

        /// <summary>
        /// Starts or stops talking, as the talk button and the talk shortcut do.
        /// </summary>
        public void ToggleTalk()
        {
            if (_voice is null)
            {
                AddLog("VOICE_UNAVAILABLE");
                return;
            }

            ClearVoiceProblem();
            var voice = _voice;
            RunVoiceTask(voice.ToggleTalkAsync);
        }

        /// <summary>
        /// Cancels the recording or the spoken reply. Returns <c>false</c> when neither is in progress.
        /// </summary>
        public bool CancelVoice()
        {
            if (_voice is null || !IsVoiceCancelVisible)
            {
                return false;
            }

            _voice.Cancel();
            return true;
        }

        private void ToggleWakeWord()
        {
            if (_voice is null)
            {
                AddLog("VOICE_UNAVAILABLE");
                return;
            }

            // Save the choice without the change event turning the listener on a second time.
            var enabled = !_voice.IsWakeWordEnabled;
            _isSavingWakeWordSetting = true;
            try
            {
                _pageSettingsService.WakeWordEnabled = enabled;
            }
            finally
            {
                _isSavingWakeWordSetting = false;
            }

            ApplyWakeWordSetting(enabled);
        }

        private void ApplyWakeWordSetting(bool enabled)
        {
            if (_voice is null)
            {
                return;
            }

            var voice = _voice;
            AddLog(enabled ? "VOICE_WAKE_PHRASE: ON" : "VOICE_WAKE_PHRASE: OFF");
            RunVoiceTask(async () =>
            {
                await voice.SetWakeWordEnabledAsync(enabled);
                Dispatcher.UIThread.Post(RaiseVoiceStateChanged);
            });
        }

        private void OnVoiceSettingChanged(object? sender, SettingChangedEventArgs e)
        {
            // Settings change on the UI thread, from the Settings page or this view model.
            switch (e.SettingName)
            {
                case nameof(ISettingsService.WakeWordEnabled):
                    if (!_isSavingWakeWordSetting
                        && _voice is not null
                        && _voice.IsWakeWordEnabled != _pageSettingsService.WakeWordEnabled)
                    {
                        ApplyWakeWordSetting(_pageSettingsService.WakeWordEnabled);
                    }

                    break;
                case nameof(ISettingsService.WakeWord):
                    RaiseVoiceStateChanged();
                    break;
                case nameof(ISettingsService.TalkShortcut):
                    this.RaisePropertyChanged(nameof(TalkShortcut));
                    this.RaisePropertyChanged(nameof(TalkToolTip));
                    TalkShortcutChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case nameof(ISettingsService.SpokenReplies):
                    this.RaisePropertyChanged(nameof(IsSpokenRepliesOn));
                    this.RaisePropertyChanged(nameof(SpokenRepliesToolTip));
                    break;
                case nameof(ISettingsService.IsNoiseSuppressionEnabled):
                    if (_voice is not null)
                    {
                        _voice.UseNoiseSuppression = _pageSettingsService.IsNoiseSuppressionEnabled;
                    }

                    break;
            }
        }

        private void SubmitVoiceCommandToCommandLoop(string text)
        {
            if (_commandInput is null)
            {
                return;
            }

            AddAgentChatMessage(AgentChatMessageViewModel.UserRole, text);
            if (!IsHostRunning)
            {
                AddLog("VOICE_COMMAND_SKIPPED: command mode is paused");
                AddAgentChatMessage(AgentChatMessageViewModel.SystemRole, Loc.Get("Voice.CommandModePaused"));
                return;
            }

            Interlocked.Increment(ref _pendingVoiceCommands);
            _commandInput.SubmitCommand(text);
        }

        /// <summary>
        /// Reads a reply aloud when the <see cref="ISettingsService.SpokenReplies"/> setting asks for it.
        /// </summary>
        /// <param name="reply">The reply as the chat shows it.</param>
        /// <param name="fromVoice">Whether the turn started from a voice command.</param>
        private void ReadReplyAloud(string? reply, bool fromVoice)
        {
            if (_voice is null || string.IsNullOrWhiteSpace(reply)
                || !ShouldReadReplyAloud(_pageSettingsService.SpokenReplies, fromVoice))
            {
                return;
            }

            var voice = _voice;
            RunVoiceTask(() => voice.SpeakAsync(reply));
        }

        /// <summary>
        /// Reads a command loop reply aloud when it answers a voice command.
        /// </summary>
        /// <param name="reply">The reply.</param>
        private void ReadCommandLoopReplyAloud(string reply)
        {
            var fromVoice = false;
            int pending;
            while ((pending = Volatile.Read(ref _pendingVoiceCommands)) > 0)
            {
                if (Interlocked.CompareExchange(ref _pendingVoiceCommands, pending - 1, pending) == pending)
                {
                    fromVoice = true;
                    break;
                }
            }

            ReadReplyAloud(reply, fromVoice);
        }

        private void RunVoiceTask(Func<Task> action)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => AddLog($"VOICE_ERROR: {ex.Message}"));
                }
            });
        }

        private void OnVoiceStateChanged(object? sender, VoiceStateChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var previous = _voiceState;
                _voiceState = e.State;
                if (e.Progress is { } progress)
                {
                    _voiceProgress = progress;
                }

                if (e.State != VoiceState.Listening)
                {
                    _voiceLevel = 0;
                }

                if (e.Problem != VoiceProblem.None)
                {
                    AddLog(string.IsNullOrWhiteSpace(e.Detail)
                        ? $"VOICE_PROBLEM: {e.Problem}"
                        : $"VOICE_PROBLEM: {e.Problem} {e.Detail}");
                    ShowVoiceProblem(FormatVoiceProblem(e.Problem));
                }
                else if (e.State is VoiceState.Listening or VoiceState.Preparing)
                {
                    ClearVoiceProblem();
                }

                if (previous != e.State && e.State is VoiceState.Listening)
                {
                    AddLog("VOICE_LISTENING");
                }

                RaiseVoiceStateChanged();
            });
        }

        private void OnVoiceLevelChanged(object? sender, float level)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_voiceState != VoiceState.Listening)
                {
                    return;
                }

                // A soft curve, so ordinary speech fills most of the meter.
                _voiceLevel = Math.Clamp(Math.Sqrt(level), 0, 1);
                this.RaisePropertyChanged(nameof(VoiceMeterValue));
            });
        }

        private void OnVoiceCommandRecognized(object? sender, string text)
        {
            Dispatcher.UIThread.Post(() => AddLog($"VOICE_COMMAND: {text}"));
        }

        private void ShowVoiceProblem(string? text)
        {
            if (text is null)
            {
                return;
            }

            _voiceProblemText = text;
            var version = Interlocked.Increment(ref _voiceProblemVersion);
            RaiseVoiceStateChanged();
            DispatcherTimer.RunOnce(() =>
            {
                if (version == _voiceProblemVersion)
                {
                    ClearVoiceProblem();
                }
            }, VoiceProblemDisplayTime);
        }

        private void ClearVoiceProblem()
        {
            if (_voiceProblemText is null)
            {
                return;
            }

            _voiceProblemText = null;
            Interlocked.Increment(ref _voiceProblemVersion);
            RaiseVoiceStateChanged();
        }

        private void RaiseVoiceStateChanged()
        {
            this.RaisePropertyChanged(nameof(IsVoiceAvailable));
            this.RaisePropertyChanged(nameof(VoiceState));
            this.RaisePropertyChanged(nameof(IsVoiceListening));
            this.RaisePropertyChanged(nameof(IsVoiceBusy));
            this.RaisePropertyChanged(nameof(IsWakeWordEnabled));
            this.RaisePropertyChanged(nameof(IsVoiceStatusVisible));
            this.RaisePropertyChanged(nameof(IsVoiceMeterVisible));
            this.RaisePropertyChanged(nameof(VoiceMeterValue));
            this.RaisePropertyChanged(nameof(IsVoiceCancelVisible));
            this.RaisePropertyChanged(nameof(VoiceStatusText));
            this.RaisePropertyChanged(nameof(VoiceStatusColor));
            this.RaisePropertyChanged(nameof(TalkToolTip));
            this.RaisePropertyChanged(nameof(SpokenRepliesToolTip));

            _trayIconService?.SetVoiceEnabled(IsWakeWordEnabled);
            _trayIconService?.UpdateStatus(
                _voiceState.ToString(),
                _voiceState is VoiceState.WakeListening or VoiceState.Listening);
        }

        private void CleanupVoice()
        {
            _pageSettingsService.SettingChanged -= OnVoiceSettingChanged;
            if (_voice is null)
            {
                return;
            }

            _voice.StateChanged -= OnVoiceStateChanged;
            _voice.LevelChanged -= OnVoiceLevelChanged;
            _voice.CommandRecognized -= OnVoiceCommandRecognized;
            _voice.Dispose();
            _voice = null;
        }
    }
}
