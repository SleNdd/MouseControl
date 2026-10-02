# Third-party notices

MouseControl is developed by **SleNdd**.

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
