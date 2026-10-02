namespace MouseControl.Core;

/// <summary>
/// Seeded, bounded synthetic cursor motion; these models do not establish human equivalence.
/// One owner must serialize calls. The consumer must discard fractional input-unit carry when stopping.
/// </summary>
public sealed class MotionGenerator
{
    // A fixed integration grid makes results independent of caller timer partitioning.
    private const double Interval = 1.0 / 200;
    public const double MaximumStepSeconds = 0.1;
    private readonly int _seed;
    private Random _random;
    private MotionSettings _settings;
    private long _cell;
    private double _cellOffset;
    private MotionDelta _start;
    private MotionDelta _end;
    private double _speedNoise;
    private double _sideNoise;
    private double _speedTarget;
    private double _sideTarget;

    public MotionGenerator(MotionSettings settings, int seed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _settings = settings;
        _seed = seed;
        _random = new Random(seed);
        ResetSession();
    }

    public void UpdateSettings(MotionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _settings = settings;
        ResetSession();
    }

    /// <summary>Resets onset, noise, and timing; a given seed reproduces each fresh session.</summary>
    public void ResetSession()
    {
        _random = new Random(_seed);
        _cell = 0;
        _cellOffset = 0;
        _start = default;
        _speedNoise = _sideNoise = _speedTarget = _sideTarget = 0;
        _end = NextVelocity();
    }

    /// <summary>
    /// Returns integrated displacement for elapsed seconds. Nonpositive intervals return zero.
    /// A gap above 100 ms resets onset and returns zero, with no accumulated catch-up movement.
    /// </summary>
    public MotionDelta Step(double dtSeconds)
    {
        if (!double.IsFinite(dtSeconds))
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (dtSeconds <= 0)
            return default;
        if (dtSeconds > MaximumStepSeconds)
        {
            ResetSession();
            return default;
        }
        if (_settings.X == 0 && _settings.Y == 0)
            return default;

        double dx = 0, dy = 0;
        var remaining = dtSeconds;
        while (remaining > 1e-14)
        {
            var slice = Math.Min(remaining, Interval - _cellOffset);
            var from = _cellOffset / Interval;
            var to = (_cellOffset + slice) / Interval;
            var midpoint = (from + to) / 2;
            dx += (_start.Dx + (_end.Dx - _start.Dx) * midpoint) * slice;
            dy += (_start.Dy + (_end.Dy - _start.Dy) * midpoint) * slice;
            remaining -= slice;
            _cellOffset += slice;
            if (_cellOffset >= Interval - 1e-14)
            {
                _cell++;
                _cellOffset = 0;
                _start = _end;
                _end = NextVelocity();
            }
        }
        return new MotionDelta(dx, dy);
    }

    private MotionDelta NextVelocity()
    {
        var h = _settings.Humanization;
        if (h > 0)
        {
            if (_cell % 20 == 0)
            {
                _speedTarget = _random.NextDouble() * 2 - 1;
                _sideTarget = _random.NextDouble() * 2 - 1;
            }
            var blend = 1 - Math.Exp(-Interval / (_settings.Model == MotionModel.Smooth ? 0.14 : 0.08));
            _speedNoise += (_speedTarget - _speedNoise) * blend;
            _sideNoise += (_sideTarget - _sideNoise) * blend;
        }
        var x = _settings.X;
        var y = _settings.Y;
        var speed = 1 + h * 0.25 * _speedNoise;
        var side = h * (_settings.Model == MotionModel.Smooth ? 0.12 : 0.25) * _sideNoise;
        var onset = 1 - Math.Exp(-(_cell + 1) * Interval / 0.16);
        var rate = _settings.MaxComponentRate;
        // Clamp the normalized components before multiplying, avoiding overflow for finite rates.
        return new MotionDelta(
            Math.Clamp(x * speed - y * side, -1, 1) * onset * rate,
            Math.Clamp(y * speed + x * side, -1, 1) * onset * rate);
    }
}
