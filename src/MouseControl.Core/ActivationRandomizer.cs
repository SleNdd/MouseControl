namespace MouseControl.Core;

/// <summary>Samples immutable effective parameters once for an accepted activation hold.</summary>
public static class ActivationRandomizer
{
    public static MotionSettings Sample(MotionSettings settings, int seed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (!settings.RandomizeOnActivation) return settings;

        // A private stream leaves MotionGenerator's seeded motion sequence unchanged.
        var random = new Random(seed);
        var x = SampleAxis(settings.X, settings.HorizontalSpread, settings.MaxComponentRate, random);
        var y = SampleAxis(settings.Y, settings.VerticalSpread, settings.MaxComponentRate, random);
        var humanization = settings.HumanizationSpread == 0 ? settings.Humanization
            : settings.Humanization + random.NextDouble() *
                (Math.Min(1, settings.Humanization + settings.HumanizationSpread / 100) - settings.Humanization);
        return settings with { X = x, Y = y, Humanization = humanization };
    }

    private static double SampleAxis(double value, double spread, double maximum, Random random)
    {
        if (spread == 0) return value;
        var minimum = Math.Max(-maximum, value * maximum - spread);
        var upper = Math.Min(maximum, value * maximum + spread);
        return (minimum + random.NextDouble() * (upper - minimum)) / maximum;
    }
}
