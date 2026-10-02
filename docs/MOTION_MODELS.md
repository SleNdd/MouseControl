# Motion models and timing

Author: **SleNdd**.

## Controls and composition

Horizontal and Vertical specify commanded relative input units per second, from −2000 to
+2000 on each axis. Humanization scales procedural variations from zero to full strength.
At zero, every model and effect combination uses the same smooth-onset straight baseline.
A zero X/Y vector remains stationary.

| Model | Behavior |
| --- | --- |
| Smooth | Slowly changing correlated speed and lateral variation. |
| Organic | Faster, stronger correlated variation. |
| Wind | Damped inertia responding to a changing directional push. |
| Pulse | Smooth bursts and coasting; full strength includes brief pauses. |
| Corrections | Slowly changing lateral arcs and speed modulation. |
| Bezier | Sequential randomly curved segments with filtered velocity. |

**Effects → Pulse envelope** and **Lateral corrections** can be combined with the selected
model. For example, Wind with both effects adds bursts and side arcs to an inertial baseline.
Pulse and Corrections already include their matching effect; selecting it again never doubles
the effect. A full-strength pulse pause stops both components, including added corrections.
The final output remains bounded by 2000 units/second per component.

## Parameter changes

Speed and direction changes are correlated over time. Their random targets and hold durations
evolve within bounded ranges; a fixed 100 ms target-update cycle is not used. Pulse cycles and
pause durations vary, and correction phase remains continuous as its period changes.
Each fresh activation obtains a new seed. A given seed and settings reproduce a trajectory.

Bezier adapts the cubic curve and one-sided random-anchor geometry from
[GhostCursorSharp](https://github.com/openbullet/GhostCursorSharp). It uses signed relative
segments, an injected seeded random source, direction-based anchor ordering and filtered
vector derivatives. This is a continuous-motion adaptation; it does not use upstream browser
automation or timestamp generation. Attribution and the MIT license are included in
[third-party notices](../THIRD_PARTY_NOTICES.md) and the executable's Settings dialog.

The integration grid is 200 Hz. Displacement is integrated across grid cells using the actual
elapsed time, independently of how the output timer partitions that time. A gap exceeding
100 ms resets the movement session and drops overdue movement.

## Output timing

Speed, integration, host output scheduling, and USB polling are separate quantities:

- Speed controls displacement per second.
- The integration grid evaluates the movement model.
- Requested output rate sets the host scheduler target: 125, 250, 500, or 1000 Hz.
- USB polling is determined by the device and its endpoint descriptors.

The Windows scheduler uses a cancellable waitable timer. The runtime measures elapsed time,
and does not enqueue missed reports for a later burst. The default host target is 1000 Hz.
Actual report timing depends on Windows and the selected backend. Arduino waits for an
acknowledgement before another command, so serial round-trip time can lower the effective rate.
Zero accumulated integer displacement produces no MOVE command.

These are procedural movement models. Their settings describe generated motion; they do
not measure or reproduce an individual person's movement characteristics.
