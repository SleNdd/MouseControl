using System.Runtime.InteropServices;
using MouseControl.Core;

namespace MouseControl.App.Services;

/// <summary>HWND-owner-thread bridge for documented hotkey, tray and session notifications.</summary>
public sealed class WindowBridge : IDisposable
{
    private const nuint SubclassId = 0x4D43;
    private const uint TrayId = 1;
    private readonly nint _hwnd;
    private readonly Native.SubclassProc _callback; // Rooted for the full native subclass lifetime.
    private GCHandle _callbackRoot;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly uint _taskbarCreated;
    private readonly uint _showExisting = Native.RegisterWindowMessage("SleNdd.MouseControl.ShowWindow");
    private readonly HashSet<int> _registeredHotkeys = new();
    private Native.NotifyIcon _icon;
    private bool _subclassInstalled, _sessionNotifications, _disposed, _sessionBlocked, _powerBlocked;
    private GlobalHotkey? _hotkey;
    private int _hotkeyId;
    private readonly Dictionary<bool, (GlobalHotkey Key, int Id)> _verticalHotkeys = new();
    private bool _enabled;
    private string _status = "Disabled";
    private Profile[] _profiles = Array.Empty<Profile>();
    private Guid _activeId;

    public WindowBridge(nint hwnd)
    {
        if (hwnd == 0) throw new ArgumentException("A valid window handle is required.", nameof(hwnd));
        _hwnd = hwnd;
        _callback = WindowProcedure;
        _callbackRoot = GCHandle.Alloc(_callback);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        _subclassInstalled = Native.SetWindowSubclass(hwnd, _callback, SubclassId, 0);
        if (!_subclassInstalled)
        {
            _callbackRoot.Free();
            AvailabilityError = "Window integration failed; tray and global hotkey are unavailable.";
            return;
        }
        _sessionNotifications = Native.WTSRegisterSessionNotification(hwnd, 0); // This session only.
        if (!_sessionNotifications) AddError("Session notification registration failed.");
        if (_taskbarCreated == 0) AddError("Explorer restart notification registration failed.");
        _icon = new Native.NotifyIcon
        {
            Size = (uint)Marshal.SizeOf<Native.NotifyIcon>(), Window = hwnd, Id = TrayId,
            Flags = 0x1 | 0x2 | 0x4 | 0x80, // MESSAGE | ICON | TIP | SHOWTIP
            Callback = Native.TrayMessage, Icon = Native.LoadIcon(0, (nint)32512),
            Tip = "MouseControl — Disabled", Info = "", InfoTitle = ""
        };
        AddTrayIcon();
    }

    public event Action? ShowRequested;
    public event Action? HideRequested;
    public event Action? ToggleRequested;
    public event Action<int>? VerticalAdjustmentRequested;
    public event Action? ExitRequested;
    public event Action<Guid>? ProfileRequested;
    public event Action<bool>? SessionSuspended;
    public event Action<string>? AvailabilityChanged;
    public event Action? DevicesChanged;
    public bool MenuOpen { get; private set; }
    public bool TrayAvailable { get; private set; }
    public bool HotkeyAvailable => _hotkey is not null;
    public string AvailabilityError { get; private set; } = "";

    public bool TrySetHotkey(GlobalHotkey hotkey, out string error)
    {
        VerifyOwnerThread();
        error = "";
        if (_disposed || !_subclassInstalled) { error = "Window hotkey integration is unavailable."; return false; }
        if (hotkey is null || hotkey.VirtualKey is < 1 or > 254 ||
            (hotkey.Modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win)) != 0)
        { error = "Choose a valid virtual key and modifier combination."; return false; }
        if (hotkey.VirtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
        { error = "A modifier cannot be the toggle key by itself."; return false; }
        if (hotkey.VirtualKey == 0x7B)
        { error = "F12 is reserved by Windows for the debugger."; return false; }
        if (_hotkey == hotkey) return true;
        if (_verticalHotkeys.Values.Any(binding => binding.Key == hotkey))
        { error = "This key is already used for vertical adjustment."; return false; }
        var candidateId = _hotkeyId == 0x5500 ? 0x5501 : 0x5500;
        if (_registeredHotkeys.Contains(candidateId))
        {
            if (!Native.UnregisterHotKey(_hwnd, candidateId))
            { error = "A prior hotkey could not be released; the current binding remains active."; return false; }
            _registeredHotkeys.Remove(candidateId);
        }
        if (!Native.RegisterHotKey(_hwnd, candidateId, (uint)hotkey.Modifiers | 0x4000, (uint)hotkey.VirtualKey))
        {
            error = $"Hotkey is reserved or already in use (Windows error {Marshal.GetLastWin32Error()}); previous binding remains active.";
            return false;
        }
        _registeredHotkeys.Add(candidateId);
        var previousId = _hotkeyId;
        _hotkeyId = candidateId;
        _hotkey = hotkey;
        if (previousId != 0 && Native.UnregisterHotKey(_hwnd, previousId)) _registeredHotkeys.Remove(previousId);
        return true;
    }

    public void UpdateTray(bool enabled, string status, IReadOnlyList<Profile> profiles, Guid activeId)
    {
        VerifyOwnerThread();
        if (_disposed) return;
        _enabled = enabled;
        _status = status ?? "";
        _profiles = profiles.ToArray();
        _activeId = activeId;
        var profileName = _profiles.FirstOrDefault(p => p.Id == activeId)?.Name ?? "Default";
        _icon.Tip = Trim($"MouseControl — {profileName} — {_status}", 127);
        if (TrayAvailable && !Native.Shell_NotifyIcon(1, ref _icon))
        {
            TrayAvailable = false;
            AddError("Tray update failed; the window remains available.");
            Show();
        }
    }

    public bool TrySetVerticalHotkeys(GlobalHotkey increase, GlobalHotkey decrease, out string error)
    {
        VerifyOwnerThread();
        error = "";
        if (_disposed || !_subclassInstalled) { error = "Window hotkey integration is unavailable."; return false; }
        if (increase == decrease || increase == _hotkey || decrease == _hotkey)
        { error = "Toggle, increase and decrease must use different keys."; return false; }
        foreach (var key in new[] { increase, decrease })
            if (key.VirtualKey is < 1 or > 254 or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x7B ||
                (key.Modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win)) != 0)
            { error = "Choose a key other than a modifier or reserved F12."; return false; }
        var candidates = new Dictionary<bool, (GlobalHotkey Key, int Id)>();
        foreach (var (increaseSlot, key) in new[] { (true, increase), (false, decrease) })
        {
            if (_verticalHotkeys.TryGetValue(increaseSlot, out var current) && current.Key == key) continue;
            var firstId = increaseSlot ? 0x5600 : 0x5602;
            var id = current.Id == firstId ? firstId + 1 : firstId;
            if (_registeredHotkeys.Contains(id))
            {
                if (!Native.UnregisterHotKey(_hwnd, id))
                { error = "A prior binding could not be released."; ReleaseCandidates(); return false; }
                _registeredHotkeys.Remove(id);
            }
            if (!Native.RegisterHotKey(_hwnd, id, (uint)key.Modifiers | 0x4000, (uint)key.VirtualKey))
            {
                error = $"Key is reserved or already in use (Windows error {Marshal.GetLastWin32Error()}); previous bindings remain active.";
                ReleaseCandidates(); return false;
            }
            _registeredHotkeys.Add(id); candidates.Add(increaseSlot, (key, id));
        }
        foreach (var (slot, candidate) in candidates)
        {
            _verticalHotkeys.TryGetValue(slot, out var previous);
            _verticalHotkeys[slot] = candidate;
            if (previous.Id != 0 && Native.UnregisterHotKey(_hwnd, previous.Id)) _registeredHotkeys.Remove(previous.Id);
        }
        return true;
        void ReleaseCandidates()
        {
            foreach (var candidate in candidates.Values)
                if (Native.UnregisterHotKey(_hwnd, candidate.Id)) _registeredHotkeys.Remove(candidate.Id);
        }
    }

    public void Hide()
    {
        VerifyOwnerThread();
        if (_disposed || !TrayAvailable) return; // Never trap the only accessible window.
        Native.ShowWindow(_hwnd, 0);
    }

    public void Show()
    {
        VerifyOwnerThread();
        if (_disposed) return;
        Native.ShowWindow(_hwnd, 9); // SW_RESTORE
        Native.SetForegroundWindow(_hwnd);
    }

    public void Dispose()
    {
        VerifyOwnerThread();
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _registeredHotkeys) Native.UnregisterHotKey(_hwnd, id);
        _registeredHotkeys.Clear();
        _hotkey = null;
        _verticalHotkeys.Clear();
        Native.Shell_NotifyIcon(2, ref _icon);
        TrayAvailable = false;
        if (_sessionNotifications) Native.WTSUnRegisterSessionNotification(_hwnd);
        if (_subclassInstalled)
        {
            if (Native.RemoveWindowSubclass(_hwnd, _callback, SubclassId))
            {
                if (_callbackRoot.IsAllocated) _callbackRoot.Free();
            }
            else AddError("Window callback removal failed; its delegate remains rooted until window destruction.");
        }
        _sessionNotifications = _subclassInstalled = false;
        GC.SuppressFinalize(this);
    }

    private nint WindowProcedure(nint hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        try
        {
            if (message == 0x82) // WM_NCDESTROY also releases a callback whose earlier removal failed.
            {
                Dispose();
                if (_callbackRoot.IsAllocated) _callbackRoot.Free();
            }
            if (!_disposed)
            {
                if (_showExisting != 0 && message == _showExisting) { Show(); Invoke(ShowRequested); return 0; }
                if (message == 0x24 && lparam != 0) // WM_GETMINMAXINFO
                {
                    var result = Native.DefSubclassProc(hwnd, message, wparam, lparam);
                    var limits = Marshal.PtrToStructure<Native.MinMaxInfo>(lparam);
                    var scale = Native.GetDpiForWindow(hwnd) / 96.0;
                    limits.MinimumTrack = new Native.Point { X = (int)(720 * scale), Y = (int)(500 * scale) };
                    limits.MaximumTrack = limits.MinimumTrack;
                    Marshal.StructureToPtr(limits, lparam, false);
                    return result;
                }
                if (message == Native.WmHotkey && (int)wparam == _hotkeyId && _hotkey is not null)
                { Invoke(ToggleRequested); return 0; }
                if (message == Native.WmHotkey)
                    foreach (var (increase, binding) in _verticalHotkeys)
                        if ((int)wparam == binding.Id) { Invoke(VerticalAdjustmentRequested, increase ? 1 : -1); return 0; }
                if (message == Native.TrayMessage)
                {
                    var notification = (uint)((nuint)lparam & 0xffff);
                    if (notification is 0x400 or 0x401 or 0x203) RequestShow(); // NIN_SELECT, KEYSELECT, double-click.
                    else if (notification is Native.WmContextMenu or 0x205) OpenMenu();
                    return 0;
                }
                if (_taskbarCreated != 0 && message == _taskbarCreated)
                {
                    TrayAvailable = false;
                    if (!AddTrayIcon()) RequestShow();
                    return 0;
                }
                if (message == Native.WmSize && wparam == 1)
                {
                    if (TrayAvailable) { Hide(); Invoke(HideRequested); }
                    else Native.ShowWindow(_hwnd, 9);
                }
                if (message == Native.WmSession)
                {
                    _sessionBlocked = SessionBlockedAfter(wparam, _sessionBlocked);
                    PublishSuspended();
                }
                if (message == Native.WmPower)
                {
                    if (wparam == 4) _powerBlocked = true;
                    else if (wparam is 7 or 18) _powerBlocked = false;
                    PublishSuspended();
                }
                if (message == 0x0219 && wparam is 0x0007 or 0x8000 or 0x8004) // WM_DEVICECHANGE
                    Invoke(DevicesChanged);
            }
        }
        catch (Exception ex)
        {
            AddError($"Window integration error: {ex.Message}");
            PublishSuspended(force: true);
        }
        return Native.DefSubclassProc(hwnd, message, wparam, lparam);
    }

    private bool AddTrayIcon()
    {
        if (_icon.Icon == 0 || !Native.Shell_NotifyIcon(0, ref _icon))
        {
            TrayAvailable = false;
            AddError("Tray icon could not be created; hiding is unavailable.");
            return false;
        }
        _icon.Version = 4;
        if (!Native.Shell_NotifyIcon(4, ref _icon))
        {
            Native.Shell_NotifyIcon(2, ref _icon);
            TrayAvailable = false;
            AddError("Tray callback registration failed; hiding is unavailable.");
            return false;
        }
        TrayAvailable = true;
        return true;
    }

    private void OpenMenu()
    {
        if (MenuOpen || _disposed) return;
        nint menu = 0, profilesMenu = 0;
        var childAttached = false;
        MenuOpen = true;
        PublishSuspended();
        try
        {
            menu = Native.CreatePopupMenu();
            profilesMenu = Native.CreatePopupMenu();
            if (menu == 0 || profilesMenu == 0) throw new InvalidOperationException("Tray menu could not be created.");
            Append(menu, 0, 1, "Show MouseControl");
            Append(menu, _enabled ? 8u : 0u, 2, "Enabled");
            Append(menu, 2, 0, Trim(_status, 100));
            Append(menu, 0x800, 0, "");
            var commands = new Dictionary<uint, Guid>();
            for (var i = 0; i < _profiles.Length; i++)
            {
                var command = checked((uint)i + 1000);
                commands.Add(command, _profiles[i].Id);
                Append(profilesMenu, _profiles[i].Id == _activeId ? 8u : 0u, command, Trim(_profiles[i].Name, 80));
            }
            if (_profiles.Length == 0) Append(profilesMenu, 2, 0, "No profiles");
            Append(menu, 0x10, (nuint)profilesMenu, "Profiles");
            childAttached = true;
            Append(menu, 0x800, 0, "");
            Append(menu, 0, 3, "Exit");
            Native.GetCursorPos(out var point);
            Native.SetForegroundWindow(_hwnd);
            var selected = Native.TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, _hwnd, 0); // RETURNCMD | RIGHTBUTTON
            Native.PostMessage(_hwnd, 0, 0, 0); // Shell popup dismissal protocol.
            if (selected == 1) RequestShow();
            else if (selected == 2) Invoke(ToggleRequested);
            else if (selected == 3) Invoke(ExitRequested);
            else if (commands.TryGetValue(selected, out var profile)) Invoke(ProfileRequested, profile);
        }
        finally
        {
            if (menu != 0) Native.DestroyMenu(menu); // Also destroys an attached submenu.
            if (profilesMenu != 0 && !childAttached) Native.DestroyMenu(profilesMenu);
            MenuOpen = false;
            PublishSuspended(); // Runtime must require release/repress after this transition.
        }
    }

    private static void Append(nint menu, uint flags, nuint id, string text)
    {
        if (!Native.AppendMenu(menu, flags, id, text.Replace("&", "&&", StringComparison.Ordinal)))
            throw new InvalidOperationException("Tray menu item could not be created.");
    }

    private void PublishSuspended(bool force = false) => Invoke(SessionSuspended, force || MenuOpen || _sessionBlocked || _powerBlocked);
    internal static bool SessionBlockedAfter(nuint notification, bool previous) => notification switch
    {
        2 or 4 or 6 or 7 => true, // Console/remote disconnect, logoff, lock.
        1 or 3 or 5 or 8 => false, // Console/remote connect, logon, unlock.
        _ => previous
    };
    private void RequestShow() { Show(); Invoke(ShowRequested); }
    private void AddError(string error)
    {
        if (!AvailabilityError.Contains(error, StringComparison.Ordinal))
            AvailabilityError = string.IsNullOrEmpty(AvailabilityError) ? error : AvailabilityError + " " + error;
        Invoke(AvailabilityChanged, AvailabilityError);
    }
    private static string Trim(string value, int length) => value.Length <= length ? value : value[..length];
    private void VerifyOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("WindowBridge must be used on its HWND owner thread.");
    }
    private static void Invoke(Action? action)
    {
        if (action is null) return;
        foreach (Action callback in action.GetInvocationList()) { try { callback(); } catch { } }
    }
    private static void Invoke<T>(Action<T>? action, T value)
    {
        if (action is null) return;
        foreach (Action<T> callback in action.GetInvocationList()) { try { callback(value); } catch { } }
    }
}
