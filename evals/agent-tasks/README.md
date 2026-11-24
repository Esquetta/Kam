# Agent task eval

The [skill routing eval](../skill-routing/README.md) checks one decision. This eval checks whether the agent finishes a job that takes several steps: it runs the real agent loop (`AgentRuntime`, its system prompt, every built-in skill as a tool) on file tasks, and afterwards looks at the files.

## How it works

- **Tasks.** [`tasks.json`](tasks.json) has 8 tasks, each in English and Turkish, so 16 runs per model:
  - find a file by its contents
  - fix a bug in a function
  - rename files by extension
  - back up and edit a JSON file
  - count TODO lines
  - move images into a folder
  - add up a CSV column
  - write a README listing folders

  Each task lists the files to start from and the checks to run afterwards: a file exists, is gone, contains or equals some text, or the reply contains some text.
- **Sandbox.** Each run gets a fresh folder under the system temp folder, and Kam is told it is the workspace. The eval replaces the approval gate with one that acts as the user:
  - It allows the built-in file tools when every path argument is absolute and inside that folder.
  - It refuses the shell, apps, messaging, the clipboard and the web, any path outside the folder, and any call that would open a window.

  A refused call reaches the model as Kam's normal "blocked by your permission rules" result.
- **Nothing reaches your Kam data.** MCP servers, plugins and Agent Skills from your setup are not loaded. Chat sessions, skill history, the audit log and skill policies go to the temp folder and are deleted afterwards.
- **Pass.** Every check passes. A test makes sure no task passes on its starting files.
- **Also recorded.** Tool calls, failed calls (a tool that ran and reported an error), blocked calls with their arguments, turn errors, wall time and tokens. Each run also records whether the final reply is in the language of the request. This is a heuristic: a Turkish reply uses Turkish letters (ç, ğ, ı, ö, ş, ü), and an English one uses none.

## Results, 2026-11-24

Four OpenAI models, one run each, before the fix below: [`results/2026-11-24-before`](results/2026-11-24-before/summary.md).

| Model | Passed | EN | TR | Avg tool calls | Median s | Avg input tokens | English tasks answered in Turkish |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| gpt-5.4-mini | 16/16 | 100% | 100% | 2.8 | 3.4 | 10549 | 6 of 8 |
| gpt-5.5 | 15/16 | 100% | 88% | 4.6 | 9.0 | 14687 | 2 of 8 |
| gpt-5-mini | 15/16 | 100% | 88% | 3.9 | 12.4 | 14879 | 1 of 8 |
| gpt-5.4-nano | 13/16 | 75% | 88% | 5.1 | 5.7 | 15433 | 6 of 8 |

What failed:

- **Arithmetic.** gpt-5.4-nano wrote 1275 and 1255 for a CSV column that sums to 1250. There is no calculator tool, and the shell is blocked here.
- **Giving up after a blocked call.** gpt-5.4-nano tried to move images with the shell, was blocked, and stopped without trying `files_move`. gpt-5-mini had one `files_read` blocked and stopped instead of retrying with an absolute path. That run predates logging of blocked arguments, so the exact path it used is unknown.
- **A locked file.** On gpt-5.5, one `files_move` failed with "the file is used by another process". Nothing in Kam holds the file, so this is most likely the virus scanner or search indexer opening a file seconds after it was written. A real user's machine has the same scanners.
- **Reply language.** 15 of 32 English requests got a Turkish answer. Kam's file and app tools returned hard-coded Turkish text ("Dosya başarıyla taşındı", "Hata: ..."). The most likely cause is that models followed the language of the tool results instead of the user's; the rerun below is meant to confirm it.

## The fix: English tool text

The file, app and web-research tools now return English. Failure messages start with `Error:` so Kam still tells failures from successes. The web research summary prompt no longer forces Turkish; it answers in the language of the topic.

Rerun on the two cheaper models only, one run each: [`results/2026-11-24`](results/2026-11-24/summary.md).

| Model | Passed before | after | English tasks answered in Turkish before | after |
| --- | ---: | ---: | ---: | ---: |
| gpt-5.4-mini | 16/16 | 15/16 | 6 of 8 | 1 of 8 |
| gpt-5.4-nano | 13/16 | 13/16 | 6 of 8 | 5 of 8 |

- **gpt-5.4-mini now answers in the user's language.** English tool text was the main cause for this model.
- **gpt-5.4-nano still drifts into Turkish.** The tool text was not the only cause for it. Each task runs in its own session, so the Turkish requests in the same run are not the cause. One untested guess is the "Istanbul" time zone in the system prompt.
- **Pass rates did not change beyond noise.** The new failures:
  - gpt-5.4-mini wrote an answer.txt that did not contain "march.txt", although its reply says it did. The sandbox is deleted after the run, so the file cannot be inspected.
  - gpt-5.4-nano moved `c.log` along with the `.txt` files. Its reply mentions "file in use" errors when it ran several moves at once, which matches the locked file seen on gpt-5.5 before. Parallel `files_move` calls may conflict; this is worth a look.
  - gpt-5.4-nano added up the CSV wrong (1375).
  - gpt-5.4-nano asked `files_write` to open the file afterwards, was blocked by the sandbox, and gave up instead of writing without opening. In the app that call would have run, so this failure is partly the sandbox's.

## Limits

- One attempt per task and model, at the provider's default temperature.
- File tasks only. No shell, so the model cannot run code to compute; the CSV total is done in its head.
- Small, clean workspaces. Large repositories, long files and ambiguous requests are not tested.
- Checks look at the result, not at how it was reached. A model that deletes and rewrites a file passes the same as one that edits it.

## Run it

```powershell
$env:OPENAI_API_KEY = "<key>"
dotnet run --project src/SmartVoiceAgent.AgentHost.ConsoleApp -- --agent-task-eval --provider OpenAI --endpoint https://api.openai.com/v1 --api-key-env OPENAI_API_KEY --models "gpt-5.4-nano,gpt-5.4-mini"
```

The defaults are OpenRouter (`OPENROUTER_API_KEY`), `evals/agent-tasks/tasks.json` and `evals/agent-tasks/results/<date>/`. Each run writes `attempts.jsonl` (one line per task) and `summary.md`.
