using MouseControl.Core;

namespace MouseControl.App.Services;

internal static class MotionUi
{
    public const double MaximumSpeed = 2000;
    public static IReadOnlyList<MotionModel> Models { get; } = new[] { MotionModel.Classic, MotionModel.Bezier };

    // Older profiles stored percentages of an editable rate. Keep their commanded
    // velocities while exposing the full range with one-unit numeric increments.
    public static ProfileConfiguration UseSpeedUnits(ProfileConfiguration source, out bool changed)
    {
        source.Validate();
        changed = source.Profiles.Any(p => p.Motion.MaxComponentRate != MaximumSpeed);
        if (!changed) return source;
        return source with
        {
            Profiles = source.Profiles.Select(p => p with
            {
                Motion = p.Motion with
                {
                    X = p.Motion.X * p.Motion.MaxComponentRate / MaximumSpeed,
                    Y = p.Motion.Y * p.Motion.MaxComponentRate / MaximumSpeed,
                    MaxComponentRate = MaximumSpeed
                }
            }).ToArray()
        };
    }

    public static ProfileConfiguration UseSimpleModels(ProfileConfiguration source, out bool changed)
    {
        source.Validate();
        var profiles = source.Profiles.Select(profile => profile with
        {
            Motion = profile.Motion.Model switch
            {
                MotionModel.Classic or MotionModel.Bezier => profile.Motion,
                MotionModel.Pulse => profile.Motion with { Model = MotionModel.Classic, PulseOverlay = true },
                MotionModel.Corrections => profile.Motion with { Model = MotionModel.Classic, CorrectionsOverlay = true },
                _ => profile.Motion with { Model = profile.Motion.Humanization == 0 ? MotionModel.Classic : MotionModel.Bezier }
            }
        }).ToArray();
        changed = !profiles.SequenceEqual(source.Profiles);
        return changed ? source with { Profiles = profiles } : source;
    }

    public static string Describe(MotionModel model) => model == MotionModel.Classic
        ? "Straight movement. Pulse and Nonlinearity are optional."
        : "Random curved segments with smooth changes in velocity.";
}
