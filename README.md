<div align="center">

# 🖱️ MouseControl

Configurable cursor motion for Windows, with profiles and optional Arduino HID output.

[![Release](https://img.shields.io/github/v/release/SleNdd/MouseControl?style=flat-square&color=8b5cf6)](https://github.com/SleNdd/MouseControl/releases/latest) ![Windows 11 x64](https://img.shields.io/badge/Windows_11-x64-0078D4?style=flat-square&logo=windows) [![MIT license](https://img.shields.io/badge/License-MIT-22c55e?style=flat-square)](LICENSE)

[![Download EXE](https://img.shields.io/badge/Download-Windows_EXE-8b5cf6?style=for-the-badge&logo=github)](https://github.com/SleNdd/MouseControl/releases/latest/download/MouseControl.exe) [![User guide](https://img.shields.io/badge/Read-User_Guide-334155?style=for-the-badge)](docs/USAGE.md) [![Report an issue](https://img.shields.io/badge/Report-an_Issue-334155?style=for-the-badge)](https://github.com/SleNdd/MouseControl/issues)

<img src="docs/screenshots/overview.jpg" width="760" alt="MouseControl interface with profile tabs, motion controls and direction preview">

[More screenshots](docs/screenshots/README.md) · [Build from source](docs/DEVELOPMENT.md) · [Arduino setup](firmware/MouseControlLeonardo/README.md)

</div>

## Get started

1. Download **MouseControl.exe** from the [latest release](https://github.com/SleNdd/MouseControl/releases/latest). No installer or separate .NET installation is required.
2. Set vertical/horizontal speed, choose a motion mode and an activation binding.
3. Press **F8** to enable, switch to another application, then release and hold the activation binding to move the cursor.

The application starts disabled. Movement pauses while its own window is foreground; edits and profile changes require a fresh release and press.

## Features

| | Controls |
| --- | --- |
| ↕️ Motion | Signed axis speeds up to ±2000 units/s, numeric inputs, sliders and a direction pad. |
| 〰️ Models | Classic or Bezier, with independent Pulse and Nonlinearity effects. |
| 🎲 Randomize | Editable per-hold spreads: speeds default to ±15 units/s, Humanization to +10 percentage points. |
| 🗂️ Profiles | Named tabs, colors, duplication, reordering and undo after deletion. |
| 🔔 Feedback | Tray controls, sound cues, vertical-speed notifications and an optional faint corner status label. |
| 🔌 Output | Windows `SendInput` or Arduino Leonardo USB HID; requested host rates of 125, 250, 500 or 1000 Hz. |
| 🪟 Identity | Startup name and icon selected from an accessible running process, with browsers preferred. |

**Randomize** adds a spread field beside each base value. `400 ±15` selects 385–415; `5 +10` Humanization selects 5–15%. A new set is chosen for each activation hold and kept until release.

Settings and profiles are stored in `%LOCALAPPDATA%\MouseControl`. The launcher prepares its own executable in the `Runtime` subfolder; other applications' files are only read. See the [user guide](docs/USAGE.md) for startup identity, hotkeys and output-rate limits.

## Documentation

- [User guide](docs/USAGE.md) — controls, settings and common questions.
- [Development](docs/DEVELOPMENT.md) — build, publish, checks and project layout.
- [Arduino guide](firmware/MouseControlLeonardo/README.md) — firmware and serial connection.
- [Technical documentation](docs/README.md) — motion models, UI and implementation contracts.

## License

[MIT](LICENSE) · Copyright © 2026 SleNdd.

Third-party components retain their own notices; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
