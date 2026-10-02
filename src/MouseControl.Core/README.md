# MouseControl.Core

Managed .NET 10 motion, activation, and configuration code. Native input submission belongs
to the application layer. `Smooth` and `Organic` provide synthetic motion variation.

## Runtime contract

- `MotionSettings` is an immutable record. X/Y are normalized components in `[-1, 1]`,
  `Humanization` is in `[0, 1]`, and `MaxComponentRate` is a positive finite rate at most
  2000 in relative input units/second (default 150). Validate before exposing settings to a running session.
- `MotionGenerator(settings, seed).Step(dtSeconds)` returns floating-point displacement.
  Each component is bounded by `MaxComponentRate * dtSeconds`. The path has a smooth onset,
  correlated speed/lateral variation, and timer partition invariance. Humanization zero
  ignores both seed and model; a zero vector always produces exact zero.
- Nonpositive dt produces zero. Nonfinite dt throws. A dt above 100 ms resets onset and
  returns zero; the caller must not accumulate catch-up time. `ResetSession()` resets the
  seeded path. `UpdateSettings()` validates and resets, leaving prior state intact on failure.
- `ActivationGate` starts disabled. After enabling, `Invalidate()`, or any suspended step,
  it requires an observed release while enabled and unsuspended, followed by a press.
  Pass the whole activation chord as `held`; release/repress applies to that chord.
- Serialize runtime calls on one owner thread. On profile changes, invalidate the gate and
  reset the generator. When activation stops, reset the generator and discard the consumer's
  fractional input-unit carry. Integer rounding/input injection belongs to the application.

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
    "arduinoPort": ""
  },
  "profiles": [
    {
      "id": "6d3433a2-a5dc-4596-b117-9cb56a18721e",
      "name": "Default",
      "color": "#60A5FA",
      "activation": "Lmb",
      "motion": {
        "x": 0, "y": 0, "humanization": 0,
        "maxComponentRate": 150, "model": "Smooth"
      }
    }
  ]
}
```

Original version-one fields are required. `inputBackend` and `arduinoPort` are optional when
reading older files and default to `Windows` and an empty string. Backend values are `Windows`
or `Arduino`; an Arduino selection requires a valid COM port name. Persisting a selection does
not itself open a connection or enable motion.

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
