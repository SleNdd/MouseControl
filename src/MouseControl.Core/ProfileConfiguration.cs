using System.Text.Json.Serialization;

namespace MouseControl.Core;

public enum ActivationBinding { Lmb, Rmb, LmbAndRmb, Mmb, Alt, Shift }
public enum InputBackendKind { Windows, Arduino }

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

public sealed record GlobalHotkey(
    [property: JsonRequired] int VirtualKey,
    [property: JsonRequired] HotkeyModifiers Modifiers);

public sealed record AppSettings(
    [property: JsonRequired] GlobalHotkey ToggleHotkey,
    [property: JsonRequired] Guid ActiveProfileId,
    InputBackendKind InputBackend = InputBackendKind.Windows,
    string ArduinoPort = "",
    int OutputRateHz = 1000,
    bool StartInTray = false,
    GlobalHotkey? VerticalIncreaseHotkey = null,
    GlobalHotkey? VerticalDecreaseHotkey = null,
    int VerticalStep = 50,
    bool CloseToTray = true,
    bool SoundNotifications = true,
    bool DesktopNotifications = true,
    bool SuppressDonationPrompt = false,
    bool PersistentStatusOverlay = false)
{
    [JsonIgnore] public GlobalHotkey EffectiveVerticalIncreaseHotkey => VerticalIncreaseHotkey ?? new(0x26, HotkeyModifiers.Control | HotkeyModifiers.Shift);
    [JsonIgnore] public GlobalHotkey EffectiveVerticalDecreaseHotkey => VerticalDecreaseHotkey ?? new(0x28, HotkeyModifiers.Control | HotkeyModifiers.Shift);
}

public sealed record Profile(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Color,
    [property: JsonRequired] ActivationBinding Activation,
    [property: JsonRequired] MotionSettings Motion);

/// <summary>
/// Version 1 stores global settings and profiles together so selection changes are atomic.
/// Runtime enabled/armed state is intentionally absent. Profile names need not be unique.
/// </summary>
public sealed record ProfileConfiguration(
    [property: JsonRequired] int Version,
    [property: JsonRequired] AppSettings AppSettings,
    [property: JsonRequired] IReadOnlyList<Profile> Profiles)
{
    public const int CurrentVersion = 1;

    public static ProfileConfiguration CreateDefault()
    {
        var id = Guid.NewGuid();
        return new(CurrentVersion,
            new(new(0x77, HotkeyModifiers.None), id), // F8
            new[] { new Profile(id, "Default", "#60A5FA", ActivationBinding.Lmb, new(Humanization: .6)) });
    }

    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new ArgumentException($"Unsupported configuration version {Version}.");
        if (AppSettings?.ToggleHotkey is null)
            throw new ArgumentException("Global settings and toggle hotkey are required.");
        if (AppSettings.ToggleHotkey.VirtualKey is < 1 or > 254)
            throw new ArgumentException("Toggle hotkey must be a Windows virtual-key code from 1 to 254.");
        if (!Enum.IsDefined(AppSettings.InputBackend))
            throw new ArgumentException("Input backend is invalid.");
        if (!IsSupportedOutputRate(AppSettings.OutputRateHz))
            throw new ArgumentException("Output rate must be 125, 250, 500, or 1000 Hz.");
        if (AppSettings.ArduinoPort is null || (AppSettings.ArduinoPort.Length != 0 && !IsArduinoPortName(AppSettings.ArduinoPort)))
            throw new ArgumentException("Arduino port must be a COM port name.");
        if (AppSettings.InputBackend == InputBackendKind.Arduino && AppSettings.ArduinoPort.Length == 0)
            throw new ArgumentException("Select an Arduino COM port.");
        const HotkeyModifiers all = HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win;
        if ((AppSettings.ToggleHotkey.Modifiers & ~all) != 0)
            throw new ArgumentException("Toggle hotkey contains unsupported modifiers.");
        foreach (var hotkey in new[] { AppSettings.EffectiveVerticalIncreaseHotkey, AppSettings.EffectiveVerticalDecreaseHotkey })
            if (hotkey.VirtualKey is < 1 or > 254 || (hotkey.Modifiers & ~all) != 0)
                throw new ArgumentException("Vertical hotkeys require valid Windows keys and modifiers.");
        if (AppSettings.VerticalStep is < 1 or > 2000)
            throw new ArgumentException("Vertical step must be between 1 and 2000 input units/s.");
        if (Profiles is null || Profiles.Count == 0)
            throw new ArgumentException("At least one profile is required.");
        var ids = new HashSet<Guid>();
        foreach (var profile in Profiles)
        {
            if (profile is null || profile.Id == Guid.Empty || !ids.Add(profile.Id))
                throw new ArgumentException("Profiles require unique, nonempty identifiers.");
            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new ArgumentException("Profile name cannot be empty.");
            if (profile.Color is not { Length: 7 } || profile.Color[0] != '#' ||
                profile.Color.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEFabcdef") >= 0)
                throw new ArgumentException("Profile color must have the form #RRGGBB.");
            if (!Enum.IsDefined(profile.Activation))
                throw new ArgumentException("Profile activation binding is invalid.");
            if (profile.Motion is null)
                throw new ArgumentException("Profile motion settings are required.");
            profile.Motion.Validate();
        }
        if (!ids.Contains(AppSettings.ActiveProfileId))
            throw new ArgumentException("The active profile must refer to an existing profile identifier.");
    }

    public static bool IsSupportedOutputRate(int rate) => rate is 125 or 250 or 500 or 1000;

    public static bool IsArduinoPortName(string? port) => port is { Length: >= 4 and <= 8 } &&
        port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(port.AsSpan(3), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 65535;
}
