# Application services

## Runtime

`RuntimeService` starts disabled and owns its gate, motion generator, fractional relative
input-unit carry, and submission under one lock. `SetEnabled(false)` returns only after any
current submission finishes; no later submission is queued. The dedicated worker measures
actual monotonic elapsed time. Gaps over 100 ms invalidate activation and clear motion/carry. Editing,
switching profiles, suspending, and resuming require a fresh release/press.

`RequestedOutputRateHz` is the requested host cadence: 125, 250, 500 or 1000 Hz, default 1000.
`SetOutputRateHz` validates these choices; a changed rate disables motion and clears the session
under the output lock. Re-enabling still requires a fresh release/press. Passing the same rate
does nothing; an invalid rate throws without changing the current session. The optional public
constructor `outputRateHz` argument applies a saved rate before the worker starts.

`OutputScheduler` uses an unnamed Windows waitable timer with
`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION` (Windows 10 version 1803 and later), falling back to a
normal waitable timer if that creation flag is unavailable. Each wait is one-shot and cancellation
wakes the blocked worker. The worker owns and closes its timer after exiting. It uses no busy spin,
`timeBeginPeriod`, driver, administrator privilege or timer callback queue. The portable service
harness uses a cancellable blocking wait outside Windows. Processing consumes the interval;
if processing has already missed the next deadline, the scheduler starts a fresh interval and
drops the missed deadlines. It never queues catchup movement or submits anything while idle.

These are requested rates, not guaranteed delivery rates. Effective cadence depends on Windows
scheduling and timer availability, nonzero integer deltas, and the selected transport. Arduino
commands wait for ACKs serially, so that backpressure can lower the achieved rate. A full-speed
Leonardo USB HID endpoint can poll at most once per 1 ms interval (1000 Hz); selecting 1000 Hz
does not establish that every host tick delivers a USB report or a target application event.
The waitable timer API is documented by Microsoft in
[CreateWaitableTimerExW](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw)
and [SetWaitableTimer](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-setwaitabletimer).

Each fresh active transition constructs a generator with a new seed from `Random.Shared`.
When `RandomizeOnActivation` is enabled, `ActivationRandomizer.Sample` uses that seed to
choose the hold's effective settings before constructing the generator. That immutable set
is retained until release; stopping clears it. `EffectiveMotionSettings` exposes the active
set under the state lock, or the saved base settings when inactive. Sampling does not mutate
profiles, consume an additional hold seed or change disabled-randomization output.
An optional seed provider supports deterministic development checks. Normal status strings
are `Idle`, `Active`, `Paused`, and `Disabled`.

The runtime pauses while its own process is foreground or `SetSuspended(true)` is in force.
Logical L/R activation honors swapped buttons; Alt and Shift use generic modifier state.
Windows submission is a single documented relative `SendInput` MOVE event. Arduino submission
uses an identified Leonardo connection and the standard Arduino Mouse library. A failed
submission disables motion and surfaces a status. Simulation routes deltas to the optional
callback and never calls `SendInput` or opens serial.

Runtime public methods are thread-safe. `StatusChanged` may originate on the worker thread;
observers run outside the state lock and cannot terminate the worker by throwing. Dispatch
to the UI thread, then read `Status` and `Enabled` to avoid displaying a superseded snapshot.
`Dispose()` disables and joins the worker; a callback on that worker cannot join itself, but
marks it disposed so it immediately exits without further submission.
Setters on a terminal/disposed runtime safely do nothing; they do not silently restart it.

## Input backends

`InputBackendKind` is `Windows` or `Arduino`. Runtime exposes `BackendKind`, `BackendPort`,
`BackendConnected`, and a `BackendChanged` notification. Read current properties after
dispatching the notification onto the UI thread.

`SetBackend(kind, port, out error)` explicitly selects/connects a backend. Every attempt
disables motion, clears fractional carry, and stops the active session first. Success replaces
and disposes the previous connection. Failure preserves the previous backend, with motion
disabled. A successful connection remains disabled until explicitly enabled and freshly held.

`SelectBackendDisconnected(kind, port, out error)` restores Arduino selection or handles
device removal without opening serial. It retains Arduino kind/port with Connected false,
so it cannot fall back to Windows output. Enabling an offline backend is refused.
`TryReconnectArduino(port, out error)` reconnects only the still-selected offline Arduino
endpoint. Queued attempts for a different/currently replaced selection are discarded; an
already-connected matching endpoint is left untouched.

Runtime opens only the supplied COM port; it neither enumerates ports nor runs a heartbeat.
The serial client uses a 500 ms identification deadline and 150 ms command deadline, with
100 ms bounded writes. CRC16 frames carry a connection nonce and increasing sequence. Only
one command is outstanding; each MOVE chunk waits for its ACK before the next chunk. Relative
components are segmented to ±127, with at most two chunks per tick. STOP is an acknowledged
ordering barrier. Disabling and backend switching serialize with these operations.

Before identification, a newly opened connection holds DTR low for 300 ms at 115200 baud,
clears both buffers, then raises DTR. This gives the firmware loop time to discard its previous
session, including a prior USB-send wait. It does not use Leonardo's 1200-baud bootloader touch.

A failed/uncertain Arduino command closes the connection, disables output, and retains the
offline Arduino selection. It is never retransmitted automatically: a missing ACK cannot
establish whether an already-written MOVE executed. A firmware ACK reports command handling,
not target-application processing. See the [firmware setup and protocol](../../../firmware/MouseControlLeonardo/README.md).

## Window integration

`WindowBridge` must be created, called, and disposed on the HWND owner thread. It owns the
rooted subclass delegate, hotkey registrations, WTS registration, and tray icon. The shared
IDI_APPLICATION icon is system-owned; a separately loaded mask icon is destroyed on disposal.
Menus are destroyed in a finally block. Global
hotkey updates register a distinct candidate with MOD_NOREPEAT before removing the old one;
failure preserves the old binding. Tray menus publish suspension on both open and close.
Vertical bindings use a separate pair of candidate IDs; both candidates must register before
either previous mapping is replaced. Registration failure removes candidates and leaves the
previous pair active. Duplicate toggle/vertical chords are rejected.
Session lock/disconnect and power suspension publish suspension; resume requires fresh input.
Explorer restart recreates the icon. `Hide()` refuses when the tray is unavailable.
Native visibility requests perform the change and then publish `ShowRequested`/`HideRequested`
notifications. Public `Show()`/`Hide()` do not emit those events recursively.

Availability failures are exposed through `AvailabilityError` and `AvailabilityChanged`;
the UI must display them. `TrayAvailable` and `HotkeyAvailable` describe current integration.
Native tray, hotkey, session, and input integration require separate Windows verification.

## Desktop feedback

`DesktopFeedbackService` owns transient layered notification windows on the UI thread.
On/Off messages include the signed vertical value; their sound kind is selected before
formatting that display text. The windows do not activate or intercept mouse input.

`DesktopStatusOverlay` owns a separate unowned, topmost, click-through label. Its 9-DIP text
has a dark outline, 20% opacity and a 2-DIP margin from the primary display's top-left corner.
The persisted `PersistentStatusOverlay` option defaults to false. The UI updates its text
from `EffectiveMotionSettings` when runtime status or profile values change, so active
randomized holds display their selected vertical speed. Hiding the main window does not
hide the label; disabling the option hides it. Disposal destroys its native window and font.

## Development checks

`DesktopFeedbackSound` uses one worker-owned shared-mode WASAPI output and one PCM voice. A new event
fades the current voice to zero over 6 ms, then starts the latest requested cue. Requests
during that fade replace the pending cue without extending the fade. Idle output is silent.
PCM generation has smooth attack/release and a continuous frequency glide. The worker owns
device initialization, recovery and disposal; the UI only posts a latest-event request.
The audio regression checks use rendered PCM and fake devices without opening speakers.
To additionally play a short rapid sequence through the default Windows audio device and
check playback progress and callback shutdown, append `-- --audio-smoke` to the command below.

The service harness is excluded by the application's hidden-directory source glob:

```powershell
dotnet run --project src/MouseControl.App/Services/.checks/ServiceChecks.csproj -c Release
```

The service harness contains 31 checks. The separate firmware protocol checks are under
`firmware/MouseControlLeonardo/.checks`; AVR compilation and physical-device behavior are not
covered by this host harness.
