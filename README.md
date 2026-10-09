# MouseControl

Windows cursor-motion application by **SleNdd**, written in C# with WinUI 3, Windows App SDK,
and .NET 10.

## Features

- Three sliders with numeric fields: signed vertical and horizontal components, and
  Humanization strength.
- A synchronized square direction pad and local motion preview.
- Classic and Bezier motion modes, with independent Pulse and Nonlinearity switches.
- Axis speeds from −2000 to +2000 relative input units/second, with one-unit increments.
- Requested host output rates of 125, 250, 500, or 1000 Hz.
- Hold-to-activate bindings: LMB, RMB, LMB + RMB, MMB, Alt, or Shift.
- A master enable switch and configurable global toggle hotkey; the default is F8.
- Profile tabs with editable names and colors, duplication, deletion, and Undo.
- Desktop Acrylic, Windows light/dark appearance, and system accent colors.
- A notification-area menu for showing the window, enabling motion, selecting a profile,
  and exiting, plus optional startup in the tray.
- Windows relative input or Arduino Leonardo USB HID output, with an Arduino-only
  Online/Offline indicator and reconnection while offline.

The Windows backend uses the documented `SendInput` API. The Arduino backend uses the
included [Leonardo firmware](firmware/MouseControlLeonardo/README.md) over the selected COM
port. The default firmware build uses the board's standard USB identity. Component rates
are measured in relative input units per second; Windows pointer settings affect the resulting
screen movement.

## Build

Use Windows 11 x64 with the .NET 10 SDK. Publishing also requires the .NET 8 runtime for
the pinned Obfuscar build tool. Run commands from the repository root.

```powershell
dotnet build src/MouseControl.App/MouseControl.App.csproj -c Release
.\scripts\publish.ps1
```

The publish script creates one self-contained Windows x64 executable. By default it installs
Obfuscar.GlobalTool 2.2.50 locally under `artifacts/tools` and obfuscates private Core
implementation names before bundling. Public APIs, record constructor parameters, enum
names, JSON fields, resources, and third-party notices are preserved. The WinUI application
assembly is not transformed; string hiding is disabled. Compiler outputs remain unchanged.
An obfuscation failure stops publishing instead of silently producing a plain build.

For a plain development executable, use `.\scripts\publish.ps1 -Plain`. Obfuscation reports
and name mappings stay in ignored build staging directories and are not included in the EXE.

Start:

```powershell
.\artifacts\MouseControl\MouseControl.exe
```

No separate .NET or Windows App SDK installation is needed. Embedded components extract
to the user's runtime cache at launch. Settings are stored separately.
The published EXE can be renamed while retaining the `.exe` extension. The resource index
uses the standard `resources.pri` name inside the bundle, independently of the EXE filename.

## Use

1. Set horizontal and vertical direction with the sliders, numeric fields, or direction pad.
   Negative X moves left, positive X right, negative Y up, and positive Y down.
2. Choose **Classic** for straight movement or **Bezier** for curved segments. Enable
   **Pulse** and/or **Nonlinearity** as needed. Humanization sets their shared strength from
   0 to 100%; it is unavailable for Classic with both effects off. The preview draws locally
   and fits its trajectory to the pad; it does not move the system cursor.
3. Choose an activation binding and enable motion with the switch or global toggle key.
4. Switch to another application, release the activation binding, then hold it to move.

The app starts disabled. Motion pauses while MouseControl is foreground. Enabling, editing,
switching profiles, or returning from suspension requires release and a fresh activation
press. Releasing the binding stops movement.

Axis values are commanded speeds in relative input units/second. The range is ±2000 per
axis, with one-unit slider and numeric increments. Existing profiles keep their commanded
speeds when converted from the older percentage controls.
Older Pulse/Corrections profiles become Classic with the corresponding switch enabled.
Older Smooth/Organic/Wind profiles become Bezier when their strength is nonzero, or Classic
at zero strength. Names, colors, activation bindings, direction, strength and global settings
are retained. Existing Classic and Bezier selections are unchanged.

**Settings → Requested output rate** selects the host scheduling target, defaulting to
1000 Hz. This is separate from motion speed and USB polling. Windows scheduling and serial
acknowledgements affect the effective output rate. See [motion models](docs/MOTION_MODELS.md).

Use **+** to create a profile from the current settings. Double-click a tab or use its context
menu to edit its name/color; the context menu also offers duplication. Closing a profile tab
offers Undo and always leaves at least one profile.

Click the toggle-key button to capture a new hotkey. Escape cancels; its own binding is
available from the button's context menu. A failed registration
keeps the previous binding. Minimize or use the tray button to hide the window; click the
tray icon to restore it. The window close button and tray **Exit** stop the application.
**Settings → Start in tray** remembers the startup preference. If the notification icon
cannot be created, the window stays visible. Startup always leaves movement disabled.

Settings are saved under `%LOCALAPPDATA%\MouseControl`. Invalid existing settings are
preserved, and save/load errors appear in the window.

## Arduino

Follow the [firmware installation guide](firmware/MouseControlLeonardo/README.md), then open
**Settings → Input method → Arduino Leonardo (USB HID)**, select the COM port, and Save.
Offline discovery checks the selected port every two seconds. Once firmware identification
succeeds, the indicator turns green and discovery stops. No idle heartbeat is sent.

USB removal notifications and command failures return the backend to Offline and disable
motion. Reconnection requires enabling motion again. Online describes the established
connection; an idle firmware hang is detected on the next command. The app does not fall
back to Windows input when Arduino is unavailable.

The sketch requires only Leonardo. It does not operate a USB Host Shield or forward a
physical mouse attached to one. Uploading replaces an existing sketch.

## Development checks

```powershell
dotnet run --project tests/MouseControl.Core.Tests/MouseControl.Core.Tests.csproj -c Release
dotnet run --project src/MouseControl.App/Services/.checks/ServiceChecks.csproj -c Release
```

These check core behavior and isolated runtime paths. Native tray, hotkey, session, and input
integration require separate Windows verification.

The Core harness can also run against the transformed assembly. See the
[artifact verification command](src/MouseControl.Core/README.md#behavioral-harness).

Firmware was compiled with Arduino AVR Boards 1.8.8 and Mouse 1.0.1. Compile with Arduino
CLI after installing those dependencies:

```powershell
.\scripts\build-firmware.ps1
```

## Documents

- [UI and interaction specification](docs/UI_SPEC.md)
- [Motion models and timing](docs/MOTION_MODELS.md)
- [Ghost Cursor upstream comparison](docs/UPSTREAM_REVIEW.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [Visual concepts](docs/concepts/README.md)
- [Core contracts](src/MouseControl.Core/README.md)
- [Application service contracts](src/MouseControl.App/Services/README.md)

The application credit reads **prod. by SleNdd**.
Repository: [SleNdd/MouseControl](https://github.com/SleNdd/MouseControl).
