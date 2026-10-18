# Development

## Requirements

- Windows 11 x64.
- .NET SDK **10.0.301**, pinned in [`global.json`](../global.json), with latest-patch roll-forward.
- Network access for the first NuGet restore and tool installation.

Run commands from the repository root. Released executables are self-contained and do not
require these development dependencies on the receiving machine.

## Build and publish

```powershell
dotnet build src/MouseControl.App/MouseControl.App.csproj -c Release
.\scripts\publish.ps1
.\artifacts\MouseControl\MouseControl.exe
```

Publishing builds the WinUI application, transforms private Core implementation names,
archives the self-contained application and embeds it in one launcher executable.
The final download is `artifacts/MouseControl/MouseControl.exe`.

The script installs Obfuscar.GlobalTool **2.2.50** locally under `artifacts/tools` when needed.
Public APIs, JSON fields, constructor parameters, enum names, resources and notices remain
available. The WinUI assembly is not transformed; string hiding is disabled. A transformation
failure stops publishing. Reports and name mappings remain in ignored staging directories.

```powershell
# Development package without the Core transformation.
.\scripts\publish.ps1 -Plain

# Choose another destination.
.\scripts\publish.ps1 -OutputDirectory "$PWD\artifacts\custom-build"
```

The launcher project embeds the archive prepared by `publish.ps1`; use this script when
producing a distributable instead of publishing the launcher project on its own.

## Checks

```powershell
dotnet run --project tests/MouseControl.Core.Tests/MouseControl.Core.Tests.csproj -c Release
dotnet run --project src/MouseControl.App/Services/.checks/ServiceChecks.csproj -c Release
```

The Core harness contains 29 checks. The service harness contains 31 Windows checks and an
optional native audio check (`-- --audio-smoke`). Tests use isolated data and simulated
output where applicable; they do not replace physical Arduino testing.

After publishing:

```powershell
dotnet run --project tests/MouseControl.Launcher.Checks/MouseControl.Launcher.Checks.csproj -c Release -- src/MouseControl.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/MouseControl.App.exe
$env:MOUSECONTROL_VERIFY_OUTPUT = "$PWD\artifacts\launch-verification.json"
.\artifacts\MouseControl\MouseControl.exe --verify-launch
```

The launcher harness contains 25 checks, including native icon/version resources, cache
reuse/recovery and archive bounds. The diagnostic launch uses temporary profiles and
simulated output, checks window/tray identity, desktop feedback, the corner indicator and
Randomize controls, writes JSON and closes. Add `--no-mask` to exercise the fallback identity.

To verify the transformed Core with its 27-check artifact harness, follow the
[Core verification command](../src/MouseControl.Core/README.md#behavioral-harness).

## Firmware

Install Arduino CLI, Arduino AVR Boards **1.8.8** and Mouse **1.0.1**, then run:

```powershell
.\scripts\build-firmware.ps1
```

Building firmware and uploading it to a board are separate operations. See the
[Arduino guide](../firmware/MouseControlLeonardo/README.md) before uploading.

## Source layout

| Path | Responsibility |
| --- | --- |
| `src/MouseControl.App` | WinUI interface, runtime, scheduling, feedback and output backends. |
| `src/MouseControl.Core` | Motion generation, activation, randomization and profile storage. |
| `src/MouseControl.Launcher` | Process selection, native executable resources and runtime preparation. |
| `tests` | Core and launcher behavioral harnesses. |
| `firmware/MouseControlLeonardo` | Leonardo sketch and serial protocol definitions. |
| `scripts` | Publishing, Core transformation and firmware builds. |

Implementation contracts: [Core](../src/MouseControl.Core/README.md),
[services](../src/MouseControl.App/Services/README.md), [motion models](MOTION_MODELS.md)
and [UI behavior](UI_SPEC.md).
