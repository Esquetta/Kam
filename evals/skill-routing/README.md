# Skill routing eval

Kam can turn a request into an action in two ways:

- **JSON plan.** `ModelSkillPlannerService` lists the skills in the system prompt and asks the model for one JSON object (`skillId`, `arguments`). Kam parses and validates it. This is the path used when `AgentRuntime:Enabled=false`.
- **Tool call.** The agent loop (`AgentRuntime`) offers every skill as a native tool through the provider's function-calling API, with the agent system prompt.

This eval sends the same requests through both paths and checks whether the model picks the right skill with the right arguments. Nothing is executed.

## What is measured

- **Cases.** [`cases.json`](cases.json): 24 single-step desktop tasks (apps, files, system, clipboard, web, email/SMS, shell), each in English and Turkish, so 48 requests. A case lists the skills that do the job (some tasks have more than one valid skill, such as `files.read` and `file.read`) and the arguments to check.
- **Skills offered.** The 62 built-in skills, the same set in both modes. `agents.run` is left out because the agent loop does not offer it either.
- **Right skill.** The model chose one of the expected skills.
- **Right skill + args.** It also gave arguments that pass Kam's own argument validation, and each checked argument matches (case, path separators and Turkish ı/İ ignored; `=` means exact, `|` separates accepted values).
- **First call right.** The first thing the model did was the right skill. In tool mode a model may look before it acts, such as `files_exists` before `files_delete`. The eval answers such calls with a simulated `OK` and lets the model continue for up to 4 model calls, as the agent loop would. "Right skill" counts the task as done if the expected skill is reached within those calls; "First call right" does not.
- **Cost and speed.** Model calls, wall time and tokens, summed over the calls of one attempt.

## Results, 2026-11-24

Four OpenAI models, one run each. Full table, every miss and the raw attempts: [`results/2026-11-24`](results/2026-11-24/summary.md).

| Model | Mode | Right skill | Right skill + args | First call right | Model calls | Median ms | Input tokens |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| gpt-5.5 | JSON plan | 100% | 100% | 100% | 1.0 | 1944 | 1158 |
| gpt-5.5 | tool call | 100% | 100% | 96% | 1.0 | 1459 | 2737 |
| gpt-5.4-mini | JSON plan | 98% | 98% | 98% | 1.0 | 818 | 1158 |
| gpt-5.4-mini | tool call | 98% | 98% | 94% | 1.1 | 919 | 2791 |
| gpt-5.4-nano | JSON plan | 96% | 96% | 96% | 1.0 | 1212 | 1158 |
| gpt-5.4-nano | tool call | 96% | 96% | 79% | 1.2 | 1032 | 3238 |
| gpt-5-mini | JSON plan | 100% | 96% | 100% | 1.0 | 4394 | 1158 |
| gpt-5-mini | tool call | 100% | 96% | 83% | 1.2 | 2879 | 3181 |

What this says:

- **Accuracy is the same within noise.** On these single-step tasks both paths reach the right skill 96–100% of the time. The JSON planner is not more accurate than native tool calling, so it is no reason to keep a second routing path for accuracy.
- **Tool calling costs 2.4–2.8× the input tokens** (2.7–3.2k against 1.2k per attempt), because full JSON schemas for 62 tools are larger than the planner's one-line skill list. Output tokens are lower.
- **Tool calling looks before it acts.** Before copying, deleting, closing, killing or sending, models often call a read-only tool first (an existence check, a process list, a validation). gpt-5.4-nano and gpt-5-mini did this in 8 cases each: 79–83% first call right against 96–100% right skill. Each look adds a model call.
- **Turkish misses are mostly one word.** "Panoda ne var?" ("what is on the clipboard?") sent gpt-5.4-nano to `system.info` and gpt-5.4-mini to `accessibility.tree`, in both modes. The clipboard skills did not mention *pano*. Fixed, see below.
- **Free-text enum arguments break.** `system.device.control` takes `action` as free text; models sent `volume_up` and `increase by 10%`, which the handler rejects. Fixed with allowed values, see below.
- **The eval is sensitive to simulated tool results.** Two earlier runs answered intermediate tool calls with "OK (simulated: this evaluation does not run tools)". Models then often stopped to ask for confirmation in prose, and gpt-5.4-nano's tool-call score fell to 83–90%. The published run uses a plain `OK`.

## After the fixes

The two fixes from the first run:

- The clipboard skills now have descriptions that mention *pano*.
- Skill arguments can list allowed values. They reach the tool schema as `enum` and the planner prompt as "one of", and the validator enforces them. `system.device.control` now lists its devices and actions.

The same four models, one run each: [`results/2026-11-24-fixes`](results/2026-11-24-fixes/summary.md).

| Model | Mode | Right skill + args before | after | First call right after |
| --- | --- | ---: | ---: | ---: |
| gpt-5.5 | JSON plan | 100% | 100% | 100% |
| gpt-5.5 | tool call | 100% | 100% | 96% |
| gpt-5.4-mini | JSON plan | 98% | 100% | 100% |
| gpt-5.4-mini | tool call | 98% | 100% | 92% |
| gpt-5.4-nano | JSON plan | 96% | 100% | 100% |
| gpt-5.4-nano | tool call | 96% | 96% | 88% |
| gpt-5-mini | JSON plan | 96% | 100% | 100% |
| gpt-5-mini | tool call | 96% | 96% | 85% |

- **The targeted cases all pass.** Across the four models and both modes, "Panoda ne var?" and the two volume cases make 24 attempts. 7 were wrong before the fixes and 0 after. This is the set the fixes were written for, so it shows that the fixes work, not that the eval generalises.
- **The planner prompt grew by 143 tokens** (1158 to 1301) because of the new descriptions and value lists.
- **Tool mode still asked for confirmation in prose.** Before deleting a file, killing a process or sending an SMS, models often stopped to ask "are you sure?". Kam already shows its own approval prompt for these calls. gpt-5-mini once killed the process through `shell.run` instead of `system.process.kill`.

## After the approval prompt change

The agent system prompt now says: when the user asked for a risky action, make the call; Kam's approval settings decide whether the user confirms it, so don't ask in the reply first. Only tool mode was rerun, since the JSON planner does not use this prompt: [`results/2026-11-24-approval`](results/2026-11-24-approval/summary.md).

| Model | Right skill + args before | after | First call right before | after |
| --- | ---: | ---: | ---: | ---: |
| gpt-5.5 | 100% | 100% | 96% | 100% |
| gpt-5.4-mini | 100% | 100% | 92% | 94% |
| gpt-5.4-nano | 96% | 100% | 88% | 85% |
| gpt-5-mini | 96% | 98% | 85% | 81% |

- **No more misses from asking.** The two earlier published runs had 2 and 3 tool-mode misses where the model asked instead of acting; one of them was about the clipboard wording. This run had none: 191 of 192 attempts reached the right skill.
- **The one miss comes from the simulation.** gpt-5-mini checked whether the destination file existed, got `OK`, and asked whether to overwrite it. A real check would have said the file was not there.
- **Models still look before they act.** First call right did not move (173 of 192 in both runs). The change stops the asking, not the checking.
- One run each, so a difference of one or two cases per model is noise.

## Limits

- One attempt per case and model, at the provider's default temperature. Differences of a few cases are within noise.
- One provider. Models from other vendors and local models (Ollama) were not run; the command takes any OpenAI-compatible or Anthropic endpoint.
- Single-step tasks only. Multi-step work, recovery from tool errors and answer quality are not measured.
- Simulated tool results are always `OK`. A model that would only act after seeing real data is not tested on that data.
- Argument checks look for key values (a path, a name), not the whole argument set.

## Run it

```powershell
$env:OPENAI_API_KEY = "<key>"
dotnet run --project src/SmartVoiceAgent.AgentHost.ConsoleApp -- --skill-routing-eval --provider OpenAI --endpoint https://api.openai.com/v1 --api-key-env OPENAI_API_KEY --models "gpt-5.4-nano,gpt-5.4-mini"
```

The defaults are OpenRouter (`OPENROUTER_API_KEY`), `evals/skill-routing/cases.json` and `evals/skill-routing/results/<date>/`. `--modes json` or `--modes tool` runs one path only. Each run writes `attempts.jsonl` (one line per attempt) and `summary.md`.
