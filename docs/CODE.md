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
- `DeviceType` — `PC`, `Laptop` или версия системы;
- `DisplayName` — готовая строка вида `Jadeonit @ Windows 10 22H2`.

`SystemInfo.WindowsLabel()` читает
`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`:

- `ProductName` — убираем редакцию (`Pro`, `Home`, `Enterprise`, ...);
- `CurrentBuildNumber` ≥ 22000 — значит, это Windows 11, даже если
  `ProductName` всё ещё пишет `Windows 10`;
- `DisplayVersion` — `22H2`, `23H2`, `24H2`.

`AppConfig.DisplayNameWithDevice` склеивает `"{UserName} @ {DeviceType}"`.
Поэтому в шапке и в строке `user` появляется `Jadeonit @ Windows 10 22H2`.
Если в настройке выбрать `PC` или `Laptop`, будет `Jadeonit @ PC` или
`Jadeonit @ Laptop`.

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
