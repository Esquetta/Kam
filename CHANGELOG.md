# Changelog

## [Unreleased]

- Contrast fixes from the Kam Studio review: off switches use a track that stands out from cards and the knob (3:1), the light theme's muted text and error red are darker (4.5:1 on hovered rows and surfaces), and the light accent tint applied at start matches the theme file.
- Kam Studio look: the app takes the prototype's palettes (dark and light), Geist and Geist Mono type (bundled, SIL OFL) and its controls: smaller buttons, chips, risk badges (read, write, shell), panels, segmented tabs and a 30x17 switch. The sidebar is a 232px rail with Work, a numbered Build ladder (Prompts, Agents, Loops, Graphs, Agent Builder) and Manage; ladder steps, Runs and the spend card are marked partial or planned and do not open anything yet. The top bar is 48px with search, the wake phrase pill and a mic button. Tasks is a three-pane workbench (270px list, thread, 290px Activity pane) with flat task rows, accent user bubbles, plain agent replies (Kam's own notices keep a System label), risk badges on tool steps and a composer with Attach, mode, mic and Send plus key hints. Needs you lists approvals as rows in one panel. Layout follows 1280, 980 and 760px breakpoints; the window can shrink to 640px.
- Settings > Appearance: pick the theme (dark or light, now remembered across starts) and an accent color: Violet, Blue, Teal, Green, Rose or Graphite. The choice recolors buttons, links, selection, switches and the logo at once. Every preset keeps button and accent text at 4.5:1 contrast or more; the dark theme's button hover is darker so its text stays readable.
- Tasks: the list is grouped into Needs you, Running and Recent, and drops the "Chats" header; search says tasks. The title bar has a search field (Ctrl+K) and shows when the "Hey Kam" wake phrase is on. The Activity panel's Context tab shows only the task's context; Plan trace and Skill results moved to Event stream.
- Activity panel, Context tab: a "This task" section shows how full the selected task's context window is (for example "54.4k of 64k tokens", with a meter that turns orange near the budget), how often it was compacted, and a Compact now button. After a compaction the size is measured again on the next turn. The size comes from the last model request of the latest turn; `AgentTurnCompleted` now carries it as `ContextTokens`.
- Sidebar: grouped into Work (Tasks, Needs you), Build (Skills, Extensions) and Manage (Integrations, Diagnostics, Settings). Tasks shows how many tasks are running. The new Needs you page lists every tool call waiting for approval across all tasks, with Approve, Always allow, Deny and Open task, and its count shows in the sidebar.
- The agent prompt says to reply in the language of the latest message, even when tool output or files are in another language, and gives the time zone as an offset (UTC+03:00) instead of a city name. An English request on a Turkish-region PC got a Turkish reply.
- Settings: while the agent model has no key, its card names the planner model chat actually runs on ("No key here yet, so chat uses gpt-5.5 on OpenAI from the planner profile") instead of saying nothing is set up.
- A tool step's time no longer includes the wait for your approval: a command approved after 25 s that ran in 0.4 s shows 0.4 s.
- Tasks: the workbench page is now called Tasks (Görevler) and lives in its own view. Task rows show an orange dot and "Needs approval" while a tool call waits for you, and an accent dot while the agent works. Finished tool steps fold to one line with their duration; click one to see its arguments and result. Steps waiting for approval or that failed stay open.
- Activity panel: shown only beside the chat, with a close button and a way back from the chat header. Its first tab, Steps, shows the latest turn in the selected chat: each tool call with its outcome and time, then the totals for tools, time and tokens. The second message box on other pages is gone, so those pages use the full width.
- Diagnostics leads with four plain status cards (model, tools, MCP servers, voice), each with one action when something needs attention; the technical checklist and reports moved under "Details for troubleshooting".
- Skills, Integrations and Extensions: no developer message on a clean profile, status badges in sentence case with colours that match their meaning, and the Docs link in the same place on every Discover card.
- Settings says whether the agent model still needs a key instead of showing an internal profile id.
- The window is named "Kam" for the taskbar and Alt-Tab, without drawing the name over the logo, and the separate full-screen button is gone. The light theme separates sidebar, page and cards more clearly, and off switches use a muted knob.
- First run: with no model connected, the empty workbench shows a setup card (OpenRouter, Ollama, OpenAI, Anthropic) that opens Settings on that provider, and a chat that already has messages shows a "Set up" strip above the composer.
- Settings: the agent model comes first, with Test Connection next to its key; the command mode planner moved under an "advanced" toggle. Test Connection checks whichever profiles are set up instead of always requiring the planner. The duplicate "Start minimized" switch is gone.
- Workbench copy: the thread list and chat header drop the "Agent threads" and "Conversation timeline" subtitles, thread rows show the status only while the agent works, and 22 icon-only buttons have accessible names.
- File writes, moves, copies and deletes retry briefly when another process (usually the virus scanner) holds a file it just wrote. Moving a file 300 ms after writing it failed about one time in four on a test machine.
- Agent task eval: `--agent-task-eval` in the console host runs 16 multi-step file tasks (8 in English and Turkish) through the real agent loop, each in a fresh temp workspace where only file tools inside it may run, and checks the files afterwards. First results in [evals/agent-tasks](evals/agent-tasks/README.md).
- File, app and web-research tools return English text, with failures starting with `Error:`. They returned Turkish, and models often answered English requests in Turkish.
- Skill routing eval: `--skill-routing-eval` in the console host sends 48 English and Turkish requests through the JSON planner and through native tool calling, and scores the skill and arguments each model picks. First results in [evals/skill-routing](evals/skill-routing/README.md).
- Skill arguments can list allowed values: they reach the tool schema as `enum` and the planner prompt as "one of", and the validator enforces them. Device control now lists its devices and actions, so models no longer send values such as `volume_up` that the handler rejects.
- Clipboard skills have descriptions, including the Turkish word *pano*, so "Panoda ne var?" routes to the clipboard.
- The agent prompt tells the model to make a requested risky call and let Kam's approval settings ask, instead of asking "are you sure?" in its reply first.

## [0.1.0] - 2026-11-24

First downloadable build. Pre-release: expect rough edges.

**Download:** `Kam-0.1.0-win-x64.zip` for Windows 10/11 x64. It is self-contained, so no .NET install is needed. Unzip and run `Kam.exe`. The build is not code-signed; SmartScreen may ask for "More info" > "Run anyway".

**First run:** open Settings > AI Runtime and add a model (OpenRouter, OpenAI, Anthropic, Ollama or another OpenAI-compatible endpoint). Until then the title bar shows "Set up a model" and chat tells you what is missing.

What is in it:

- A tool-calling agent loop with built-in skills for apps, files, shell, web search, clipboard and windows.
- Permission modes (Ask, Auto-edit, Full auto) with allow and deny rules for every tool call.
- MCP servers, Agent Skills, plugins and Markdown slash commands, managed on the Extensions page.
- Chat threads saved to disk, a model per thread, and context compaction.
- Voice: push to talk, an optional "Hey Kam" wake phrase and spoken replies, with local Whisper speech-to-text.
- English and Turkish interface, dark and light themes.

Known limits:

- Windows x64 only, no installer and no automatic updates.
- One agent per turn: no subagents, no parallel tool calls, no retry on model errors.
- The local Whisper `large-v3-turbo` model needs a GPU with a Vulkan driver to be usable.
