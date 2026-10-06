using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using ReactiveUI;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// A model offered in the chat header.
    /// </summary>
    /// <param name="ModelId">The model id, or null for the model chosen in Settings.</param>
    /// <param name="Label">The model name shown in the picker.</param>
    /// <param name="Caption">A short note such as "Settings default".</param>
    public sealed record AgentChatModelOption(string? ModelId, string Label, string Caption)
    {
        public bool IsDefault => ModelId is null;

        public override string ToString() => Label;
    }

    /// <summary>
    /// Chat workbench features: thread search, rename and delete, the per-thread model picker,
    /// copying replies, approval notifications and Settings applied while the app runs.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private string _agentChatSearchText = string.Empty;
        private IReadOnlyList<AgentChatModelOption> _agentChatModelOptions = [];
        private AgentChatModelOption? _selectedAgentChatModel;
        private bool _isRefreshingAgentChatModels;
        private IApprovalNotifier? _approvalNotifier;

        /// <summary>
        /// Raised after Settings or Integrations save a change, so the app can apply it without a restart.
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// Gets the settings the Settings and Integrations pages edit.
        /// </summary>
        public ISettingsService SettingsService => _pageSettingsService;

        /// <summary>
        /// Writes text to the clipboard. Tests replace it; by default the main window's clipboard is used.
        /// </summary>
        public Func<string, Task>? ClipboardWriter { get; set; }

        public ICommand RenameAgentChatCommand { get; private set; }

        public ICommand CommitAgentChatRenameCommand { get; private set; }

        public ICommand CancelAgentChatRenameCommand { get; private set; }

        public ICommand DeleteAgentChatCommand { get; private set; }

        public ICommand ConfirmDeleteAgentChatCommand { get; private set; }

        public ICommand CancelDeleteAgentChatCommand { get; private set; }

        public ICommand ClearAgentChatSearchCommand { get; private set; }

        public ICommand CopyAgentMessageCommand { get; private set; }

        public ICommand CopyAgentChatCommand { get; private set; }

        /// <summary>
        /// Gets or sets the sidebar search text; threads whose title or summary match stay listed.
        /// </summary>
        public string AgentChatSearchText
        {
            get => _agentChatSearchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _agentChatSearchText, value ?? string.Empty);
                ApplyAgentChatSearch();
            }
        }

        public bool IsAgentChatSearchActive => !string.IsNullOrWhiteSpace(AgentChatSearchText);

        public bool HasNoAgentChatSearchResults =>
            IsAgentChatSearchActive && !AgentChatSessions.Any(session => session.IsVisibleInList);

        /// <summary>
        /// Gets the models the selected thread can run on: the Settings model first, then the
        /// suggested models for the same provider.
        /// </summary>
        public IReadOnlyList<AgentChatModelOption> AgentChatModelOptions
        {
            get => _agentChatModelOptions;
            private set => this.RaiseAndSetIfChanged(ref _agentChatModelOptions, value);
        }

        /// <summary>
        /// Gets or sets the model the selected thread runs on. The choice is saved with the thread.
        /// </summary>
        public AgentChatModelOption? SelectedAgentChatModel
        {
            get => _selectedAgentChatModel;
            set
            {
                // The picker clears its selection while its list is replaced.
                if (value is null || _isRefreshingAgentChatModels)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedAgentChatModel, value);
                this.RaisePropertyChanged(nameof(AgentChatModelTip));
                if (SelectedAgentChatSession is not { } session
                    || string.Equals(session.ModelId, value.ModelId, StringComparison.Ordinal))
                {
                    return;
                }

                session.ModelId = value.ModelId;
                AddLog($"CHAT_MODEL: {value.Label}");
                _ = SaveAgentSessionChangeAsync(
                    store => store.SetModelAsync(session.SessionId, value.ModelId),
                    "AGENT_THREAD_MODEL_SAVE_FAILED");
            }
        }

        /// <summary>
        /// Gets the model picker's tooltip, naming the model the selected chat runs on.
        /// </summary>
        public string AgentChatModelTip => SelectedAgentChatModel switch
        {
            null => Loc.Get("Workbench.Model.Tip"),
            { IsDefault: true } => Loc.Get("Workbench.Model.DefaultTip"),
            var option => Loc.Format("Workbench.Model.ThreadTip", option.ModelId)
        };

        [MemberNotNull(
            nameof(RenameAgentChatCommand),
            nameof(CommitAgentChatRenameCommand),
            nameof(CancelAgentChatRenameCommand),
            nameof(DeleteAgentChatCommand),
            nameof(ConfirmDeleteAgentChatCommand),
            nameof(CancelDeleteAgentChatCommand),
            nameof(ClearAgentChatSearchCommand),
            nameof(CopyAgentMessageCommand),
            nameof(CopyAgentChatCommand))]
        private void InitializeChatExperience()
        {
            RenameAgentChatCommand = ReactiveCommand.Create<AgentChatSessionViewModel?>(StartAgentChatRename);
            CommitAgentChatRenameCommand = ReactiveCommand.Create<AgentChatSessionViewModel?>(CommitAgentChatRename);
            CancelAgentChatRenameCommand = ReactiveCommand.Create<AgentChatSessionViewModel?>(CancelAgentChatRename);
            DeleteAgentChatCommand = ReactiveCommand.Create<AgentChatSessionViewModel?>(RequestAgentChatDelete);
            ConfirmDeleteAgentChatCommand = ReactiveCommand.CreateFromTask<AgentChatSessionViewModel?>(DeleteAgentChatAsync);
            CancelDeleteAgentChatCommand = ReactiveCommand.Create<AgentChatSessionViewModel?>(session =>
            {
                if (session is not null)
                {
                    session.IsConfirmingDelete = false;
                }
            });
            ClearAgentChatSearchCommand = ReactiveCommand.Create(() => { AgentChatSearchText = string.Empty; });
            CopyAgentMessageCommand = ReactiveCommand.CreateFromTask<AgentChatMessageViewModel?>(CopyAgentMessageAsync);
            CopyAgentChatCommand = ReactiveCommand.CreateFromTask(CopyAgentChatAsync);

            _pageSettingsService.SettingChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Shows approvals the user cannot see, for example while the window is hidden.
        /// </summary>
        public void SetApprovalNotifier(IApprovalNotifier notifier)
        {
            _approvalNotifier = notifier;
        }

        /// <summary>
        /// Called after Settings changes reached the running services.
        /// </summary>
        /// <param name="changedKeys">The configuration keys that changed.</param>
        public void OnRuntimeSettingsApplied(IReadOnlyCollection<string> changedKeys)
        {
            if (changedKeys.Count == 0)
            {
                return;
            }

            AddLog($"SETTINGS_APPLIED: {DescribeSettingsChange(changedKeys)}");
            if (changedKeys.Any(key => key.StartsWith("AIService", StringComparison.OrdinalIgnoreCase)))
            {
                RefreshAgentChatModelOptions();
            }
        }

        /// <summary>
        /// Names what a Settings change affects, such as "models, web search".
        /// Email, SMS and GitHub App settings are read once at startup.
        /// </summary>
        public static string DescribeSettingsChange(IEnumerable<string> changedKeys)
        {
            var areas = changedKeys
                .Select(key => key.Split(':', 2)[0])
                .Select(section => section.ToUpperInvariant() switch
                {
                    "AISERVICE" => "models",
                    "WEBRESEARCH" => "web search",
                    "MCPOPTIONS" => "Todoist",
                    "EMAIL" => "email (after restart)",
                    "SMS" => "SMS (after restart)",
                    "GITHUBAPP" => "GitHub App (after restart)",
                    _ => section
                })
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return string.Join(", ", areas);
        }

        /// <summary>
        /// Rebuilds the model picker for the selected thread from the current Settings and interface language.
        /// </summary>
        public void RefreshAgentChatModelOptions()
        {
            var profile = AiRuntimeConfigurationMapper.ResolveChatProfile(_pageSettingsService);
            var defaultModel = profile?.ModelId;
            var options = new List<AgentChatModelOption>
            {
                new(
                    null,
                    string.IsNullOrWhiteSpace(defaultModel) ? Loc.Get("Workbench.Model.NotSetUp") : defaultModel,
                    Loc.Get("Workbench.Model.SettingsDefault"))
            };

            if (profile is not null)
            {
                options.AddRange(ModelCatalogDefaults.GetModelIds(profile.Provider, profile.ModelId)
                    .Where(model => !string.Equals(model, defaultModel, StringComparison.OrdinalIgnoreCase))
                    .Select(model => new AgentChatModelOption(model, model, profile.Provider.ToString())));
            }

            var threadModel = SelectedAgentChatSession?.ModelId;
            if (!string.IsNullOrWhiteSpace(threadModel)
                && options.All(option => !string.Equals(option.ModelId, threadModel, StringComparison.OrdinalIgnoreCase)))
            {
                options.Insert(1, new AgentChatModelOption(threadModel, threadModel, Loc.Get("Workbench.Model.ThisChat")));
            }

            _isRefreshingAgentChatModels = true;
            try
            {
                AgentChatModelOptions = options;
                _selectedAgentChatModel = options.FirstOrDefault(option =>
                        string.Equals(option.ModelId, threadModel, StringComparison.OrdinalIgnoreCase))
                    ?? options[0];
            }
            finally
            {
                _isRefreshingAgentChatModels = false;
            }

            this.RaisePropertyChanged(nameof(SelectedAgentChatModel));
            this.RaisePropertyChanged(nameof(AgentChatModelTip));
        }

        private void ApplyAgentChatSearch()
        {
            foreach (var session in AgentChatSessions)
            {
                session.IsVisibleInList = session.Matches(AgentChatSearchText);
            }

            this.RaisePropertyChanged(nameof(IsAgentChatSearchActive));
            this.RaisePropertyChanged(nameof(HasNoAgentChatSearchResults));
        }

        private void StartAgentChatRename(AgentChatSessionViewModel? session)
        {
            session ??= SelectedAgentChatSession;
            if (session is null)
            {
                return;
            }

            CloseAgentChatEditors(session);
            session.EditTitle = session.Title;
            session.IsRenaming = true;
        }

        /// <summary>
        /// Saves the title typed in the sidebar. A blank title keeps the old one.
        /// </summary>
        public void CommitAgentChatRename(AgentChatSessionViewModel? session)
        {
            if (session is not { IsRenaming: true })
            {
                return;
            }

            session.IsRenaming = false;
            var title = (session.EditTitle ?? string.Empty).ReplaceLineEndings(" ").Trim();
            if (title.Length > JsonAgentSessionStore.MaxTitleLength)
            {
                title = title[..JsonAgentSessionStore.MaxTitleLength].TrimEnd();
            }

            if (title.Length == 0 || string.Equals(title, session.Title, StringComparison.Ordinal))
            {
                return;
            }

            session.Title = title;
            session.HasCustomTitle = true;
            ApplyAgentChatSearch();
            AddLog($"THREAD_RENAMED: {title}");
            _ = SaveAgentSessionChangeAsync(
                store => store.RenameAsync(session.SessionId, title),
                "AGENT_THREAD_RENAME_FAILED");
        }

        /// <summary>
        /// Closes the title editor without saving.
        /// </summary>
        public void CancelAgentChatRename(AgentChatSessionViewModel? session)
        {
            if (session is not null)
            {
                session.IsRenaming = false;
            }
        }

        private void RequestAgentChatDelete(AgentChatSessionViewModel? session)
        {
            session ??= SelectedAgentChatSession;
            if (session is null || !session.CanDelete)
            {
                return;
            }

            CloseAgentChatEditors(session);
            session.IsConfirmingDelete = true;
        }

        /// <summary>
        /// Closes other rows' title editors and delete prompts, and shows the thread if the search hid it.
        /// </summary>
        private void CloseAgentChatEditors(AgentChatSessionViewModel session)
        {
            foreach (var other in AgentChatSessions)
            {
                other.IsRenaming = false;
                other.IsConfirmingDelete = false;
            }

            if (!session.IsVisibleInList)
            {
                AgentChatSearchText = string.Empty;
            }
        }

        private async Task DeleteAgentChatAsync(AgentChatSessionViewModel? session)
        {
            if (session is null || !session.CanDelete || !AgentChatSessions.Contains(session))
            {
                return;
            }

            session.IsConfirmingDelete = false;
            var index = AgentChatSessions.IndexOf(session);
            if (_queuedAgentMessages.RemoveAll(queued => queued.Session == session) > 0)
            {
                RaiseQueuedAgentMessagesChanged();
            }

            AgentChatSessions.Remove(session);
            if (SelectedAgentChatSession == session)
            {
                var next = AgentChatSessions.Skip(index).FirstOrDefault(other => other.IsVisibleInList)
                    ?? AgentChatSessions.Take(index).LastOrDefault(other => other.IsVisibleInList)
                    ?? AgentChatSessions.FirstOrDefault();
                if (next is null)
                {
                    CreateNewAgentChat();
                }
                else
                {
                    SelectAgentChat(next);
                }
            }

            ApplyAgentChatSearch();
            RaiseAgentChatStateChanged();
            AddLog($"THREAD_DELETED: {session.Title}");
            await SaveAgentSessionChangeAsync(
                store => store.DeleteAsync(session.SessionId),
                "AGENT_THREAD_DELETE_FAILED");
        }

        private async Task SaveAgentSessionChangeAsync(
            Func<SmartVoiceAgent.Core.Interfaces.IAgentSessionStore, Task> change,
            string failureLog)
        {
            if (_agentSessionStore is null)
            {
                return;
            }

            try
            {
                await change(_agentSessionStore);
            }
            catch (Exception ex)
            {
                AddLog($"{failureLog}: {ex.Message}");
            }
        }

        private async Task CopyAgentMessageAsync(AgentChatMessageViewModel? message)
        {
            if (message is null || string.IsNullOrEmpty(message.Content))
            {
                return;
            }

            if (await TryCopyTextAsync(message.Content))
            {
                message.MarkCopied();
            }
        }

        private async Task CopyAgentChatAsync()
        {
            if (SelectedAgentChatSession is not { } session || session.Messages.Count == 0)
            {
                return;
            }

            if (await TryCopyTextAsync(FormatConversation(session)))
            {
                AddLog("COPIED_CONVERSATION");
            }
        }

        /// <summary>
        /// Formats a thread as Markdown: each message under its author, and tool calls as one line each.
        /// </summary>
        public static string FormatConversation(AgentChatSessionViewModel session)
        {
            var builder = new StringBuilder();
            builder.Append("# ").AppendLine(session.Title).AppendLine();
            foreach (var item in session.Messages)
            {
                if (item.IsToolStep)
                {
                    builder.Append("> `").Append(item.ToolName).Append("` · ").AppendLine(item.ToolStatusText).AppendLine();
                    continue;
                }

                builder.Append("**").Append(item.Role).AppendLine(":**").AppendLine(item.Content.Trim()).AppendLine();
            }

            return builder.ToString().TrimEnd() + Environment.NewLine;
        }

        private async Task<bool> TryCopyTextAsync(string text)
        {
            try
            {
                if (ClipboardWriter is not null)
                {
                    await ClipboardWriter(text);
                    return true;
                }

                var clipboard = (global::Avalonia.Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
                if (clipboard is null)
                {
                    AddLog("COPY_FAILED: clipboard unavailable");
                    return false;
                }

                await clipboard.SetTextAsync(text);
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"COPY_FAILED: {ex.Message}");
                return false;
            }
        }

        private void NotifyApprovalRequested(
            AgentChatSessionViewModel session,
            AgentChatMessageViewModel step,
            string requestId)
        {
            _approvalNotifier?.Show(new ApprovalNotice(
                requestId,
                session.Title,
                step.ToolDisplayName,
                step.RiskText,
                ActiveView == NavView.Coordinator && SelectedAgentChatSession == session,
                () =>
                {
                    NavigateTo(NavView.Coordinator);
                    SelectAgentChat(session);
                }));
        }

        private void DismissApprovalNotice(string? requestId)
        {
            if (!string.IsNullOrEmpty(requestId))
            {
                _approvalNotifier?.Dismiss(requestId);
            }
        }
    }
}
