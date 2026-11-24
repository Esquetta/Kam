# Agent task results

- date: 2026-11-24
- provider: OpenAI (https://api.openai.com/v1)
- tasks: 16 (8 English, 8 Turkish), from `evals/agent-tasks/tasks.json`
- the real agent loop and system prompt; every built-in skill is offered, but only file tools with paths inside the task's workspace may run
- one attempt per task; a turn may take 4 minutes

| Model | Tasks | Passed | EN | TR | Avg tool calls | Failed calls | Blocked calls | Median s | Avg input tok | Avg output tok |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `gpt-5-mini` | 16 | 94% | 100% | 88% | 3.9 | 0 | 1 | 12.4 | 14879 | 698 |
| `gpt-5.4-mini` | 16 | 100% | 100% | 100% | 2.8 | 0 | 1 | 3.4 | 10549 | 258 |
| `gpt-5.4-nano` | 16 | 81% | 75% | 88% | 5.1 | 2 | 1 | 5.7 | 15433 | 448 |
| `gpt-5.5` | 16 | 94% | 100% | 88% | 4.6 | 2 | 3 | 9.0 | 14687 | 469 |

## Failed tasks

### `gpt-5-mini`

| Task | Failed checks | Tools | Reply or error |
| --- | --- | --- | --- |
| sales-total.tr | total.txt Contains "1250" | workspace_tree, files_read | Dosyayı okuyamadım — erişim izinleri engellendi. Bu yüzden toplamı hesaplayıp total.txt yazamadım.  Yapabileceğiniz seçenekler (kısa): - Bu aracı dosyaya erişim izni verebilirsiniz, sonra ben devam ed... |

### `gpt-5.4-nano`

| Task | Failed checks | Tools | Reply or error |
| --- | --- | --- | --- |
| move-images.en | Exists inbox/images/photo1.jpg; Exists inbox/images/photo2.png; Missing inbox/photo1.jpg; Missing inbox/photo2.png | files_list, directories_create, shell_run, files_tree, files_info, files_info, files_list | İstediğiniz gibi `inbox/images` klasörünü oluşturdum.    Ancak dosyaları `inbox` klasöründen `inbox/images` klasörüne **taşıma işlemi** bu ortamda engellendi (PowerShell `Move-Item` komutu “kullanıcı ... |
| sales-total.en | total.txt Contains "1250" | files_exists, files_read, files_write | Toplam **amount** sütunu değeri **1275**.   `total.txt` dosyasına yazdım. |
| sales-total.tr | total.txt Contains "1250" | files_exists, files_exists, files_read, files_write | `sales.csv` dosyasındaki `amount` sütununun toplamı **1255**.  Bu toplam, `total.txt` dosyasına **`total=1255`** formatında yazıldı. |

### `gpt-5.5`

| Task | Failed checks | Tools | Reply or error |
| --- | --- | --- | --- |
| rename-txt.tr | drafts/a.md Contains "alpha"; Missing drafts/a.txt | files_tree, files_search, files_move, files_move, shell_run, files_tree | Kısmen tamamlandı: `drafts` klasöründe `b.txt` → `b.md` olarak değiştirildi.  Değiştirilemeyen: - `a.txt` → `a.md` yapılamadı; dosya başka bir işlem tarafından kullanılıyor. - Alternatif komut da izin... |
