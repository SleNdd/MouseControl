namespace MouseControl.Core;

/// <summary>
/// A single-owner activation state machine. Call Step with the complete activation chord.
/// After enabling, invalidating or suspending, it must observe a released chord before a press.
/// </summary>
public sealed class ActivationGate
{
    private bool _armed;

    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return;
        Enabled = enabled;
        Invalidate();
    }

    /// <summary>Call after editing/switching a profile or changing the activation binding.</summary>
    public void Invalidate() => _armed = false;

    public bool Step(bool held, bool suspended)
    {
        if (!Enabled || suspended)
        {
            Invalidate();
            return false;
        }
        if (!held)
        {
            _armed = true;
            return false;
        }
        return _armed;
    }
}
