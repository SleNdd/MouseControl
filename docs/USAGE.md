# User guide

[Download the Windows x64 executable](https://github.com/SleNdd/MouseControl/releases/latest/download/MouseControl.exe) · [Screenshots](screenshots/README.md)

## Start and activate

Run `MouseControl.exe`, configure a profile and enable movement with **F8** or the main switch.
Switch to another application, release the activation binding, then hold it to move the cursor.
Release it to stop. Supported bindings are LMB, RMB, LMB + RMB, MMB, Alt and Shift.

Startup is always disabled. Movement pauses while MouseControl is foreground. Enabling,
editing a profile, switching profiles or returning from suspension requires a released
binding followed by a new press. The direction-pad preview never moves the actual cursor.

## Motion controls

| Control | Meaning |
| --- | --- |
| Vertical | Signed speed from −2000 to +2000 input units/s; negative moves up. |
| Horizontal | Signed speed from −2000 to +2000 input units/s; negative moves left. |
| Humanization | Strength from 0 to 100%, shared by the selected model and effects. |
| Direction pad | Changes both axes together; double-click resets both. |
| Classic | Straight motion, optionally modified by Pulse and Nonlinearity. |
| Bezier | Curved motion segments. |
| Pulse | Varies movement in bursts using Humanization strength. |
| Nonlinearity | Adds changes in direction and speed using Humanization strength. |

Humanization is unavailable for Classic when both effects are off. Axis values describe
relative input, not screen pixels; Windows pointer settings affect the resulting movement.

## Randomize

Enable **Randomize** beside Pulse and Nonlinearity to reveal a spread field next to each base value.

| Parameter | Default spread | Example |
| --- | --- | --- |
| Vertical | ±15 units/s | `400 ±15` selects 385–415. |
| Horizontal | ±15 units/s | `70 ±15` selects 55–85. |
| Humanization | +10 percentage points | `5 +10` selects 5–15%. |

One independent uniform value is selected for each parameter at the start of an accepted
hold and kept until release. Values stay within the valid speed and percentage limits.
The editable spreads are saved per profile. A zero spread keeps an exact value; disabling
Randomize keeps the saved spreads and uses the base values again.

## Profiles and hotkeys

- **+** creates a profile from the current settings.
- Double-click a tab or use its context menu to edit its name and color.
- The context menu also duplicates profiles; tabs can be dragged to reorder them.
- Closing a tab offers Undo and always leaves at least one profile.
- Click the toggle-key button to capture another key. Escape cancels capture; its context
  menu can assign Escape itself. Failed registrations keep the previous binding.
- **Ctrl + Shift + Up/Down** changes vertical speed by 50 units/s by default. Change both
  bindings and the step in Settings.

## Settings and feedback

**Sound notifications** and **Desktop notifications** can be enabled independently. On/Off
notifications include the signed vertical speed. Rapid sound events replace the previous
cue instead of accumulating a playback queue.

**Always show status and vertical speed** enables a tiny outlined label in the primary
display's top-left corner, such as `on 450` or `off -350`. It has a transparent background,
20% text opacity and passes mouse clicks through. During a randomized hold it shows the
selected vertical speed; between holds it shows the base speed. It remains visible while
the main window is in the tray. The option is off by default.

**Requested output rate** offers 125, 250, 500 and 1000 Hz, defaulting to 1000. This is a host
scheduling target, not a guaranteed report rate or USB polling setting. Windows timing and
serial round trips affect the actual rate. Leonardo USB HID polling is limited to 1000 Hz.

**Start in tray** and **Close button minimizes to tray** control window visibility. The tray
menu can show the window, toggle movement, select a profile or exit. Tray **Exit** stops the
application. When the tray icon is unavailable, the window stays accessible.

The startup support dialog can be suppressed with **Don't ask again** after its five-second
countdown. It currently contains placeholder donation text.

## Output backends

**Windows (SendInput)** submits relative mouse input through Windows.

**Arduino Leonardo (USB HID)** requires the [included firmware](../firmware/MouseControlLeonardo/README.md).
Choose the board's COM port in Settings and save. Offline discovery retries every two
seconds. Disconnection disables movement; after reconnection, enable it again. The app
does not fall back to Windows output when Arduino is unavailable.

## Startup identity and storage

At launch, the executable selects a readable process in the current user session. Browsers
take priority; within a tier, a larger same-name process group is preferred. System
processes and MouseControl's own instances are excluded. Unavailable candidates are skipped.

The launcher extracts its own application into `%LOCALAPPDATA%\MouseControl\Runtime`, prepares
a copy with the selected filename and icon, and starts that copy. The window and tray use
the same identity. Other applications' executables are only read. If no suitable process
is found, MouseControl uses its own name. `--no-mask` starts without selecting another identity.

The first launch can take longer while files are prepared. Later launches reuse the cache.
Starting the launcher again while the app is running shows its existing window.

Profiles and global settings are saved in `%LOCALAPPDATA%\MouseControl\profiles.json`.
**Open data folder** in Settings opens this location. Corrupt or unsupported settings are
preserved; load/save errors appear in the window, and recovery writes use a separate file.
