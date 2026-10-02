using System.Text.Json.Serialization;

namespace MouseControl.Core;

public enum MotionModel
{
    Smooth = 0,
    Organic = 1,
    Wind = 2,
    Pulse = 3,
    Corrections = 4,
    Bezier = 5,
    Classic = 6
}

/// <summary>Immutable cursor motion parameters. Components are normalized; rates use relative input units/second.</summary>
public sealed record MotionSettings(
    [property: JsonRequired] double X = 0,
    [property: JsonRequired] double Y = 0,
    [property: JsonRequired] double Humanization = 0,
    [property: JsonRequired] double MaxComponentRate = 150,
    [property: JsonRequired] MotionModel Model = MotionModel.Classic,
    bool PulseOverlay = false,
    bool CorrectionsOverlay = false)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || X is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(X));
        if (!double.IsFinite(Y) || Y is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Y));
        if (!double.IsFinite(Humanization) || Humanization is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Humanization));
        if (!double.IsFinite(MaxComponentRate) || MaxComponentRate is <= 0 or > 2000)
            throw new ArgumentOutOfRangeException(nameof(MaxComponentRate));
        if (!Enum.IsDefined(Model))
            throw new ArgumentOutOfRangeException(nameof(Model));
    }
}

public readonly record struct MotionDelta(double Dx, double Dy);
