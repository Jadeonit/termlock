# Разбор кода TermLock (Day 1)

Это учебные заметки: что делает каждая часть и какие технологии под капотом.
Читай сверху вниз, файлы идут примерно в порядке выполнения.

## Карта проекта

```
src/TermLock/
├── App.xaml / App.xaml.cs          точка входа, жизненный цикл
├── Core/
│   ├── AppPaths.cs                 пути в %APPDATA%
│   ├── Log.cs                      простой лог
│   ├── AppConfig.cs                JSON-конфиг
│   └── Passphrase.cs               PBKDF2
├── Platform/
│   ├── HotkeyHost.cs               глобальный хоткей
│   ├── Autostart.cs                HKCU Run
│   └── TrayIcon.cs                 иконка в трее
└── UI/
    ├── MainWindow.xaml(.cs)        ширма
    └── SetupWindow.xaml(.cs)       первый запуск
```

## 1. Точка входа: `App.OnStartup`

WPF-приложение стартует с `App.xaml`. Мы убрали `StartupUri`, потому что окно
не должно показываться само: приложение живёт в трее.

```csharp
ShutdownMode = ShutdownMode.OnExplicitShutdown;
```

По умолчанию WPF закрывается, когда закрывается последнее окно. Нам это не
подходит: ширма скрывается (`Hide()`), а процесс должен жить. Поэтому
выключение — только явное (`Shutdown()`).

### Аргументы командной строки

```csharp
var args = e.Args.Select(a => a.Trim().ToLowerInvariant()).ToHashSet();
```

`--install`, `--uninstall`, `--disable`, `--lock`. `ToHashSet` нужен, чтобы
проверка была O(1) и не зависела от порядка.

### Один экземпляр

```csharp
_mutex = new Mutex(true, @"Local\TermLock.SingleInstance", out var isNew);
if (!isNew) { Shutdown(); return; }
```

`Local\` — мьютекс в пределах текущей сессии Windows (не глобальный).
Если TermLock уже запущен, второй экземпляр молча выходит. Это важно, иначе
на экране окажутся две ширмы, два хука и два трея.

Мьютекс надо освобождать в `OnExit`; мы это делаем в `try/catch`, потому что
`ReleaseMutex` кидает исключение, если поток не владеет им.

## 2. Глобальный хоткей: `HotkeyHost`

Windows умеет сама сообщать о нажатии комбинации, даже если фокус в другом
приложении. Для этого есть `RegisterHotKey`.

```csharp
RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_L);
```

Нужен HWND, которому придёт сообщение `WM_HOTKEY`. Мы создаём невидимое окно
через `HwndSource`:

```csharp
var source = new HwndSource(new HwndSourceParameters("TermLock.HotkeySink") { ... });
source.AddHook(WndProc);
```

`HwndSource` — это мост между Win32-окном и WPF. `AddHook` позволяет
обрабатывать сырые сообщения. В `WndProc` ловим `WM_HOTKEY` и поднимаем
событие `Pressed`.

`MOD_NOREPEAT` (0x4000) не даёт сообщению спамиться, пока клавиши зажаты.

## 3. Автозапуск: `Autostart`

```csharp
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

Значение `TermLock` = `"C:\path\TermLock.exe" --autostart`.

Почему HKCU, а не служба:
- не нужны права администратора;
- пользователь может удалить запись через Диспетчер задач → Автозагрузка;
- служба — это лишний привилегированный процесс, который нам не нужен.

`Environment.ProcessPath` возвращает путь к запущенному exe. У `dotnet run`
это будет `dotnet.exe`, поэтому автозапуск надо ставить из опубликованного
exe, а не из-под SDK.

## 4. Пароль: `Passphrase`

```csharp
var salt = RandomNumberGenerator.GetBytes(16);
var hash = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 600_000, HashAlgorithmName.SHA256, 32);
```

PBKDF2 — это «медленная» функция: она намеренно тратит время, чтобы перебор
был дорогим. Соль уникальна для каждого пароля и хранится рядом с хэшем. Она
не секретна; её задача — чтобы одинаковые пароли давали разные хэши и нельзя
было использовать заранее посчитанные таблицы.

Проверка:

```csharp
CryptographicOperations.FixedTimeEquals(expected, actual);
```

Обычное `==` сравнивает байты до первого несовпадения и теоретически сливает
информацию по времени. Для ширмы это паранойя, но привычка правильная.

600 000 итераций — рекомендация OWASP для PBKDF2-HMAC-SHA256. На ноутбуке это
~100–200 мс: человек не заметит, перебор — заметит.

## 5. Ширма: `MainWindow`

### Почему окно, а не `LockWorkStation`

`Win+L` блокирует сессию Windows. Тогда:
- удалённые процессы продолжают работать, но GUI-сессия недоступна;
- на экране появляется стандартный экран входа, а не наш терминал;
- понадобился бы пароль Windows, который мы не хотим трогать.

Оверлей оставляет сессию живой. Серверы, агенты, RDP и фоновые задачи
продолжают работать, а физический экран закрыт.

### Геометрия

```csharp
Left = SystemParameters.VirtualScreenLeft;
Top = SystemParameters.VirtualScreenTop;
Width = SystemParameters.VirtualScreenWidth;
Height = SystemParameters.VirtualScreenHeight;
```

Это покрывает все мониторы одним окном. `WindowStyle=None` убирает рамку,
`Topmost=True` держит поверх остальных окон. Таймер раз в 750 мс заново
поднимает окно и возвращает фокус.

### Ввод

Видимая строка — это `TextBlock` с `●`. Настоящий ввод принимает невидимый
`PasswordBox` размером 1×1:

```xml
<PasswordBox x:Name="HiddenInput" Width="1" Height="1" Opacity="0" />
```

Почему так: `PasswordBox` уже умеет Backspace, вставку, Unicode и разные
раскладки. Если писать свой обработчик `TextInput`, придётся повторять всё это
руками и ловить странные баги с IME.

События:

```csharp
PreviewKeyDown += OnPreviewKeyDown;
HiddenInput.PasswordChanged += (_, _) => UpdatePrompt();
```

`Esc` → `Clear()`, `Enter` → `TryUnlock()`. `Esc` никогда не разблокирует.

### Таймеры

- `_clock` — раз в секунду обновляет шапку (uptime, время).
- `_cursorBlink` — мигает курсором `_` каждые 530 мс.
- `_keepOnTop` — раз в 750 мс поднимает окно и возвращает фокус.

Все три — `DispatcherTimer`, то есть работают в UI-потоке. Для тяжёлых задач
(рендер матрицы, HTTP) понадобятся отдельные потоки; об этом на Day 2 и Day 8.

### Закрытие

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    if (!_allowClose) e.Cancel = true;
    base.OnClosing(e);
}
```

`Alt+F4` не должен открывать ширму. Закрыть окно можно только через
`ForceClose()` (при выходе из приложения) или убив процесс.

## 6. Трей: `TrayIcon`

WPF не имеет своей иконки в трее, поэтому используем WinForms-ный `NotifyIcon`.
В проекте включены оба фреймворка:

```xml
<UseWPF>true</UseWPF>
<UseWindowsForms>true</UseWindowsForms>
```

Из-за этого появляются неоднозначности вроде `KeyEventArgs` (есть и в WPF, и в
WinForms). Мы решаем их алиасом:

```csharp
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
```

## 7. Как запускать и отлаживать

```powershell
# собрать
dotnet build TermLock.sln -c Release

# запустить и сразу показать ширму
dotnet run --project src/TermLock -- --lock

# снять окно ширмы или профиля в PNG (dev)
TermLock.exe --shot
TermLock.exe --setup-shot

# посмотреть лог
Get-Content "$env:APPDATA\TermLock\logs\termlock.log" -Tail 20
```

Если ширма перекрыла всё, а ты в отладке: `Ctrl+Alt+Del` → Диспетчер задач →
снять `TermLock`. Это нормальный сценарий, сессия не блокируется.

## 8. Упражнения

1. Поменяй хоткей в `HotkeyHost` на `Ctrl+Alt+Shift+K` и проверь, что старый
   перестал работать.
2. Добавь в `TryUnlock` вывод количества оставшихся попыток.
3. Сделай, чтобы `Esc` дважды подряд показывал подсказку `hint: 3 attempts,
   then 10s cooldown`, но всё ещё не разблокировал.
4. Прочитай `docs/ARCHITECTURE.md` и объясни, почему `Credential Provider`
   не подходит для этой задачи.

## 9. Day 2: матрица

Сделано в `UI/MatrixRain.cs`:

- атлас цифр `0-9` рисуется один раз в `RenderTargetBitmap`;
- сетка `float[] _brightness` + `byte[] _glyph` хранит состояние клеток;
- одна капля на колонку со своей скоростью и хвостом;
- каждые 33 мс буфер `Bgra32` перерисовывается и уходит в `WriteableBitmap`;
- таймер останавливается, когда ширма скрыта.

Полный разбор с картинками и упражнениями — в [MATRIX.md](MATRIX.md).

Главная идея прежняя: не создавать тысячи `TextBlock`, а рисовать пиксели в
буфер, как настоящий терминальный эмулятор.

## 10. Что дальше (Day 3)

Раскладка: мультимонитор, DPI, аккуратные отступы, курсор и строка статуса.
Рендерер уже есть; теперь надо сделать так, чтобы он хорошо смотрелся на любом
экране.

## 11. Профиль и версия Windows

`SetupWindow` собирает:

- `UserName` — имя на ширме;
- `DeviceType` — `PC` или `Laptop`;
- `DisplayName` — готовая строка для конфига, например
  `Jadeonit @ Laptop @ Windows 10 Pro 22H2`.

`SystemInfo.WindowsLabel()` читает
`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`:

- `ProductName` — полное имя с редакцией (`Windows 10 Pro`, `Windows 11 Home`);
- `CurrentBuildNumber` ≥ 22000 — значит, это Windows 11, даже если
  `ProductName` всё ещё пишет `Windows 10`;
- `DisplayVersion` — `22H2`, `23H2`, `24H2`;
- пара `(product, version)` кэшируется в `Lazy<...>`: версия ОС не меняется
  на ходу.

`AppConfig.DisplayNameFull` склеивает
`"{UserName} @ {DeviceType} @ {SystemInfo.WindowsLabel()}"` и используется
только в шапке, например `Jadeonit @ Laptop @ Windows 10 Pro 22H2`.

В блоке `SYSTEM` дублирования нет:

- `user` — просто имя учётной записи (`UserName`);
- `os` — только продукт, `SystemInfo.WindowsProduct()` → `Windows 10 Pro`;
- `uptime` — только здесь, в шапке его больше нет.

## 12. Раскладка, Caps и кириллица

Рядом с промптом висит бейдж `[EN]` / `[RU]`:

- `InputLanguageManager.Current.CurrentInputLanguage` даёт текущий язык ввода;
- `CultureInfo.TwoLetterISOLanguageName` превращается в `EN` / `RU`;
- событие `InputLanguageChanged` обновляет бейдж при `Alt+Shift`;
- `Keyboard.IsKeyToggled(Key.CapsLock)` добавляет `CAPS`;
- если в `PasswordBox.Password` есть символы `\u0400..\u04FF`, добавляется
  `CYR` — значит, фраза набрана кириллицей.

`CAPS` и `CYR` подсвечиваются жёлтым, но ничего не блокируют: это подсказка,
а не запрет. Проверка фразы всё равно идёт по хэшу.

## 13. Клавиатурный щит

`Platform/KeyboardHook.cs` ставит низкоуровневый хук `WH_KEYBOARD_LL`:

```csharp
SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
```

Пока ширма видна, колбэк возвращает `1` для:

- `VK_LWIN` / `VK_RWIN` — заодно отваливаются `Win+D`, `Win+Tab`, `Win+Shift+S`;
- `VK_TAB` при зажатом Alt — `Alt+Tab`;
- `VK_F4` при зажатом Alt — `Alt+F4`;
- `VK_ESCAPE` при зажатом Ctrl — `Ctrl+Esc`;
- `VK_SNAPSHOT` — PrintScreen.

`Ctrl+Alt+Del` не блокируется: это secure attention sequence, user-mode до неё
не достаёт.

Двойной Caps:

- хук запоминает время первого `VK_CAPITAL`;
- второй в течение 400 мс вызывает `ShowLock()`;
- `_capsHeld` не даёт автоповтору зажатой клавиши сработать как двойной тап;
- оба нажатия проходят дальше, поэтому состояние Caps Lock в итоге не меняется.

Хук смотрит только коды клавиш. Текст пароля он не читает и никуда не пишет.

## 14. Реальные статы

`Core/SystemStats.cs` собирает данные без WMI, без админа и без сети:

- **CPU** — `GetSystemTimes`: берём дельты `idle`, `kernel`, `user` между
  вызовами; `kernel` уже включает `idle`, поэтому занятость считается как
  `1 - idle / (kernel + user)`.
- **RAM** — `GlobalMemoryStatusEx`: `dwMemoryLoad` даёт процент, а
  `ullTotalPhys` / `ullAvailPhys` — гигабайты.
- **Диск** — `DriveInfo` для системного диска: `TotalSize`,
  `AvailableFreeSpace`.
- **Defender** — процесс `MsMpEng` плюс ветки реестра
  `DisableRealtimeMonitoring` и `DisableAntiSpyware`.
- **Firewall** — `EnableFirewall` в профилях Domain/Standard/Public.
- **Secure Boot** — `UEFISecureBootEnabled` в
  `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State`.

`SystemStats.Bar()` рисует полоску из `█` и `░`, например
`[██████░░░░]`. `MainWindow` обновляет статы раз в секунду, пока ширма видна,
а `RenderToFile()` делает `Refresh()` перед скриншотом.

Проверки безопасности — эвристики: сторонний антивирус может выключить Defender,
и тогда строка покажет `OFF`. Это не аудит, а быстрый индикатор.

## 15. Recovery-код и USB-ключ

Recovery-код (`Core/RecoveryCode.cs`):

- 20 случайных байт = 160 бит;
- Crockford base32 → 32 символа, группами по 4: `XXXX-XXXX-...`;
- `Normalize` убирает дефисы/пробелы, приводит к верхнему регистру и
  исправляет `I/L → 1`, `O → 0`;
- в конфиге лежит только `SHA256(normalized)`;
- `MainWindow.TryUnlock` ловит префикс `:recovery`, проверяет код через
  `CryptographicOperations.FixedTimeEquals` и вызывает `Unlock("recovery")`.

USB-ключ (`Core/UsbKey.cs`):

- `Create(root)` пишет `termlock.key` с 32-байтным токеном;
- в конфиг попадают `SHA256(token)` и серийный номер тома
  (`GetVolumeInformation`);
- `IsPresent` перебирает съёмные диски, проверяет заголовок, хэш и серийник;
- `MainWindow._usbPoll` раз в 1.5 с смотрит наличие ключа;
- срабатывает только переход «нет → есть». Если флешка уже вставлена в момент
  блокировки, её нужно вынуть и вставить заново.

Проверка алгоритма recovery-кода отдельным консольным тестом: генерация,
lowercase, вариант без дефисов — `True`; случайный код — `False`.

## 16. ntfy и действие после трёх неудач

`Core/NtfyNotifier.cs`:

- статический `HttpClient` с таймаутом 5 секунд;
- POST на `{NtfyUrl}/{Uri.EscapeDataString(NtfyTopic)}`;
- заголовки `Title`, `Priority`, `Tags`;
- если `NtfyEnabled` выключен или топик пуст — молчит;
- ограничение частоты по `NtfyMinIntervalSeconds` (по умолчанию 10);
- ошибки пишутся в лог без топика; исключения не всплывают в UI.

`MainWindow`:

- каждый `Fail` шлёт `warning`; на третьем — `urgent` с `force: true`;
- `BeginFailAction()` читает `FailAction` (`Shutdown`/`Sleep`/`Hibernate`/
  `Lock`/`None`) и `FailActionSeconds`, запускает `_failTimer`;
- `TickFailAction()` обновляет строку статуса и по нулю вызывает
  `ExecuteFailAction()`;
- `ExecuteFailAction()` запускает `shutdown.exe /s /t 0`, `shutdown.exe /h`,
  `rundll32 powrprof.dll,SetSuspendState` или `LockWorkStation`;
- `Unlock()` останавливает `_failTimer` и сбрасывает `_failAt`, поэтому
  правильная фраза/recovery/USB отменяет отсчёт;
- `Fail()` при активном отсчёте не увеличивает счётчик и не перезапускает
  таймер — просто показывает, сколько осталось.

`Sleep`/`Hibernate` могут не сработать, если в системе нет соответствующих
режимов (у ноута с Modern Standby их может не быть). Тогда лучше `Shutdown`
или `None`.
