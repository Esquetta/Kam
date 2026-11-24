# Changelog

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
