using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    public partial class MainWindowViewModel
    {
        /// <summary>Opens the page given as the command parameter; the Build group and Runs in the rail use it.</summary>
        public ReactiveCommand<NavView, Unit> NavigateToPageCommand { get; private set; } = null!;

        /// <summary>Opens the Graphs page on the subagent a chat step started.</summary>
        public ICommand OpenSubagentGraphCommand { get; private set; } = null!;

        /// <summary>Gets the command palette (Ctrl+K): tasks, pages, prompts and actions in one search.</summary>
        public CommandPaletteViewModel CommandPalette { get; } = new();

        /// <summary>Opens the command palette with the current tasks, pages, prompts and actions.</summary>
        public ICommand OpenCommandPaletteCommand { get; private set; } = null!;

        /// <summary>
        /// Raised after a prompt was put in a new task's composer, so the view can focus it.
        /// </summary>
        public event EventHandler? ComposerFocusRequested;

        /// <summary>
        /// Creates the Build and palette commands. Called once from the constructor.
        /// </summary>
        private void InitializeBuild()
        {
            NavigateToPageCommand = ReactiveCommand.Create<NavView>(NavigateTo);
            OpenSubagentGraphCommand = ReactiveCommand.Create<AgentChatMessageViewModel?>(step =>
            {
                if (step?.SubagentRunId is { } runId && SelectedAgentChatSession is { } session)
                {
                    OpenSubagentGraph(session.SessionId, runId);
                }
            });
            OpenCommandPaletteCommand = ReactiveCommand.Create(OpenCommandPalette);

            // The Loops page changes the context budget; the task context meter follows it.
            _pageSettingsService.SettingChanged += (_, e) =>
            {
                if (e.SettingName == nameof(ISettingsService.AgentContextTokenBudget))
                {
                    SetContextTokenBudget(_pageSettingsService.AgentContextTokenBudget);
                }
            };
        }

        /// <summary>
        /// Opens a new task with <paramref name="text"/> in its composer, ready to edit and send.
        /// The Prompts page and the palette's prompts use it.
        /// </summary>
        /// <param name="text">The composer text, such as "/review ".</param>
        public void UsePromptInNewTask(string text)
        {
            StartNewTask();
            CommandInputText = text ?? string.Empty;
            ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Opens the task with <paramref name="sessionId"/> on the Tasks page; Runs rows use it.</summary>
        /// <param name="sessionId">The task's session id.</param>
        public void OpenTaskById(string sessionId)
        {
            var session = AgentChatSessions.FirstOrDefault(item => item.SessionId == sessionId);
            if (session is not null)
            {
                OpenTaskCommand.Execute(session);
            }
        }

        /// <summary>Opens the Graphs page on a task and, when given, one of its subagents; chat steps and Runs rows use it.</summary>
        /// <param name="sessionId">The task's session id.</param>
        /// <param name="runId">The subagent run id, or null.</param>
        public void OpenSubagentGraph(string sessionId, string? runId)
        {
            NavigateTo(NavView.Graphs);
            if (GetOrCreateViewModel(NavView.Graphs) is PageModels.GraphsViewModel graphs)
            {
                graphs.Focus(sessionId, runId);
            }
        }

        /// <summary>
        /// Returns "needs-you" or "running" for an open task, or null; the Runs page shows it as the row status.
        /// </summary>
        /// <param name="sessionId">The task's session id.</param>
        internal string? LiveTaskStatus(string sessionId)
        {
            var session = AgentChatSessions.FirstOrDefault(item => item.SessionId == sessionId);
            return session switch
            {
                { HasPendingApproval: true } => "needs-you",
                { IsRunning: true } => "running",
                _ => null
            };
        }

        /// <summary>Opens the command palette.</summary>
        public void OpenCommandPalette() => CommandPalette.Open(BuildPaletteItems());

        /// <summary>
        /// Returns the palette's entries: actions, pages, open tasks and saved prompts, in that group order.
        /// </summary>
        internal IReadOnlyList<PaletteItem> BuildPaletteItems()
        {
            var actions = Loc.Get("Palette.Group.Actions");
            var pages = Loc.Get("Palette.Group.Pages");
            var tasks = Loc.Get("Palette.Group.Tasks");
            var commands = Loc.Get("Palette.Group.Commands");

            var items = new List<PaletteItem>
            {
                new(actions, Loc.Get("Shell.Tray.NewTask"), null, "Ctrl N", "IconPlus", StartNewTask),
                new(actions, IsDarkMode ? Loc.Get("Shell.LightMode") : Loc.Get("Shell.DarkMode"), null, null,
                    IsDarkMode ? "IconSun" : "IconMoon", ToggleTheme)
            };

            (NavView View, string Title, string Icon, string? Shortcut)[] pageList =
            [
                (NavView.Coordinator, Loc.Get("Shell.Nav.Workbench"), "IconListTodo", null),
                (NavView.Inbox, Loc.Get("Shell.Nav.Inbox"), "IconInbox", null),
                (NavView.Prompts, Loc.Get("Shell.Ladder.Prompts"), "IconFile", null),
                (NavView.Agents, Loc.Get("Shell.Ladder.Agents"), "IconBot", null),
                (NavView.Loops, Loc.Get("Shell.Ladder.Loops"), "IconRefresh", null),
                (NavView.Graphs, Loc.Get("Shell.Ladder.Graphs"), "IconNetwork", null),
                (NavView.Runs, Loc.Get("Shell.Nav.Runs"), "IconActivity", null),
                (NavView.Plugins, Loc.Get("Shell.Nav.Skills"), "IconBlocks", null),
                (NavView.Extensions, Loc.Get("Shell.Nav.Extensions"), "IconPuzzle", null),
                (NavView.Integrations, Loc.Get("Shell.Nav.Integrations"), "IconPlug", null),
                (NavView.Diagnostics, Loc.Get("Shell.Nav.Diagnostics"), "IconCpu", null),
                (NavView.Settings, Loc.Get("Shell.Nav.Settings"), "IconSliders", "Ctrl ,")
            ];
            items.AddRange(pageList.Select(page =>
                new PaletteItem(pages, page.Title, null, page.Shortcut, page.Icon, () => NavigateTo(page.View))));

            items.AddRange(AgentChatSessions.Select(session =>
                new PaletteItem(tasks, session.Title, null, null, "IconChat", () => OpenTaskCommand.Execute(session))));

            if (_agentCommands is not null)
            {
                foreach (var command in _agentCommands.GetCommands())
                {
                    var text = "/" + command.Name + " ";
                    items.Add(new PaletteItem(commands, "/" + command.Name, command.Description, null, "IconTerminal",
                        () => UsePromptInNewTask(text)));
                }
            }

            return items;
        }
    }
}
