# Roadmap

One small commit per day beats one big commit per month. Each day is a vertical
slice that builds, runs, and can be tested on the real machine.

| Day | Slice | Commit |
|---|---|---|
| 1 | Repo scaffold, WPF window, hotkey, passphrase, tray, autostart | `chore: scaffold termlock` |
| 2 | Animated matrix rain (glyph atlas + `WriteableBitmap`) | `feat(render): matrix rain` |
| 3 | Terminal layout polish, multi-monitor, DPI, cursor/status line | `feat(ui): lock screen layout` |
| 4 | Auth hardening: attempt policy, cooldown timer, config migration | `feat(auth): passphrase policy` |
| 5 | Keyboard guard: block `Win`/`Alt+Tab`/`Alt+F4`/`Ctrl+Esc`/`PrintScreen`, double Caps | `feat(guard): keyboard hook + double Caps` |
| 6 | Real stats: CPU, RAM, disk, Defender, firewall, Secure Boot | `feat(stats): system probes` |
| 7 | USB key unlock, recovery code, first-run wizard | `feat(auth): usb key + recovery` |
| 8 | ntfy alerts, configurable fail action (Shutdown by default) | `feat(notify): alerts + fail action` || 9 | Installer scripts, GitHub Actions, screenshots, GIF | `chore: packaging + ci` |
| 10 | v0.1 release: docs pass, code review, tag | `chore: release v0.1` |

## Backlog

- [ ] guest passphrase → harmless "Study Mode" decoy screen
- [ ] watchdog process (relaunch the curtain if the UI process dies)
- [ ] optional intruder log (webcam photo, local only)
- [ ] themes: terminal / BIOS / Windows Update
- [ ] mechanical keyboard sounds (opt-in)
- [ ] tray balloon with the last auth event
- [ ] `SetWindowDisplayAffinity` to hide the curtain from screen capture (opt-in)
- [ ] auto-lock on idle (opt-in, off by default because of remote work)

## Done

- [x] Day 1 — scaffold, curtain window, hotkey, passphrase, tray, autostart
- [x] Day 2 — animated digit rain (glyph atlas + `WriteableBitmap`, 30 FPS)
- [x] Day 5 — keyboard guard + double Caps
- [x] Day 6 — live CPU/RAM/disk/Defender/firewall/Secure Boot
- [x] Day 7 — USB key + recovery code
- [x] Day 8 — ntfy alerts + fail action
