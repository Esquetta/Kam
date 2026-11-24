# Skill routing results

- date: 2026-11-24
- provider: OpenAI (https://api.openai.com/v1)
- cases: 48 (24 English, 24 Turkish), from `evals/skill-routing/cases.json`
- skills offered: 62 built-in skills, the same set in both modes
- one attempt per case; provider errors are retried twice and then left out of the rates

| Model | Mode | Cases | Right skill | Right skill + args | First call right | EN skill | TR skill | Avg model calls | Median ms | Avg input tok | Avg output tok | Provider errors |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `gpt-5-mini` | tool call | 48 | 98% | 98% | 81% | 100% | 96% | 1.3 | 2613 | 3634 | 163 | 0 |
| `gpt-5.4-mini` | tool call | 48 | 100% | 100% | 94% | 100% | 100% | 1.1 | 848 | 3030 | 30 | 0 |
| `gpt-5.4-nano` | tool call | 48 | 100% | 100% | 85% | 100% | 100% | 1.2 | 1092 | 3331 | 31 | 0 |
| `gpt-5.5` | tool call | 48 | 100% | 100% | 100% | 100% | 100% | 1.0 | 1506 | 2849 | 32 | 0 |

## Misses

Every attempt that did not pick the right skill with the expected arguments.

### `gpt-5-mini`, tool call

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| copy-file.tr | files.exists | {} | Stopped after calling files.exists, files.exists: Hedefte zaten C:\Users\me\Desktop\report.pdf dosyası var. Ben C:\Users\me\Downloads\report.pdf dosyasını kopya... |
