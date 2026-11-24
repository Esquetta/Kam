# Skill routing results

- date: 2026-11-24
- provider: OpenAI (https://api.openai.com/v1)
- cases: 48 (24 English, 24 Turkish), from `evals/skill-routing/cases.json`
- skills offered: 62 built-in skills, the same set in both modes
- one attempt per case; provider errors are retried twice and then left out of the rates

| Model | Mode | Cases | Right skill | Right skill + args | First call right | EN skill | TR skill | Avg model calls | Median ms | Avg input tok | Avg output tok | Provider errors |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `gpt-5-mini` | JSON plan | 48 | 100% | 100% | 100% | 100% | 100% | 1.0 | 4072 | 1301 | 278 | 0 |
| `gpt-5-mini` | tool call | 48 | 96% | 96% | 85% | 100% | 92% | 1.2 | 2637 | 3273 | 144 | 0 |
| `gpt-5.4-mini` | JSON plan | 48 | 100% | 100% | 100% | 100% | 100% | 1.0 | 748 | 1301 | 54 | 0 |
| `gpt-5.4-mini` | tool call | 48 | 100% | 100% | 92% | 100% | 100% | 1.1 | 843 | 2974 | 31 | 0 |
| `gpt-5.4-nano` | JSON plan | 48 | 100% | 100% | 100% | 100% | 100% | 1.0 | 1156 | 1301 | 58 | 0 |
| `gpt-5.4-nano` | tool call | 48 | 96% | 96% | 88% | 96% | 96% | 1.1 | 1117 | 3213 | 33 | 0 |
| `gpt-5.5` | JSON plan | 48 | 100% | 100% | 100% | 100% | 100% | 1.0 | 1974 | 1301 | 94 | 0 |
| `gpt-5.5` | tool call | 48 | 100% | 100% | 96% | 100% | 100% | 1.0 | 1500 | 2916 | 35 | 0 |

## Misses

Every attempt that did not pick the right skill with the expected arguments.

### `gpt-5-mini`, tool call

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| delete-file.tr | files.exists | {} | Stopped after calling files.exists: Onaylıyor musunuz? C:\temp\old.log dosyasını kalıcı olarak sileyim mi? (evet / hayır) |
| kill-process.tr | shell.run | {} | Stopped after calling shell.run, shell.run: 4312 numaralı işlem sonlandırıldı. |

### `gpt-5.4-nano`, tool call

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| send-sms.en | - | {} | No tool call: Mesajı hazırladım: **“on my way”**.  Lütfen iki şey netleştirir misin? 1) Bu numaraya mı göndereyim: **+905551112233** (TR formatında)   2) Mesaj ... |
| kill-process.tr | system.processes.list | {} | Stopped after calling system.processes.list, system.processes.list, system.processes.list: “4312” PID’li işlemi sonlandırmam için onay verin.  Lütfen bekleyin: ... |
