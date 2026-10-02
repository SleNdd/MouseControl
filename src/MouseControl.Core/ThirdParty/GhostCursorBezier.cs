// Geometry adapted from GhostCursorSharp, Copyright (c) 2026 openbullet.
// MIT license: see LICENSE.GhostCursorSharp.txt and THIRD_PARTY_NOTICES.md.
// Pinned source: 461c2f0949ba774f96339c27c2cacf776e718cfd, CursorPath.cs.
// Local adaptation: injected seeded RNG, signed relative coordinates, anchor ordering
// along the direction of travel, and vector derivatives for continuous velocity use.

namespace MouseControl.Core.ThirdParty;

internal readonly record struct GhostCursorBezier(
    MotionDelta Start, MotionDelta Control1, MotionDelta Control2, MotionDelta End)
{
    public static GhostCursorBezier Create(Random random, MotionDelta end, double spread)
    {
        var magnitude = Math.Sqrt(end.Dx * end.Dx + end.Dy * end.Dy);
        if (magnitude == 0) return default;
        var normal = new MotionDelta(end.Dy / magnitude, -end.Dx / magnitude);
        var side = random.Next(2) == 1 ? 1 : -1;
        var progress1 = random.NextDouble();
        var progress2 = random.NextDouble();
        var offset1 = random.NextDouble() * spread * side;
        var offset2 = random.NextDouble() * spread * side;
        var first = new MotionDelta(end.Dx * progress1 + normal.Dx * offset1, end.Dy * progress1 + normal.Dy * offset1);
        var second = new MotionDelta(end.Dx * progress2 + normal.Dx * offset2, end.Dy * progress2 + normal.Dy * offset2);
        return progress1 <= progress2
            ? new(default, first, second, end)
            : new(default, second, first, end);
    }

    public MotionDelta At(double t)
    {
        var inverse = 1 - t;
        var a = inverse * inverse * inverse;
        var b = 3 * inverse * inverse * t;
        var c = 3 * inverse * t * t;
        var d = t * t * t;
        return new(a * Start.Dx + b * Control1.Dx + c * Control2.Dx + d * End.Dx,
                   a * Start.Dy + b * Control1.Dy + c * Control2.Dy + d * End.Dy);
    }

    public MotionDelta Derivative(double t)
    {
        var inverse = 1 - t;
        var a = 3 * inverse * inverse;
        var b = 6 * inverse * t;
        var c = 3 * t * t;
        return new(a * (Control1.Dx - Start.Dx) + b * (Control2.Dx - Control1.Dx) + c * (End.Dx - Control2.Dx),
                   a * (Control1.Dy - Start.Dy) + b * (Control2.Dy - Control1.Dy) + c * (End.Dy - Control2.Dy));
    }
}
