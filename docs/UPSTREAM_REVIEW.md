# Ghost Cursor upstream comparison

Reviewed on 2026-10-03. This compares the original Xetera/ghost-cursor source, the pinned
GhostCursorSharp port, and MouseControl's local geometry and motion generator.

## Source revisions

- The GhostCursorSharp source used for the local geometry is commit
  [`461c2f0949ba774f96339c27c2cacf776e718cfd`](https://github.com/openbullet/GhostCursorSharp/tree/461c2f0949ba774f96339c27c2cacf776e718cfd),
  dated 2026-05-14. The relevant file is
  [`CursorPath.cs`](https://github.com/openbullet/GhostCursorSharp/blob/461c2f0949ba774f96339c27c2cacf776e718cfd/src/GhostCursorSharp.Core/CursorPath.cs).
- The latest original-repository revision observed during this review is
  [`5525a783fcf833a60f1b0746a8f80653ccfee2ba`](https://github.com/Xetera/ghost-cursor/commit/5525a783fcf833a60f1b0746a8f80653ccfee2ba),
  dated 2026-08-04; its subject concerns `scrollTo` accepting an `ElementHandle`.
- The relevant upstream timestamp correction is
  [`1f6328882b23960c54b54122497c741598d71077`](https://github.com/Xetera/ghost-cursor/commit/1f6328882b23960c54b54122497c741598d71077),
  dated 2025-07-22, titled “fix: use i+2 to P3”. The original source history for
  [`math.ts`](https://github.com/Xetera/ghost-cursor/commits/master/src/math.ts) shows no later
  geometry rewrite in the history checked for this review.

## What the port and local adaptation contain

The pinned C# `CursorPath` includes cubic Bézier evaluation, random one-sided anchors,
Fitts-based path sampling, and optional timestamps. Its timestamp calculation uses the
corrected `i + 2` point for `P3`, so that upstream fix is already present in the pinned port.
MouseControl does not generate timestamped point paths, so this correction has no direct
counterpart to add to the local generator.

MouseControl's [`GhostCursorBezier.cs`](../src/MouseControl.Core/ThirdParty/GhostCursorBezier.cs)
adapts only the cubic curve and one-sided random-anchor geometry. It injects a seeded random
source, uses signed relative coordinates, orders the control anchors by progress along the
movement vector, and exposes the vector derivative. The generator uses those curves in
successive relative segments whose durations evolve between 0.45 and 1.20 seconds. It projects
the curve derivative onto speed and lateral components, then filters and bounds changes in
velocity. A fixed 200 Hz integration grid makes the generated displacement independent of
how the caller partitions its timer intervals.

## Geometry comparison and fix status

Both original `src/math.ts` and the pinned port order the two random control anchors by their
absolute `x` coordinate. This is not a reliable ordering along a path: for movement toward
decreasing `x`, it can reverse the anchors' travel order; for vertical travel, `x` values do
not determine progress. The local adaptation instead orders anchors by their scalar progress
along the commanded movement vector. That fixes this ordering issue in the local implementation.
The source history checked here shows no later upstream geometry fix missing from the port or
the local adaptation.

## Different path and timing contracts

Original Ghost Cursor's `path` API takes a start and a destination point or target box. It
creates a finite point lookup table, chooses its sampling count using path length, target width,
Fitts' law, and `moveSpeed`, and can attach timestamps to each point. Its cursor class adds
browser-specific target selection, overshoot and correction, retries when an element moves,
and Puppeteer/CDP dispatch. These behaviors are documented in the original
[`src/spoof.ts`](https://github.com/Xetera/ghost-cursor/blob/master/src/spoof.ts) and
[`README`](https://github.com/Xetera/ghost-cursor).

MouseControl generates a continuous stream from configured relative-axis rates. It has no
destination, target box, or finite path endpoint from which to derive Fitts timing or target
overshoot. The browser path API and its timestamped lookup-table schedule therefore describe a
different contract from the local relative-motion stream. They should only be reconsidered if
MouseControl adds a separate, target-aware finite-movement API.

## License metadata

The original repository's `LICENSE` says MIT and identifies Xetera with copyright year 2021,
while its `package.json` declares `ISC`. The reviewed repository artifacts do not explain this
conflict. The pinned GhostCursorSharp commit has its own MIT license and copyright notice for
openbullet; MouseControl retains that notice for the code it adapts. The original project's
license metadata discrepancy remains unresolved; see [third-party notices](../THIRD_PARTY_NOTICES.md).

## Limits of this review

This is a source comparison, not a behavioral equivalence or motor-control study. It does not
establish that generated movement resembles any particular person or assess detection outcomes.
The “latest” original revision above means the repository tip observed on the review date;
upstream may have changed since then.
