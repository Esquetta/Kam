# Skill routing results

- date: 2026-11-24
- provider: OpenAI (https://api.openai.com/v1)
- cases: 48 (24 English, 24 Turkish), from `evals/skill-routing/cases.json`
- skills offered: 62 built-in skills, the same set in both modes
- one attempt per case; provider errors are retried twice and then left out of the rates

| Model | Mode | Cases | Right skill | Right skill + args | First call right | EN skill | TR skill | Avg model calls | Median ms | Avg input tok | Avg output tok | Provider errors |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `gpt-5-mini` | JSON plan | 48 | 100% | 96% | 100% | 100% | 100% | 1.0 | 4394 | 1158 | 292 | 0 |
| `gpt-5-mini` | tool call | 48 | 100% | 96% | 83% | 100% | 100% | 1.2 | 2879 | 3181 | 131 | 0 |
| `gpt-5.4-mini` | JSON plan | 48 | 98% | 98% | 98% | 100% | 96% | 1.0 | 818 | 1158 | 55 | 0 |
| `gpt-5.4-mini` | tool call | 48 | 98% | 98% | 94% | 100% | 96% | 1.1 | 919 | 2791 | 31 | 0 |
| `gpt-5.4-nano` | JSON plan | 48 | 96% | 96% | 96% | 96% | 96% | 1.0 | 1212 | 1158 | 57 | 0 |
| `gpt-5.4-nano` | tool call | 48 | 96% | 96% | 79% | 96% | 96% | 1.2 | 1032 | 3238 | 35 | 0 |
| `gpt-5.5` | JSON plan | 48 | 100% | 100% | 100% | 100% | 100% | 1.0 | 1944 | 1158 | 87 | 0 |
| `gpt-5.5` | tool call | 48 | 100% | 100% | 96% | 100% | 100% | 1.0 | 1459 | 2737 | 41 | 0 |

## Misses

Every attempt that did not pick the right skill with the expected arguments.

### `gpt-5-mini`, JSON plan

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| volume-up.en | system.device.control | {"deviceName":"system","action":"volume_up"} | - |
| volume-up.tr | system.device.control | {"deviceName":"speaker","action":"increase by 10%"} | - |

### `gpt-5-mini`, tool call

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| volume-up.tr | system.device.control | {"deviceName":"volume","action":"status"} | - |
| top-processes.tr | system.processes.list | {"sortBy":"memory","count":10} | - |

### `gpt-5.4-mini`, JSON plan

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| clipboard-get.tr | accessibility.tree | {"maxScreens":1,"includeObjects":true} | - |

### `gpt-5.4-mini`, tool call

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| clipboard-get.tr | accessibility.tree | {} | Stopped after calling accessibility.tree: Panoda şu an görünür bir içerik algılayamadım. İstersen ekranı daha ayrıntılı tarayayım ya da belirli bir uygulama/pen... |

### `gpt-5.4-nano`, JSON plan

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| clipboard-get.tr | system.info | {} | - |
| active-window.en | window.list | {"maxWindows":1} | - |

### `gpt-5.4-nano`, tool call

| Case | Picked | Arguments | Note |
| --- | --- | --- | --- |
| clipboard-get.tr | system.info | {} | Stopped after calling system.info: “Panoda” diye yazmışsınız—tam olarak neyi kastediyorsunuz?  - **Panoda** bir **uygulama/website** mi (ör. “Panoda” adıyla bir... |
| send-sms.en | - | {} | No tool call: Got it — I can send the SMS, but I need one thing first:  What should the full message text be (besides the name/phone and “on my way”)?   Example... |
