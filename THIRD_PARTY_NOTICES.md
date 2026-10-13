# Third-party notices

MouseControl is developed by **SleNdd**.

## NAudio audio playback

Desktop notification playback uses NAudio.Wasapi and NAudio.Core 2.3.0,
from [NAudio](https://github.com/naudio/NAudio/tree/v2.3.0), under the MIT license.
Copyright 2020 Mark Heath. The full notice is retained in
`src/MouseControl.App/ThirdParty/LICENSE.NAudio.txt`, embedded in the executable,
and displayed in **Settings → Third-party licenses**.

## GhostCursorSharp geometry

`src/MouseControl.Core/ThirdParty/GhostCursorBezier.cs` adapts the cubic Bézier geometry
and one-sided random-anchor method from
[GhostCursorSharp](https://github.com/openbullet/GhostCursorSharp),
commit `461c2f0949ba774f96339c27c2cacf776e718cfd`, `CursorPath.cs`.

Copyright (c) 2026 openbullet. Distributed under the MIT license; the full notice is in
`src/MouseControl.Core/ThirdParty/LICENSE.GhostCursorSharp.txt`.

The adaptation injects a seeded random source, preserves signed relative coordinates,
orders anchors along the movement direction, and exposes vector derivatives. MouseControl
uses it for sequential relative-motion segments with filtered velocity and its own effects
and output scheduler. Browser automation and upstream timestamp generation are not included.

## Original Ghost Cursor project

GhostCursorSharp describes itself as a .NET port of
[Xetera/ghost-cursor](https://github.com/Xetera/ghost-cursor). The original repository's
[`LICENSE`](https://github.com/Xetera/ghost-cursor/blob/master/LICENSE) states MIT and
attributes copyright to Xetera (2021). Its current [`package.json`](https://github.com/Xetera/ghost-cursor/blob/master/package.json)
declares `ISC`; this metadata conflict is unresolved. MouseControl adapts geometry from the
pinned GhostCursorSharp source above and does not include the original JavaScript or its
browser integration.

The original MIT notice is also retained in
`src/MouseControl.Core/ThirdParty/LICENSE.GhostCursor.txt`. Both MIT notices are embedded in
the executable and displayed in **Settings → Third-party license**.
