using MouseControl.Core.ThirdParty;

namespace MouseControl.Core;

/// <summary>
/// Seeded, bounded synthetic cursor motion with continuous component velocities.
/// One owner must serialize calls. The consumer must discard fractional input-unit carry when stopping.
/// </summary>
public sealed class MotionGenerator
{
    // A fixed integration grid makes results independent of caller timer partitioning.
    private const double Interval = 1.0 / 200;
    public const double MaximumStepSeconds = 0.1;
    private readonly int _seed;
    private Random _random;
    private Random _pulseRandom;
    private Random _correctionRandom;
    private Random _bezierRandom;
    private MotionSettings _settings;
    private long _cell;
    private double _cellOffset;
    private MotionDelta _start;
    private MotionDelta _end;
    private double _speedNoise;
    private double _sideNoise;
    private double _speedTarget;
    private double _sideTarget;
    private double _windSpeedRate;
    private double _windSideRate;
    private long _nextNoiseRefreshCell;
    private double _noiseHoldSeconds;
    private double _pulsePhase;
    private double _pulsePeriod;
    private double _pulsePauseFraction;
    private long _nextCorrectionRefreshCell;
    private double _correctionHoldSeconds;
    private double _correctionPhase;
    private double _correctionPeriod;
    private double _correctionPeriodTarget;
    private GhostCursorBezier _bezierCurve;
    private double _bezierElapsed;
    private double _bezierPeriod;
    private double _bezierSpeed;
    private double _bezierSide;

    public MotionGenerator(MotionSettings settings, int seed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _settings = settings;
        _seed = seed;
        _random = new Random(seed);
        _pulseRandom = new Random(unchecked(seed * 397 ^ 0x51ED270B));
        _correctionRandom = new Random(unchecked(seed * 397 ^ 0x2C1B3C6D));
        _bezierRandom = new Random(unchecked(seed * 397 ^ 0x6A09E667));
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
        _pulseRandom = new Random(unchecked(_seed * 397 ^ 0x51ED270B));
        _correctionRandom = new Random(unchecked(_seed * 397 ^ 0x2C1B3C6D));
        _bezierRandom = new Random(unchecked(_seed * 397 ^ 0x6A09E667));
        _cell = 0;
        _cellOffset = 0;
        _start = default;
        _speedNoise = _sideNoise = _speedTarget = _sideTarget = 0;
        _windSpeedRate = _windSideRate = 0;
        _nextNoiseRefreshCell = _nextCorrectionRefreshCell = 0;
        _noiseHoldSeconds = _correctionHoldSeconds = 0;
        _pulsePhase = _pulsePeriod = _pulsePauseFraction = 0;
        _correctionPhase = _correctionPeriod = _correctionPeriodTarget = 0;
        _bezierCurve = default;
        _bezierElapsed = _bezierPeriod = _bezierSide = 0;
        _bezierSpeed = 1;
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
        double speed = 1, side = 0;
        if (h > 0)
        {
            switch (_settings.Model)
            {
                case MotionModel.Smooth:
                    FilterNoise(0.08, 0.22, timeConstant: 0.10);
                    speed += h * 0.55 * _speedNoise;
                    side = h * 0.45 * _sideNoise;
                    break;
                case MotionModel.Organic:
                    FilterNoise(0.035, 0.15, timeConstant: 0.045, maximumNoiseRate: 10);
                    speed += h * 0.85 * _speedNoise;
                    side = h * 0.80 * _sideNoise;
                    break;
                case MotionModel.Wind:
                    DrawNoiseTargets(0.15, 0.50);
                    AdvanceWind(ref _speedNoise, ref _windSpeedRate, _speedTarget);
                    AdvanceWind(ref _sideNoise, ref _windSideRate, _sideTarget);
                    speed += h * 0.75 * _speedNoise;
                    side = h * 0.90 * _sideNoise;
                    break;
                case MotionModel.Pulse:
                    FilterNoise(0.08, 0.22, timeConstant: 0.10);
                    side = h * 0.45 * _sideNoise;
                    break;
                case MotionModel.Corrections:
                    break;
                case MotionModel.Bezier:
                    AdvanceBezier(h);
                    speed = 1 + h * (_bezierSpeed - 1);
                    side = _bezierSide;
                    break;
            }
            if (_settings.Model == MotionModel.Corrections || _settings.CorrectionsOverlay)
            {
                AdvanceCorrections();
                var angle = 2 * Math.PI * _correctionPhase;
                speed += h * 0.45 * Math.Cos(angle);
                side += h * 0.90 * Math.Sin(angle);
            }
            if (_settings.Model == MotionModel.Pulse || _settings.PulseOverlay)
            {
                var envelope = AdvancePulse();
                var multiplier = 1 - h + h * envelope;
                speed *= multiplier;
                side *= multiplier;
            }
        }
        var x = _settings.X;
        var y = _settings.Y;
        var onset = 1 - Math.Exp(-(_cell + 1) * Interval / 0.16);
        var rate = _settings.MaxComponentRate;
        // Variation scales with the commanded vector, not an absolute rate floor. Nonzero
        // commanded axes retain their sign; a zero component may have lateral variation.
        return new MotionDelta(
            BoundComponent(x * speed - y * side, x) * onset * rate,
            BoundComponent(y * speed + x * side, y) * onset * rate);
    }

    private void FilterNoise(double minimumHold, double maximumHold, double timeConstant,
        double maximumNoiseRate = double.PositiveInfinity)
    {
        DrawNoiseTargets(minimumHold, maximumHold);
        var blend = 1 - Math.Exp(-Interval / timeConstant);
        var maximumChange = maximumNoiseRate * Interval;
        _speedNoise += Math.Clamp((_speedTarget - _speedNoise) * blend, -maximumChange, maximumChange);
        _sideNoise += Math.Clamp((_sideTarget - _sideNoise) * blend, -maximumChange, maximumChange);
    }

    private void DrawNoiseTargets(double minimumHold, double maximumHold)
    {
        if (_cell < _nextNoiseRefreshCell) return;
        _speedTarget = _random.NextDouble() * 2 - 1;
        _sideTarget = _random.NextDouble() * 2 - 1;
        _noiseHoldSeconds = Evolve(_random, _noiseHoldSeconds, minimumHold, maximumHold);
        _nextNoiseRefreshCell = _cell + (long)Math.Ceiling(_noiseHoldSeconds / Interval);
    }

    private static void AdvanceWind(ref double value, ref double changeRate, double target)
    {
        // Damped second-order response: q'' = 36(target-q) - 10q'. Fixed 5ms updates
        // keep the spring stable and avoid frame-rate-dependent random forcing.
        changeRate += (36 * (target - value) - 10 * changeRate) * Interval;
        value += changeRate * Interval;
        var bounded = Math.Clamp(value, -1, 1);
        if (bounded != value) changeRate = 0;
        value = bounded;
    }

    private double AdvancePulse()
    {
        if (_pulsePeriod == 0)
        {
            _pulsePeriod = Evolve(_pulseRandom, 0, 0.50, 1.00);
            _pulsePauseFraction = Evolve(_pulseRandom, 0, 0.12, 0.24);
        }
        _pulsePhase += Interval / _pulsePeriod;
        if (_pulsePhase >= 1)
        {
            // Carry sub-cell seconds; period/pause changes happen where the envelope and
            // its first derivative are zero, keeping velocity continuous across cycles.
            var remainingSeconds = (_pulsePhase - 1) * _pulsePeriod;
            _pulsePeriod = Evolve(_pulseRandom, _pulsePeriod, 0.50, 1.00);
            _pulsePauseFraction = Evolve(_pulseRandom, _pulsePauseFraction, 0.12, 0.24);
            _pulsePhase = remainingSeconds / _pulsePeriod;
        }
        var activeFraction = 1 - _pulsePauseFraction;
        return _pulsePhase < activeFraction
            ? 1.80 * Math.Pow(Math.Sin(Math.PI * _pulsePhase / activeFraction), 2)
            : 0;
    }

    private void AdvanceCorrections()
    {
        if (_cell >= _nextCorrectionRefreshCell)
        {
            _correctionHoldSeconds = Evolve(_correctionRandom, _correctionHoldSeconds, 0.18, 0.70);
            _nextCorrectionRefreshCell = _cell + (long)Math.Ceiling(_correctionHoldSeconds / Interval);
            _correctionPeriodTarget = Evolve(_correctionRandom, _correctionPeriodTarget, 0.80, 1.40);
        }
        if (_correctionPeriod == 0) _correctionPeriod = _correctionPeriodTarget;
        _correctionPeriod += (_correctionPeriodTarget - _correctionPeriod) * (1 - Math.Exp(-Interval / 0.35));
        _correctionPhase += Interval / _correctionPeriod;
        _correctionPhase -= Math.Floor(_correctionPhase);
    }

    private void AdvanceBezier(double humanization)
    {
        var x = _settings.X;
        var y = _settings.Y;
        var magnitudeSquared = x * x + y * y;
        if (magnitudeSquared == 0) return;
        if (_bezierPeriod == 0) BeginBezierSegment(humanization);
        _bezierElapsed += Interval;
        if (_bezierElapsed >= _bezierPeriod)
        {
            _bezierElapsed -= _bezierPeriod;
            BeginBezierSegment(humanization);
        }
        var derivative = _bezierCurve.Derivative(_bezierElapsed / _bezierPeriod);
        var dx = derivative.Dx / _bezierPeriod;
        var dy = derivative.Dy / _bezierPeriod;
        var speedTarget = (dx * x + dy * y) / magnitudeSquared;
        var sideTarget = (dy * x - dx * y) / magnitudeSquared;
        var blend = 1 - Math.Exp(-Interval / 0.10);
        // The geometric derivative can jump at a new segment. Keep the filtered velocity
        // continuous and bound its change, including the boundary between signed segments.
        _bezierSpeed += Math.Clamp((speedTarget - _bezierSpeed) * blend, -10 * Interval, 10 * Interval);
        _bezierSide += Math.Clamp((sideTarget - _bezierSide) * blend, -10 * Interval, 10 * Interval);
    }

    private void BeginBezierSegment(double humanization)
    {
        _bezierPeriod = Evolve(_bezierRandom, _bezierPeriod, 0.45, 1.20);
        var end = new MotionDelta(_settings.X * _bezierPeriod, _settings.Y * _bezierPeriod);
        var magnitude = Math.Sqrt(end.Dx * end.Dx + end.Dy * end.Dy);
        _bezierCurve = GhostCursorBezier.Create(_bezierRandom, end, 0.60 * magnitude * humanization);
    }

    private static double Evolve(Random random, double current, double minimum, double maximum)
    {
        var target = minimum + random.NextDouble() * (maximum - minimum);
        // Hold durations drift toward successive seeded targets rather than following a
        // constant cadence or jumping independently across the entire allowed range.
        return current == 0 ? target : current + 0.35 * (target - current);
    }

    private static double BoundComponent(double value, double commanded) => commanded switch
    {
        > 0 => Math.Clamp(value, 0, 1),
        < 0 => Math.Clamp(value, -1, 0),
        _ => Math.Clamp(value, -1, 1)
    };
}
