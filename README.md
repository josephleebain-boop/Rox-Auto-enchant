# ROX Stat Reroller (Windows x64, WinForms / .NET 8)

Screen-reading + mouse-clicking helper for the Enchant Awakening reroll screen in ROX Global's PC client.
It only captures screenshots and sends mouse clicks. It never touches the game's memory, files or network.
(Automation may still be against the game's Terms of Service. That risk is yours.)

## Status – read this first
* **Not compiled into an .exe here, and never run against the live game.** My sandbox has no Windows and no NuGet access.
* Tested on Linux: all text/colour logic (38 unit tests: stat parsing, warning detection, orange/blue/grey/ambiguous icons, settings). `tests/LogicTests`.
* NOT tested: WinForms UI, screen capture, Windows OCR, SendInput clicks, calibration overlay, hotkeys. Expect small fixes on first run.
* The Reroll-label region (used to confirm the right screen is open) is **my estimate**, not from your list – check it with *Test read*.

## Get the .exe (pick one)
1. **Local:** install the .NET 8 SDK once (`winget install Microsoft.DotNet.SDK.8`), then double-click `build.bat`.
   Output: `dist\RoxStatReroller.exe` – self-contained, no runtime needed on the PC where it runs (≈70–150 MB).
2. **No SDK:** push this folder to a GitHub repo; Actions → "Build Windows exe" → download the artifact.

OCR uses the engine **built into Windows 10/11** (Windows.Media.Ocr). If it says no OCR language is available, add
*English (United States)* in Settings → Time & language → Language & region.
If the game runs as Administrator, run this app as Administrator too (otherwise Windows blocks the clicks).

## Launch
1. Start the game, maximize it at 1920×1080, open **Enchant Awakening**, select the equipment.
2. Run `RoxStatReroller.exe`. It docks at the far left of the screen so it doesn't cover the game's buttons/text.
   Don't drag it over the right half or the centre popup area (it refuses to start if it overlaps a read area).

## Calibrate (do this once)
*Calibrate…* lists 8 items (3 click points, 5 read areas). Select one → *Set selected…* → the app hides, waits the
"capture delay", freezes a screenshot, and you click (points) or drag (areas). Esc cancels.
* Save button / stat text / icon / Save label: have a **Rerolled Stat** row on screen (do one manual reroll).
* Confirm + warning text: the **orange warning popup** must be open. Use a delay of ~8 s to set that up.
* *Show all on screen* overlays every configured box so you can eyeball them. Positions are stored in `settings.json`.
Fine-tune numbers in *Settings…* (grid) if needed.

## Test read (always before a live run)
Press **Test read** with a Rerolled Stat row visible. It shows what OCR read, the icon colour percentages, the images the OCR saw,
and what a real run would do. Check: stat name correct, orange stat = "Orange", blue/white stat = "Other".
Tune hue/fractions in Settings → Recognition if your orange icon isn't classified.

## Dry-run
Dry-run is ON by default. It reads the screen every ~1.5 s and logs what it *would* do (reroll / confirm / save / pause) and **never clicks**.
Reroll manually in the game while it watches. It stops after "Max rerolls" reads or at the first match.

## Live run
Untick dry-run, choose the stat and a low max (default 10), **Start**, confirm the prompt.
Loop: verify screen → read row twice (must agree) → if stat+orange: re-read, then click **Save**, stop →
else click Reroll → if the previous stat was orange, wait for the warning, **verify its text twice**, then click Confirm.

It **pauses (never keeps clicking)** when: OCR is uncertain or unstable; icon colour is ambiguous; stat name is unreadable
(orange: always; others: if "Strict stat reading" is on); the Reroll label isn't seen; the orange warning appears when not expected or
is already open; the game window is missing/not responding/not in front; the mouse was moved by you between clicks.
On pause you get a sound, taskbar flash, balloon, red status, and `debug/<time>-screen.png` + `-reason.txt`. Fix it, then **Resume (F9)** –
it re-reads everything from scratch.

## Stop safely
**F8** = emergency stop (global, works while the game has focus; also checked right before every click). **F9** = pause/resume. Or press the buttons.
Moving the mouse during a live run also pauses it.

## Limits
* Max rerolls counts Reroll clicks. Low game lag → default waits are fine; if the game is slow raise *After reroll wait* in Settings.
  If the result appears later than the wait, the app may read the old stat and reroll again (it can't distinguish two identical rolls).
* It can't detect running out of materials/currency or unknown popups except through the checks above (label unreadable, row unclear).
* Quality detection is colour based (orange/gold hue). Red or other tiers above orange, if they exist, are classified "Other".
* Files: `settings.json` (stat list, positions, timings), `log.txt`, `debug/` – all next to the exe.
