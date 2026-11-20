# Architecture

## Goal

Cover the physical screen of a running Windows session with a terminal-looking
curtain, and require a secret to remove it. The session must stay unlocked so
background servers, agents, and remote work keep running.

## Why an overlay and not something "real"

Windows has three levels of logon protection:

| Level | Mechanism | Bypass |
|---|---|---|
| Cosmetic | topmost window | `Ctrl+Alt+Del`, Task Manager, Safe Mode, another admin |
| Gatekeeper | Credential Provider in Winlogon | rogue admin, WinRE, RDP |
| Cryptographic | BitLocker + TPM + PIN | key recovery |

A Credential Provider is the "proper" way to add a second factor to logon, but it
runs inside `LogonUI`, requires a COM DLL in `System32`, and can brick the logon
screen if it misbehaves. It also has nothing to do with RDP.

TermLock deliberately picks level 1: a normal user-mode process with a normal
window. It cannot lock the owner out, it needs no admin rights, and it does not
touch the Windows session.

## Process model

```
TermLock.exe
├── WPF Application (single instance mutex)
│   ├── MainWindow        hidden until locked, then fullscreen topmost
│   ├── SetupWindow       first run only
│   ├── HotkeyHost        hidden HWND + RegisterHotKey
│   ├── TrayIcon          NotifyIcon (WinForms interop)
│   └── Autostart         HKCU Run key
└── %APPDATA%\TermLock
    ├── config.json       settings + PBKDF2 hash
    └── logs\termlock.log
```

## Startup flow

1. Parse `--install`, `--uninstall`, `--disable`, `--lock`.
2. Acquire `Local\TermLock.SingleInstance`.
3. Load `config.json` (or defaults).
4. Create the lock window once, but keep it hidden.
5. Create the tray icon and register `Ctrl+Alt+Shift+L`.
6. If there is no passphrase hash, show the first-run dialog.
7. Show the curtain if `--lock` was passed.

The lock window is created once and reused. Creating a new WPF window for every
lock would leak timers and cause a visible flash.

## Authentication

```
passphrase (string, never stored)
   │
   ├── PBKDF2-HMAC-SHA256, 600 000 iterations
   ├── 16-byte random salt
   └── 32-byte hash
          │
          └── config.json: { passphraseHash, passphraseSalt, passphraseIterations }
```

Verification uses `CryptographicOperations.FixedTimeEquals`. A failed attempt
increments a counter; after `maxAttempts` the UI enters a cooldown. Day 8 adds a
configurable fail action (Shutdown by default) and an ntfy alert.

Why not DPAPI? Because the threat is a family member opening a JSON file, not a
forensic attacker. A hash is enough to stop a casual reader, and it keeps the
config portable between machines. Disk encryption is explicitly out of scope.

## Global hotkey

`RegisterHotKey` posts `WM_HOTKEY` to a hidden `HwndSource` on the UI thread.
`MOD_NOREPEAT` prevents auto-repeat while the keys are held. The hotkey works
regardless of which application has focus, including fullscreen games.

If the combo is already taken, `HotkeyHost.IsRegistered` is `false` and the log
records it. The tray menu remains as a fallback.

## Screen coverage

One window is positioned over the whole virtual desktop:

```
Left   = SystemParameters.VirtualScreenLeft
Top    = SystemParameters.VirtualScreenTop
Width  = SystemParameters.VirtualScreenWidth
Height = SystemParameters.VirtualScreenHeight
```

`PerMonitorV2` DPI awareness in `app.manifest` keeps coordinates sane on mixed-DPI
setups. Day 3 evaluates per-monitor windows if a single spanning window shows
scaling artifacts.

The window is `Topmost`, `WindowStyle=None`, `ShowInTaskbar=False`, and re-asserts
topmost/focus every 750 ms while visible.

## Input

A visible `TextBlock` renders `●` per character; an invisible 1×1 `PasswordBox`
owns the real keyboard input. This gives correct handling of Backspace, Unicode,
paste, and keyboard layouts without reimplementing a text editor.

`Esc` and `Ctrl+U` clear the line. `Enter` verifies. `Esc` never unlocks.

## Keyboard guard

`Platform/KeyboardHook.cs` installs a low-level `WH_KEYBOARD_LL` hook on the UI
thread. While the curtain is visible it swallows:

- `Win` (both keys) — which also kills `Win+D`, `Win+Tab`, `Win+Shift+S`;
- `Alt+Tab`;
- `Alt+F4`;
- `Ctrl+Esc` (Start menu);
- `PrintScreen`.

It never blocks `Ctrl+Alt+Del` — that is the secure attention sequence and
user-mode cannot intercept it. While unlocked, the hook only watches for a
double-tap of Caps Lock; both presses pass through, so the Caps state ends where
it started. The hook inspects virtual-key codes only; it never stores text.

## Matrix renderer

`UI/MatrixRain.cs` is a small terminal-style renderer:

1. **Glyph atlas** — digits `0-9` are pre-rendered once into a single
   `RenderTargetBitmap`; every character occupies one fixed cell.
2. **Cell grid** — `float[] _brightness` and `byte[] _glyph` hold one entry per
   terminal cell.
3. **Drops** — one drop per column (`DropsPerColumn = 1`), each with its own speed,
   tail length and vertical position.
4. **Frame** (30 FPS, driven by a `DispatcherTimer`):
   - decay every cell by `Decay`;
   - move each drop, stamp a near-white head and a green fading tail;
   - draw only active cells into a persistent BGRA frame buffer;
   - `WriteableBitmap.WritePixels` once, then invalidate the visual.
5. **Lifecycle** — the timer starts and stops with `IsVisible`, so a hidden
   curtain costs nothing.

No `TextBlock` per character, no layout pass per frame, no GC churn. See
[docs/MATRIX.md](MATRIX.md) for the walkthrough.

## System probes

`Core/SystemStats.cs` supplies the live dashboard without WMI or admin rights:

| Value | Source |
|---|---|
| CPU | `GetSystemTimes` deltas, sampled once per second |
| RAM | `GlobalMemoryStatusEx` |
| Disk | `DriveInfo` for the system drive |
| Defender | `MsMpEng` process plus registry real-time flags |
| Firewall | `EnableFirewall` in the domain/standard/public profiles |
| Secure Boot | `UEFISecureBootEnabled` under `HKLM\...\SecureBoot\State` |

The security rows are heuristics, not a full security audit: a third-party
antivirus can make Defender look off, and the registry values are only as
trustworthy as the machine. Nothing is sent over the network.

## Recovery and USB

Two independent fallbacks:

- **Recovery code** — 160 random bits encoded as Crockford base32 in eight
  groups (`XXXX-XXXX-...`). Only its SHA-256 is stored. On the lock screen,
  `:recovery CODE` verifies it; dashes, spaces and lowercase are normalized.
  The code is displayed once by `RecoveryWindow` and must be saved by the user.
- **USB key** — `UsbKey.Create` writes a random 32-byte token to
  `termlock.key` on a removable drive and stores the token hash plus the
  drive's volume serial. While locked, `MainWindow` polls removable drives
  every 1.5 s. It only unlocks on an absent → present transition; a key that is
  already inserted when the curtain appears must be removed and reinserted.

Neither path is a substitute for BitLocker: someone with the drive or the
recovery code can still unlock. They exist so the owner cannot be locked out by
a forgotten passphrase.

## Notifications and fail action

`Core/NtfyNotifier.cs` sends a fire-and-forget HTTP POST to
`{url}/{topic}`. It never blocks the lock screen, times out after 5 seconds, and
rate-limits messages per process. The topic is a secret and is never logged.

On every failed attempt the curtain sends a `warning` notification. On the third
failure it arms `FailAction` (`Shutdown`, `Sleep`, `Hibernate`, `Lock`, `None`)
with a visible countdown (`FailActionSeconds`). The correct passphrase,
recovery code or USB key cancels the countdown. `Shutdown` is executed with
`shutdown.exe /s /t 0`; `Sleep` via `rundll32 powrprof.dll,SetSuspendState`;
`Hibernate` via `shutdown.exe /h`; `Lock` via `LockWorkStation`.

## Future

- USB key: file token + volume serial, hashed in config.
- Recovery code: 128-bit Crockford base32, shown once, stored hashed.
- ntfy: fire-and-forget HTTP POST on a background queue, rate-limited.
- Watchdog: a second process that relaunches the curtain if the UI process dies.

## Non-goals

- replacing the Windows password
- encrypting the disk
- surviving an administrator, Safe Mode, or a live USB
- protecting against malware already running as the user

Those are real security boundaries and belong to BitLocker, Windows Hello, and
proper endpoint hardening, not to a WPF window.
