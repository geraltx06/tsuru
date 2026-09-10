# Tsuru

Smooth mouse-wheel scrolling for Windows, in a tray app. A clone of
[smoothscroll.net](https://www.smoothscroll.net/win/): it intercepts each wheel
notch and replays it as a stream of small steps along an easing curve, so
scrolling glides instead of jumping line by line.

Works system-wide — Explorer, browsers, editors, Office, anything that takes a
normal wheel event.

```
dist\Tsuru-1.0.0-setup.exe    installer, ~2 MB
build\Tsuru.exe               the app itself, ~65 KB, no runtime to download
```

## Installing

Run `dist\Tsuru-1.0.0-setup.exe`. It installs **per user** into
`%LOCALAPPDATA%\Programs\Tsuru`, so there is no administrator prompt — which
suits a tray utility whose settings and startup entry are per-user anyway, and
avoids stacking a UAC prompt on top of the SmartScreen warning that any
unsigned download already gets.

The installer offers a desktop shortcut and *Start Tsuru when I sign in*; the
latter writes the same registry value the app's own checkbox manages, so the
two never disagree. Uninstalling removes the program, both shortcuts and the
startup entry, and offers to delete your settings — a silent uninstall keeps
them, since deleting someone's data unattended is the worse mistake.

A running copy is asked to quit through a named event before files are touched,
so it removes its own tray icon rather than leaving the phantom that killing a
tray app produces.

Build the installer with:

```powershell
powershell -ExecutionPolicy Bypass -File make-installer.ps1
```

That needs [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install --id JRSoftware.InnoSetup`). Pass `-SkipBuild` to package the
existing `build\` output.

### Before releasing publicly

The executable installs a global mouse hook, and an **unsigned** one will be
flagged hard by SmartScreen and some antivirus. Signing it with an OV or EV
code-signing certificate is the difference between "Windows protected your PC"
and a clean install. Worth sorting before the link goes anywhere.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Add `-Run` to launch it afterwards, or `-Symbols` for a debug build.

The build uses the C# compiler bundled with the .NET Framework
(`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), so **nothing needs
installing** — no SDK, no NuGet. The result is a single self-contained
executable that runs on any Windows 7+ machine, since .NET Framework 4.x ships
with Windows.

## Using it

Run `build\Tsuru.exe`. It has no main window — it lives in the
notification area. On Windows 11 new tray icons start hidden behind the `^`
chevron; drag it onto the taskbar via *Taskbar settings → Other system tray
icons* if you want it always visible.

| Action | Result |
| --- | --- |
| Double-click the tray icon | Open Settings |
| Right-click the tray icon | Menu: enable/pause, Settings, Start with Windows, Welcome screen, About, Exit |
| Run the .exe again | Brings up Settings on the instance already running |

### First run

The first launch opens a welcome screen with a **Start** button. Smoothing
stays paused until it is pressed — Start switches it on and takes you straight
to Settings.

Onboarding is tracked by `OnboardingComplete` in the config, set the first time
smoothing is switched on by any route. So dismissing the welcome screen without
pressing Start leaves the app paused and offers onboarding again next launch,
rather than sitting silent forever. Once complete it does not reappear; reopen
it any time from **Welcome screen** in the tray menu.

The title is set in **Monograph**. That font is unlikely to exist on someone
else's machine, so `FontLoader` resolves it in order: a `.ttf`/`.otf` in a
`fonts\` folder beside the executable, then the installed system family, then
Segoe UI Light. Drop the font file into `build\fonts\` to ship it — subject to
its redistribution licence.

The icon shows a mouse; struck through in red when smoothing is paused. It
redraws itself when you switch Windows between light and dark.

Settings apply **live** and the window is non-modal, so leave it open and
scroll something else to feel each change as you make it.

## Settings

Stored at `%APPDATA%\Tsuru\settings.ini` — plain `key=value`, safe to
hand-edit while the app is closed.

### Master switch

The **On/Off** switch at the top of the settings panel turns all smoothing off
in one click; the wheel reverts to normal Windows scrolling and nothing is
intercepted. It mirrors *Smooth scrolling* in the tray menu — flip either and
the other follows, along with the tray icon. Everything below it stays editable
while off, so you can tune first and switch back on when it feels right.

### Feel

| Setting | Default | What it does |
| --- | --- | --- |
| Step size | 120 | Distance one notch travels, in wheel units. 120 is exactly what Windows scrolls natively — raise it to cover more ground per notch. |
| Animation time | 400 ms | How long one notch takes to play out. Higher is floatier, lower is snappier. |
| Smoothness | 8 | Shape of the easing curve. Higher front-loads the motion and settles more sharply. |
| Use pulse easing | on | Off gives plain linear motion. |

### Acceleration

| Setting | Default | What it does |
| --- | --- | --- |
| Acceleration window | 50 ms | Notches arriving closer together than this compound, so a fast flick travels further. 0 disables it. |
| Maximum boost | 3.0x | Ceiling on that multiplier. |

### Axes

Vertical and horizontal smoothing toggle independently, and each direction can
be inverted.

### Compatibility

| Setting | Default | What it does |
| --- | --- | --- |
| Update rate | 120 fps | How often motion is delivered. Match your monitor's refresh rate. |
| Leave precision touchpads alone | on | Touchpads report fine-grained deltas and already scroll smoothly; re-animating them only adds lag. Detected by deltas that are not multiples of 120. |
| Ignore scrolling from other apps | on | Stops Tsuru fighting other automation tools that synthesise scroll events. |
| Excluded apps | empty | One executable name per line (`game.exe`). Those keep native scrolling. |

Exclusions match the window **under the pointer**, so per-app behaviour follows
where you are actually scrolling.

## How it works

Two threads, deliberately kept apart:

- **Hook thread** ([`ScrollEngine.HookLoop`](src/ScrollEngine.cs)) owns a
  `WH_MOUSE_LL` hook with its own message pump. It runs at highest priority and
  does almost nothing, because a low-level hook that returns slowly gets evicted
  by Windows. Each real notch is swallowed and queued as a *pulse*.
- **Animation thread** samples every in-flight pulse against the easing curve,
  sums what they owe this frame, and injects the difference with `SendInput`.
  It sleeps on an event when nothing is in flight, so an idle Tsuru costs
  nothing.

Injected events carry a signature in `dwExtraInfo`, which the hook checks first
so it never re-smooths its own output. Wheel events are integral, so fractional
remainders carry to the next frame — total travel comes out exact rather than
drifting.

The easing is the smoothscroll pulse curve: a quick exponential ramp decaying
into a long tail, normalised so a notch lands precisely on its target.

## Notes and limits

- **Administrator windows.** Windows will not let a normal-integrity process
  hook input destined for an elevated one. To smooth scrolling inside apps
  running as administrator, run Tsuru as administrator too.
- **Games.** Anything reading raw input or running exclusive fullscreen should
  go in the exclusion list.
- **Old apps.** A few very old controls only act on whole 120-unit deltas and
  will look steppy. Exclude them.
- Errors, if any, land in `%APPDATA%\Tsuru\log.txt`. It stays empty
  during normal operation.

## Uninstall

Exit from the tray menu, untick *Start with Windows* first (or delete the
`Tsuru` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), then delete the folder
and `%APPDATA%\Tsuru`. Nothing else is touched.
