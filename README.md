# Tsuru

Smooth mouse-wheel scrolling for Windows, in a tray app. It intercepts each
wheel notch and replays it as a stream of small steps along an easing curve, so
scrolling glides instead of jumping line by line. Works anywhere that takes a
normal wheel event.

## Download

### **[Download Tsuru for Windows](https://github.com/geraltx06/tsuru/releases/latest)**

That page has one file — `Tsuru-1.1.0-setup.exe`, about 4.6 MB. Download it,
double-click it, done. There is nothing to unzip and nothing else to install: no
runtime, no SDK. (Ignore the green *Code → Download ZIP* button at the top of
this page — that gives you the source code, not the app.)

Windows will show a blue **"Windows protected your PC"** box, because the app is
unsigned. Click **More info**, then **Run anyway**. A code-signing certificate is
what clears that, and im too broke for that shit

It installs **per user** into `%LOCALAPPDATA%\Programs\Tsuru` with no
administrator prompt, and offers a desktop shortcut and *Start Tsuru when I sign
in*.

Uninstall from Add/Remove Programs. It removes the program, both shortcuts and
the startup entry, and offers to delete your settings.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1        # -Run to launch, -Symbols to debug
powershell -ExecutionPolicy Bypass -File make-installer.ps1
```

Uses the C# compiler bundled with the .NET Framework, so **nothing needs
installing** — no SDK, no NuGet. The installer needs
[Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Using it

Tsuru has no main window — it lives in the notification area. On Windows 11 new
tray icons start hidden behind the `^` chevron.

| Action | Result |
| --- | --- |
| Double-click the tray icon | Open Settings |
| Right-click the tray icon | Enable/pause, Settings, Start with Windows, Welcome screen, About, Exit |
| Run the .exe again | Brings up Settings on the running instance |

The first launch opens a welcome screen; smoothing stays paused until you press
**Start**. Settings apply **live** and the window is non-modal, so leave it open
and scroll something else to feel each change.

## Settings

Stored at `%APPDATA%\Tsuru\settings.ini` — plain `key=value`, safe to hand-edit
while the app is closed. **Reset to default** restores the sliders only, leaving
the master switch, startup entry and exclusions alone.

| Group | Setting | Default | What it does |
| --- | --- | --- | --- |
| — | Tsuru effect | on | Master switch. Off reverts the wheel to normal Windows scrolling; mirrors the tray menu. |
| Feel | Step size | 120 | Distance one notch travels. 120 is what Windows scrolls natively. |
| Feel | Animation time | 400 ms | How long one notch takes. Higher is floatier. |
| Feel | Smoothness | 8 | Shape of the easing curve. Higher settles more sharply. |
| Feel | Use pulse easing | on | Off gives plain linear motion. |
| Acceleration | Acceleration window | 50 ms | Notches closer together than this compound, so a fast flick goes further. 0 disables. |
| Acceleration | Maximum boost | 3.0x | Ceiling on that multiplier. |
| Axes | Vertical / horizontal | on | Each axis smooths independently, and each can be inverted. |
| Compatibility | Update rate | 120 fps | Match your monitor's refresh rate. |
| Compatibility | Leave precision touchpads alone | on | They already scroll smoothly; re-animating adds lag. |
| Compatibility | Ignore scrolling from other apps | on | Stops Tsuru fighting other tools that synthesise scroll events. |
| Compatibility | Excluded apps | empty | One executable name per line (`game.exe`). Matched against the window **under the pointer**. |

Sliders take a value from the pointer continuously, and respond to the keyboard
once focused: arrows step by one, PageUp/PageDown by a twentieth of the range,
Home/End jump to the limits. The wheel scrolls the window rather than editing
whichever slider it passes over.

## How it works

Two threads, deliberately kept apart. The **hook thread** owns a `WH_MOUSE_LL`
hook with its own message pump at highest priority, and does almost nothing —
a low-level hook that returns slowly gets evicted by Windows. It swallows each
real notch and queues it as a *pulse*. The **animation thread** samples every
in-flight pulse against the easing curve and injects the difference with
`SendInput`, sleeping on an event when nothing is in flight, so an idle Tsuru
costs nothing.

Injected events carry a signature in `dwExtraInfo` so the hook never re-smooths
its own output, and fractional remainders carry between frames so total travel
stays exact.

## Artwork

The welcome screen loads optional artwork from `assets\` beside the executable —
`title.png` for the wordmark and `collage\*.png` for the cut-outs along the
bottom. Missing files just mean that element is not painted.

In the repo these live in `assets\assets\`, which `build.ps1` copies into
`build\assets\`. **Everything in that folder ships in the installer**, so only
what the app loads belongs there. `Collage.cs` holds a table of where each
cut-out sits in the 4200x2181 design export, ordered back to front.

Full-resolution exports live in `design\`, which is **not in the repository** —
Figma is the source of truth. Re-export it there to run `make-assets.ps1`, which
regenerates the shipped cut-outs at 3x the 560px canvas.

## Limits

- **Administrator windows.** Windows will not let a normal process hook input
  destined for an elevated one. Run Tsuru as administrator to cover those.
- **Games.** Anything reading raw input or running exclusive fullscreen belongs
  in the exclusion list.
- **Very old apps** that only act on whole 120-unit deltas will look steppy.
- Errors land in `%APPDATA%\Tsuru\log.txt`, which stays empty in normal use.
