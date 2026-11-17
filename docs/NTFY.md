# ntfy: уведомления на телефон и часы

## Что это

[ntfy](https://ntfy.sh) (читается «нотифай») — открытый сервис push-уведомлений
поверх обычного HTTP. Никаких аккаунтов, SDK и регистраций:

1. Ты придумываешь **топик** — это просто строка, например
   `jadeonit-termlock-7f3k9q2m`.
2. Телефон подписывается на топик в приложении ntfy.
3. Любой процесс делает HTTP-запрос на `https://ntfy.sh/<топик>` — телефон
   показывает уведомление.

Всё. Топик — это и адрес, и пароль одновременно.

## Зачем TermLock

TermLock не должен зависеть от сети при разблокировке. ntfy нужен только для
**оповещений**:

- неудачная попытка ввода фразы;
- три неудачных подряд (и, по настройке, выключение/сон);
- успешный вход после нескольких неудач;
- вход по USB-ключу или recovery-коду.

Если интернета нет — уведомление просто не уйдёт, ширма продолжит работать.
Критический путь (вход) от ntfy не зависит.

## Настройка телефона

1. Установи **ntfy** из Google Play или F-Droid.
2. Нажми `+`, введи топик, например `jadeonit-termlock-7f3k9q2m`.
3. Разреши уведомления и отключи оптимизацию батареи для ntfy (Samsung любит
   усыплять фоновые приложения: Настройки → Приложения → ntfy → Батарея →
   «Без ограничений»).
4. Проверь тестовым сообщением (ниже).

### Mi Band 10

ntfy показывает **обычное Android-уведомление**. Твой модифицированный клиент
для часов зеркалит системные уведомления — отдельное приложение для TermLock не
нужно. Проверь, что:
- у клиента есть доступ к уведомлениям;
- ntfy не в списке «не зеркалить»;
- на часах включены уведомления от этого клиента.

## Тест

PowerShell:

```powershell
$topic = "jadeonit-termlock-7f3k9q2m"
Invoke-RestMethod -Uri "https://ntfy.sh/$topic" -Method Post `
  -Body "test from TermLock" `
  -Headers @{ Title = "TermLock"; Priority = "high"; Tags = "lock" }
```

curl:

```bash
curl -H "Title: TermLock" \
     -H "Priority: high" \
     -H "Tags: warning,lock" \
     -d "3 failed attempts at 23:14" \
     https://ntfy.sh/jadeonit-termlock-7f3k9q2m
```

## Параметры сообщения

Передаются HTTP-заголовками (или query-параметрами):

| Заголовок | Значение |
|---|---|
| `Title` | заголовок уведомления |
| `Priority` | `min`, `low`, `default`, `high`, `urgent` (или 1–5) |
| `Tags` | emoji-шорткоды: `warning`, `lock`, `skull`, `white_check_mark` |
| `Click` | URL, который откроется по тапу |
| `Attach` | URL вложения |
| `Actions` | кнопки: `view`, `broadcast`, `http` |
| `Delay` | отложенная отправка (10 секунд – 3 дня) |

Пример с кнопкой:

```powershell
Invoke-RestMethod -Uri "https://ntfy.sh/$topic" -Method Post `
  -Body "Unlock attempt failed 3 times" `
  -Headers @{
    Title = "TermLock"
    Priority = "urgent"
    Tags = "warning"
    Actions = "view, Open log, https://example.com/log"
  }
```

## Лимиты публичного ntfy.sh

Из официальной документации:

| Лимит | Значение |
|---|---|
| Длина сообщения | 4 096 байт (длиннее — уходит как вложение) |
| Запросы | 60 сразу, потом 1 запрос в 5 секунд |
| Сообщений в день | 250 на ntfy.sh |
| Подписок | 30 одновременных соединений |
| Вложения | живут 3 часа |
| E-mail | на ntfy.sh отключён для анонимов |

Для TermLock это с огромным запасом: несколько событий в день.

## Безопасность

Важно понимать:

- **Топик — это секрет.** Кто знает топик, тот может и читать, и отправлять.
  Поэтому топик должен быть длинным и случайным, а не `termlock`.
- Публичный `ntfy.sh` **не даёт end-to-end шифрования**. Сообщения кэшируются
  на сервере (по умолчанию 12 часов) и доступны всем, кто знает топик.
- Не отправляй в уведомлениях имена файлов, пароли, тексты документов.
  Достаточно: «3 failed attempts at 23:14».
- Топик не коммить в репозиторий. В TermLock он будет лежать только в
  `%APPDATA%\TermLock\config.json`.
- Если хочется приватности — подними свой ntfy по HTTPS и закрой доступ
  токеном.

## Свой сервер (опционально)

Docker:

```bash
docker run -p 80:80 \
  -v /var/cache/ntfy:/var/cache/ntfy \
  binwiederhier/ntfy serve
```

Минимальная защита (`server.yml`):

```yaml
base-url: "https://ntfy.example.com"
auth-file: "/var/cache/ntfy/user.db"
auth-default-access: "deny-all"
```

Пользователи и права:

```bash
ntfy user add --role=admin jadeonit
ntfy access jadeonit jadeonit-termlock read-write
```

Дальше — reverse proxy с TLS (Caddy, nginx) и, при желании, access tokens.

## Как это будет в TermLock (Day 8)

Конфиг:

```json
{
  "notifications": {
    "provider": "ntfy",
    "url": "https://ntfy.sh",
    "topic": "REPLACE_WITH_RANDOM_TOPIC",
    "minIntervalSeconds": 10
  }
}
```

Поведение:

- отправка в фоне, с таймаутом 5 секунд;
- не блокирует ввод и разблокировку;
- не чаще одного сообщения в `minIntervalSeconds`, чтобы спамер не завалил часы;
- при ошибке сети — запись в лог и тишина;
- события: `failed`, `locked_out`, `unlocked`, `usb`, `recovery`.

## Альтернативы

| Сервис | Плюсы | Минусы |
|---|---|---|
| **ntfy** | без аккаунта, свой сервер, кнопки | топик = секрет, публичный сервер без E2E |
| Telegram-бот | привычно, богатые сообщения | нужен бот и chat_id, зависимость от аккаунта |
| Gotify | простой self-host | только self-host, меньше клиентов |
| Pushover | надёжный, красивый | платный |

Для TermLock выбран ntfy: один HTTP-запрос, ноль регистраций, легко заменить
на Telegram позже (интерфейс `INotifier`).
