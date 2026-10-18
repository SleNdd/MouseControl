# MouseControl.Core

Managed .NET 10 motion, activation, and configuration code. Native input submission belongs
to the application layer. New settings default to `Classic`, an exact straight baseline with
optional effects. `Bezier` provides continuous synthetic motion variation. Legacy models
`Smooth`, `Organic`, `Wind`, `Pulse`, and `Corrections` remain supported for persisted profiles.
Their enum values 0–4 and `Bezier` value 5 are retained; `Classic` is appended as value 6.

## Runtime contract

- `MotionSettings` is an immutable record. X/Y are normalized components in `[-1, 1]`,
  `Humanization` is in `[0, 1]`, and `MaxComponentRate` is a positive finite rate at most
  2000 in relative input units/second (default 150). Validate before exposing settings to a running session.
- `MotionGenerator(settings, seed).Step(dtSeconds)` returns floating-point displacement.
  Each component is bounded by `MaxComponentRate * dtSeconds`. The path has a smooth onset,
  correlated speed/lateral variation, and timer partition invariance. Humanization zero
  ignores both seed and model; a zero vector always produces exact zero.
- Optional trailing `PulseOverlay` and `CorrectionsOverlay` flags default to false. Each
  activates its effect alongside the selected model. Selecting the matching base model
  already activates that effect; setting its flag does not apply it twice. Nonzero commanded
  components retain their sign; a component commanded to zero may vary laterally.
  The application's Nonlinearity option uses `CorrectionsOverlay`, serialized as
  `correctionsOverlay`.
  Classic without either effect matches the exact Humanization-zero baseline at every
  strength and seed. Selected effects still use Humanization to scale their contribution.
- Nonpositive dt produces zero. Nonfinite dt throws. A dt above 100 ms resets onset and
  returns zero; the caller must not accumulate catch-up time. `ResetSession()` resets the
  seeded path. `UpdateSettings()` validates and resets, leaving prior state intact on failure.
- `ActivationGate` starts disabled. After enabling, `Invalidate()`, or any suspended step,
  it requires an observed release while enabled and unsuspended, followed by a press.
  Pass the whole activation chord as `held`; release/repress applies to that chord.
- `ActivationRandomizer.Sample(settings, seed)` selects effective parameters for one accepted
  hold when optional `RandomizeOnActivation` is true. `HorizontalSpread`/`VerticalSpread`
  default to 15 input units/s in either direction; `HumanizationSpread` defaults to 10
  upward percentage points. Uniform sampling uses the clipped valid intervals. Base settings
  are immutable; sample once when a hold is accepted, before creating its generator, and
  discard the sample on release. The enable flag and three spreads are optional persisted
  motion fields; older profiles default to randomization off and the listed spread defaults.
  Spread values remain in the profile while randomization is off.
- Serialize runtime calls on one owner thread. On profile changes, invalidate the gate and
  reset the generator. When activation stops, reset the generator and discard the consumer's
  fractional input-unit carry. Integer rounding/input injection belongs to the application.

## Motion models and combinations

All models integrate linear interpolation between velocity samples on a fixed 200 Hz grid.
Caller tick boundaries do not decide random target updates. Every output has the common onset
factor `1 - exp(-t / 0.16)` and is clamped per component after applying the effects. With
command vector `(x, y)`, scalar speed `s`, and lateral coefficient `l`, the components before
clamping are `(x*s - y*l, y*s + x*l) * MaxComponentRate`. Variation therefore scales with the
command vector, including small velocities. Component clamping can reduce variation near
the configured rate limit or where a lateral correction would reverse a commanded axis.

At Humanization 1, the model coefficients before component clamping are:

| Model | Speed coefficient | Lateral coefficient | Timing |
| --- | --- | --- | --- |
| Classic | `1` | `0` | Exact straight onset baseline; no random target updates |
| Smooth | `1 + 0.55*q` | `0.45*r` | Noise targets held 80–220 ms; exponential response with 100 ms time constant |
| Organic | `1 + 0.85*q` | `0.80*r` | Targets held 35–150 ms; 45 ms response, noise slope capped at 10/second |
| Wind | `1 + 0.75*q` | `0.90*r` | Targets held 150–500 ms; damped inertia `q'' = 36*(target-q) - 10*q'` |
| Pulse | Envelope from 0 to 1.80 | `0.45*r * envelope` | Evolving 0.50–1.00 s cycles; 12–24% of each cycle is a zero-motion pause |
| Corrections | `1 + 0.45*cos(phase)` | `0.90*sin(phase)` | Alternating arcs; cycle period evolves within 0.80–1.40 s |
| Bezier | Filtered curve derivative, within 0–3 at full strength | Filtered perpendicular derivative, within ±1.80 at full strength | Evolving 0.45–1.20 s signed relative segments; 100 ms velocity filter and slope cap 10/second |

`q` and `r` are bounded correlated states in `[-1, 1]`. Seeded target-hold durations move 35%
toward successive random durations and are quantized upward to the integration grid. This
produces bounded, nonperiodic refresh intervals rather than one uniform cadence. Corrections
refreshes its period target every 180–700 ms and filters it with a 350 ms time constant;
the arc phase continues through period changes. Pulse cycle periods and pause fractions also
evolve by 35% toward new seeded targets. Its squared-sine envelope joins each pause with
zero slope at both edges.

Bezier uses the adapted MIT-licensed GhostCursorSharp geometry in
`ThirdParty/GhostCursorBezier.cs`; its source attribution and license are retained beside that
file. Each segment endpoint is the command vector times the segment duration. Control-point
spread is `0.60 * endpointMagnitude * h`. The derivative divided by duration supplies the
velocity shape, with speed deviation blended by `h`; filtered velocity continues through
segment boundaries. Filtering and component caps mean the submitted displacement is not an
exact traversal of the geometric endpoint. The geometry has no absolute screen-coordinate
clamping and supports signed coordinates in all quadrants.

Corrections adds its speed deviation and lateral arc to the base coefficients. Pulse then
multiplies both resulting components by `1 - h + h*envelope`, where `h` is Humanization.
Consequently a full-strength Pulse pause stops the entire combination, including Corrections.
The two effects use separate derived random streams, so enabling them does not consume the
base model's random sequence. Humanization scales deviations and lateral variation; at zero
the generator bypasses every effect and returns the same seeded-independent onset baseline.

## Storage contract

`ProfileStore(path).Load()` returns `ProfileLoadResult(Configuration, Error)` and
`Save(configuration)` returns `ProfileSaveResult(Success, Error)`. A missing file yields fresh
defaults without writing. Any corrupt, invalid, unknown-field, or unsupported-version file
returns an error and is preserved byte-for-byte; Save also refuses to replace it. Recovery
requires the application/user to explicitly preserve or relocate that file before a new one
is saved. Never silently convert a failed load into an overwrite.

Version 1 is a single camelCase JSON document:

```json
{
  "version": 1,
  "appSettings": {
    "toggleHotkey": { "virtualKey": 119, "modifiers": "None" },
    "activeProfileId": "6d3433a2-a5dc-4596-b117-9cb56a18721e",
    "inputBackend": "Windows",
    "arduinoPort": "",
    "outputRateHz": 1000,
    "startInTray": false
  },
  "profiles": [
    {
      "id": "6d3433a2-a5dc-4596-b117-9cb56a18721e",
      "name": "Default",
      "color": "#60A5FA",
      "activation": "Lmb",
      "motion": {
        "x": 0, "y": 0, "humanization": 0,
        "maxComponentRate": 150, "model": "Classic",
        "pulseOverlay": false, "correctionsOverlay": false
      }
    }
  ]
}
```

Original version-one fields are required. `inputBackend` and `arduinoPort` are optional when
reading older files and default to `Windows` and an empty string. Backend values are `Windows`
or `Arduino`; an Arduino selection requires a valid COM port name. Persisting a selection does
not itself open a connection or enable motion.
`outputRateHz` is optional in older files, defaults to 1000, and must be 125, 250, 500, or 1000.
`startInTray` is optional in older files and defaults to false. It is a window startup
preference; enabled/armed state remains transient and always starts disabled.
`verticalIncreaseHotkey` and `verticalDecreaseHotkey` are optional/nullable. Missing or null
values use Ctrl + Shift + Up/Down. `verticalStep` defaults to 50 and accepts whole values 1–2000.
`closeToTray`, `soundNotifications`, and `desktopNotifications` are optional and default to
true. `suppressDonationPrompt` and `persistentStatusOverlay` default to false. These remain version-one optional fields;
older application versions that reject unknown fields cannot read a newly saved document.
`pulseOverlay` and `correctionsOverlay` are optional when reading older motion settings and
default to false. `randomizeOnActivation` also defaults to false. `horizontalSpread` and
`verticalSpread` default to 15 and accept finite values from 0 to 2000 input units/s;
`humanizationSpread` defaults to 10 and accepts finite values from 0 to 100 percentage points.
All seven model names, both effect flags and randomization settings roundtrip in version 1.

IDs are nonempty and unique; names may repeat. At least one profile
must exist, and the active ID must refer to one. Colors use `#RRGGBB`. Activation values are
`Lmb`, `Rmb`, `LmbAndRmb`, `Mmb`, `Alt`, `Shift`. Modifier flags are `None`, `Alt`, `Control`,
`Shift`, `Win`, with combinations serialized as comma-separated strings. Enabled/armed state
is never stored. Documents are limited to one megabyte.

Save serializes a snapshot, flushes a uniquely named sibling temporary file, and atomically
replaces an existing valid file. Failure preserves the prior document and attempts to remove
the temporary file. An exclusive sibling `.lock` file serializes cooperating writers;
contention fails without writing and can be retried. Valid documents use last-writer-wins
semantics. Load retries transient Windows sharing violations for at most 30 ms. External
editors that bypass the lock are outside the concurrency contract. Flushed contents and
atomic replacement do not establish a guarantee against filesystem/power-loss failures.

## Behavioral harness

```powershell
dotnet run --project tests/MouseControl.Core.Tests/MouseControl.Core.Tests.csproj -c Release
```

The harness uses isolated temporary directories and exits nonzero on a failed assertion.
Motion checks cover all models, effect combinations, seeded reset, fixed-grid partition
invariance, bounds/sign preservation, exact zero, stall reset, nonperiodic refresh timing,
command-vector scaling, smooth onset, Classic's exact baseline at all strengths, independent
Classic effects, full-strength pauses, and optional-field compatibility.

After running the default publish script, verify the transformed Core directly:

```powershell
$protectedCore = (Resolve-Path 'src/MouseControl.App/obj/Release/net10.0-windows10.0.19041.0/win-x64/obfuscated-core/MouseControl.Core.dll').Path
dotnet run --project tests/MouseControl.Core.Tests/MouseControl.Core.Tests.csproj -c Release `
    "-p:ObfuscatedCorePath=$protectedCore" `
    "-p:OutputPath=$PWD/artifacts/checks/protected-core-tests/" `
    -p:IntermediateOutputPath=obj/protected/Release/ -- --protected
```

The regular harness has 29 checks. Artifact mode has 27: it retains all public behavior,
persistence, and license checks, skips three checks that inspect original private field
names, and adds verification of renamed private members and preserved public constructor
parameters and enum identities. Native verification must additionally confirm that the
single-file executable contains this transformed assembly.
