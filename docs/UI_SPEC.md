# MouseControl: UI and interaction specification

Author: **SleNdd**. Date: **2026-10-13**.

## Window and appearance

The initial window target is **720 × 500 effective pixels**, scaled for Windows DPI. The window
is fixed in size and uses native window controls. The direction pad is **236 × 236**. The layout
uses Segoe UI, Fluent controls, modest rounding, and Desktop Acrylic with Windows-managed
material fallback.

Follow Windows light/dark appearance and system accent changes. Sliders and the direction
drawing use the accent; profile colors remain independent markers. Text, controls, and the
direction handle stay opaque. Reduced-animation settings leave a static preview trace.

All interface text is English. The underlined corner link **prod. by SleNdd** opens
`https://github.com/SleNdd` and uses readable theme foreground colors.

## Profiles

Browser-style tabs show a name, color marker, selected state, and close affordance. Long names
are shortened visually and remain available in a tooltip. The trailing **+** creates a profile
from the current settings with a new ID, generated name, and new random color.
Dragging changes the stored order without changing profile IDs or restarting an active profile.

Double-click a tab or select **Edit name and color** in its context menu to open the editor.
Names must be nonempty; colors are opaque RGB values. The context menu also offers
**Duplicate**. Profile identity uses stable IDs, so display names may repeat.

Closing a tab deletes that profile and offers **Undo**. Removing the final profile creates
an inert default. Switching, editing, or deleting profiles stops the current motion and
requires a fresh activation hold.

Per-profile settings are name, color, X/Y components, Humanization, activation binding, model,
and pulse/correction effect flags. The global toggle hotkey, input backend, Arduino port,
output rate, startup-in-tray preference and active profile ID are application settings.
Enabled and pressed state are transient. Edits save after a short debounce; normal
exit flushes pending changes. Errors are visible and preserve the previous settings document.

## Main controls

Exactly three sliders have matching editable numeric fields:

Numeric fields omit spin buttons and the inline clear button. Typing and keyboard
adjustments remain available.

| Label | Range | Meaning |
| --- | --- | --- |
| Vertical | −2000 to +2000 units/s | Up to down; zero is the commanded Y baseline. |
| Horizontal | −2000 to +2000 units/s | Left to right; zero is the commanded X baseline. |
| Humanization | 0 to 100% | Strength of model variation; zero is the deterministic baseline. |

Arrow keys adjust a focused slider by one input unit/second. Numeric values are bounded by the same ranges. A
double-click resets an axis slider to zero. All axis controls and the direction pad share
one X/Y state.

The footer selects **Classic** or **Bezier**, with independent inline **Pulse** and
**Nonlinearity** checkboxes. The Humanization slider scales the mode and selected effects.
For Classic with both effects off, strength controls are disabled while their value is
retained. Legacy model migration is described in [motion models](MOTION_MODELS.md).

Settings contain the requested host output rate: 125, 250, 500, or 1000 Hz, and **Start in tray**.
The dialog also selects **Windows (SendInput)**
or **Arduino Leonardo (USB HID)**, with an editable COM-port selector for Arduino, and opens
the application's settings folder. Rate controls do not add a fourth main slider.
The output rate is a scheduling target; acknowledgements and Windows scheduling can reduce
the effective rate. Existing profile percentages are converted to the full speed range while
preserving their commanded X/Y velocities.

The Arduino-only footer indicator is green **Online** after firmware identification and red
**Offline** while disconnected. Offline discovery checks only the selected port every two
seconds and stops once connected. USB notifications and submission failures disconnect the
backend and disable movement. No idle heartbeat is sent; Online records the established
connection rather than continually measured device health. Reconnection leaves motion disabled.

## Direction pad and preview

The pad draws a faint grid, stronger centered axes, an origin, a direction ray, and a handle.
Positive X is right; positive Y is down. Within usable square bounds `(left, top, L)`:

```text
x = clamp(2 * (pointerX - left) / L - 1, -1, 1)
y = clamp(2 * (pointerY - top) / L - 1, -1, 1)
handleX = left + (x + 1) * L / 2
handleY = top + (y + 1) * L / 2
```

The usable bounds include a margin for the handle radius. Dragging updates both axes.
Double-clicking the pad or pressing Home with pad focus resets both to zero. Arrow keys
change a speed by one unit/second; Ctrl+arrow changes it by ten.

The handle sets a movement component, rather than a screen destination. A separate marker
and dashed trace show the local preview. Its tooltip states a 1.5-second window and the
current fitted scale. The whole trajectory is scaled to fit the pad; it never submits system
input. The preview button pauses/resumes animation. A zero vector remains stationary.

## Activation and hotkey

**Activate on** offers exactly LMB, RMB, LMB + RMB, MMB, Alt, and Shift. Activation is held,
not toggled. LMB/RMB honor Windows button swapping. The master **Enabled** switch and global
hotkey control the same state; the default toggle hotkey is **F8**.

Click the toggle-key button to capture a key with supported modifiers. Capture pauses motion;
Escape cancels. Registration must succeed before the new binding is saved. Unsupported,
reserved, or conflicting keys show a notice and preserve the previous binding. A held toggle
key does not repeatedly toggle Enabled.

The app starts disabled. Its foreground window, hotkey capture, tray menus, session lock,
and power suspension pause motion. Enabling, changing settings/profiles, or leaving suspension
requires release and a new activation press.

| Status | Meaning |
| --- | --- |
| Disabled | Master switch is off. |
| Idle | Enabled, waiting for activation. |
| Active | Motion is active. |
| Paused | Enabled, with a suspension in force. |

Input failure disables motion and displays an error. The switch, status, and tray state stay
synchronized.

## Tray and exit

Minimize and the tray button hide the window when the notification icon is available.
Hiding stops the current motion and requires a fresh hold. If tray creation fails, hiding
is unavailable and the window remains accessible.
The optional **Start in tray** preference applies after window initialization, only when
the notification icon is available. Its default is off, including for older settings files.
It does not enable movement at startup.

Click or double-click the icon to show the window. Right-click opens a native menu containing:

- **Show MouseControl**
- **Enabled**, checked when the master switch is on
- Current status
- **Profiles**, with the active profile checked
- **Exit**

Opening and closing the menu invalidate the current activation hold. Explorer restart
recreates the icon. The icon remains present while the main window is visible.
The title-bar close button saves and hides to the tray by default. Settings can disable this
behavior. Tray **Exit** stops output, saves pending settings, and closes the application.

## Feedback and support prompt

Settings contain **Vertical +**, **Vertical −**, and a step of 1–2000 units/s (default 50).
The default chords are Ctrl + Shift + Up/Down. These adjust the signed vertical value within
the existing range and rearm motion after the edit. Key capture pauses motion; conflicts or
registration failures preserve previous bindings.

On/Off and vertical changes produce distinct sound cues and profile-colored panels. Panels
are 220 × 54 effective pixels, flush to the current screen's left edge, 72 effective pixels
below the work area's top. A tinted surface, fine border, narrow shadow, and transparency
follow dark/light appearance. Panels slide and stack downwards, with up to four visible.
They do not take focus or intercept clicks. Both sounds and panels can be disabled.

The voluntary support dialog currently contains WIP text. Close and Escape are unavailable
for five seconds after it opens. It appears on every startup until the persisted **Don't ask
again** preference is selected. It does not contain a payment action.
