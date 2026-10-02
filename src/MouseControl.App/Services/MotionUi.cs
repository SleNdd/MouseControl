using MouseControl.Core;

namespace MouseControl.App.Services;

internal static class MotionUi
{
    public const double MaximumSpeed = 2000;

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

    public static string Describe(MotionModel model) => model.ToString() switch
    {
        "Smooth" => "Slow, smooth changes in speed and lateral direction.",
        "Organic" => "Stronger, faster variation in speed and lateral direction.",
        "Wind" => "Damped inertia with a slowly changing directional push.",
        "Pulse" => "Smooth bursts and coasting, with brief pauses at full strength.",
        "Corrections" => "Slow lateral arcs with changing speed and direction.",
        "Bezier" => "Random curved segments with smooth changes in velocity.",
        _ => "Procedural movement variation."
    };
}
