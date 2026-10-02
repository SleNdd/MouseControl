# MouseControl: UI and interaction specification

Author: **SleNdd**. Date: **2026-10-02**.

## Window and appearance

The initial window target is **720 × 500 effective pixels**, scaled for Windows DPI. The window
is resizable and uses native window controls. The direction pad is **236 × 236**. The layout
uses Segoe UI, Fluent controls, modest rounding, and Desktop Acrylic with Windows-managed
material fallback.

Follow Windows light/dark appearance and system accent changes. Sliders and the direction
drawing use the accent; profile colors remain independent markers. Text, controls, and the
direction handle stay opaque. Reduced-animation settings leave a static preview trace.

All interface text is English. The corner credit is **prod. by SleNdd**.

## Profiles

Browser-style tabs show a name, color marker, selected state, and close affordance. Long names
are shortened visually and remain available in a tooltip. The trailing **+** creates a profile
from the current settings with a new ID and generated name.

Double-click a tab or select **Edit name and color** in its context menu to open the editor.
Names must be nonempty; colors are opaque RGB values. The context menu also offers
**Duplicate**. Profile identity uses stable IDs, so display names may repeat.

Closing a tab deletes that profile and offers **Undo**. Removing the final profile creates
an inert default. Switching, editing, or deleting profiles stops the current motion and
requires a fresh activation hold.

Per-profile settings are name, color, X/Y components, Humanization, activation binding, model,
and maximum component rate. The global toggle hotkey, input backend, Arduino port, and active profile ID are application
settings. Enabled and pressed state are transient. Edits save after a short debounce; normal
exit flushes pending changes. Errors are visible and preserve the previous settings document.

## Main controls

Exactly three sliders have matching editable numeric fields:

| Label | Range | Meaning |
| --- | --- | --- |
| Vertical | −100 to +100 | Up to down; zero is stationary on Y. |
| Horizontal | −100 to +100 | Left to right; zero is stationary on X. |
| Humanization | 0 to 100% | Strength of model variation; zero is the deterministic baseline. |

Arrow keys adjust a focused slider. Numeric values are bounded by the same ranges. A
double-click resets an axis slider to zero. All axis controls and the direction pad share
one X/Y state.

Settings contain the **Smooth/Organic** selector and maximum component rate. The UI rate
range is 1–1000 relative input units/second. The dialog also selects **Windows (SendInput)**
or **Arduino Leonardo (USB HID)**, with an editable COM-port selector for Arduino, and opens
the application's settings folder. Model/rate controls do not add a fourth main slider.

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
change a component by 0.01; Ctrl+arrow changes it by 0.1.

The handle sets a movement component, rather than a screen destination. A separate marker
and dashed trace show the local preview. Its tooltip states a 1.5-second window and 0.8 canvas
pixels per relative input unit. The drawing is clipped to the pad; it never submits system
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

Click or double-click the icon to show the window. Right-click opens a native menu containing:

- **Show MouseControl**
- **Enabled**, checked when the master switch is on
- Current status
- **Profiles**, with the active profile checked
- **Exit**

Opening and closing the menu invalidate the current activation hold. Explorer restart
recreates the icon. The title-bar close button and tray **Exit** stop output, save pending
settings, and close the application.
