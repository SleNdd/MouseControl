using System.Globalization;
using System.IO.Ports;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MouseControl.App.Services;
using MouseControl.Core;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace MouseControl.App;

public sealed partial class MainWindow : Window
{
    private readonly RuntimeService _runtime;
    private readonly WindowBridge _bridge;
    private readonly ProfileStore _store;
    private ProfileConfiguration _configuration;
    private readonly UISettings _system = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly object _backendSetupLock = new();
    private int _backendRevision;
    private bool _connecting;
    private bool _updating = true, _dragging, _capturing, _preview = true, _closed, _dirty;
    private bool _sessionSuspended;
    private bool _hidden;
    private Profile? _deleted, _undoReplacement;
    private int _deletedIndex, _previewFrame;
    private IReadOnlyList<MotionDelta> _previewPoints = Array.Empty<MotionDelta>();
    private readonly bool _smoke;
    private readonly string _dataFolder;
    private string? _initialError;
    private new Profile Current => _configuration.Profiles.First(p => p.Id == _configuration.AppSettings.ActiveProfileId);

    public MainWindow(bool smoke)
    {
        _smoke = smoke;
        _dataFolder = smoke ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MouseControl-smoke-" + Guid.NewGuid().ToString("N"))
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseControl");
        Directory.CreateDirectory(_dataFolder);
        _store = new ProfileStore(System.IO.Path.Combine(_dataFolder, "profiles.json"));
        var loaded = _store.Load();
        _configuration = loaded.Configuration ?? ProfileConfiguration.CreateDefault();
        if (!loaded.Success)
        {
            _initialError = "Settings could not be read. The original file is preserved. This session saves to a recovery file. " + loaded.Error;
            _store = new ProfileStore(System.IO.Path.Combine(_dataFolder, "profiles.recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json"));
        }
        InitializeComponent();
        Title = "MouseControl";
        SystemBackdrop = new DesktopAcrylicBackdrop();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = Native.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(720 * scale), (int)(500 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsMaximizable = false;
        _runtime = new RuntimeService(smoke);
        _runtime.SetProfile(Current);
        _runtime.StatusChanged += _ => DispatcherQueue.TryEnqueue(RefreshStatus);
        _runtime.BackendChanged += () => DispatcherQueue.TryEnqueue(RefreshBackendStatus);
        if (_configuration.AppSettings.InputBackend == InputBackendKind.Arduino)
            _runtime.SelectBackendDisconnected(InputBackendKind.Arduino, _configuration.AppSettings.ArduinoPort, out _);
        _bridge = new WindowBridge(hwnd);
        _bridge.ShowRequested += OnShown;
        _bridge.HideRequested += OnHidden;
        _bridge.ToggleRequested += () => { CancelCapture(); _runtime.SetEnabled(!_runtime.Enabled); RefreshStatus(); };
        _bridge.ExitRequested += RequestExit;
        _bridge.ProfileRequested += SelectProfile;
        _bridge.SessionSuspended += suspended => { _sessionSuspended = suspended; ApplySuspension(); };
        _bridge.AvailabilityChanged += error => { ShowNotice(error, InfoBarSeverity.Warning); if (!_bridge.TrayAvailable) OnShown(); };
        _bridge.DevicesChanged += () => DispatcherQueue.TryEnqueue(CheckDeviceChange);
        if (!_smoke && !_bridge.TrySetHotkey(_configuration.AppSettings.ToggleHotkey, out var hotkeyError)) _initialError = hotkeyError;
        if (!_bridge.TrayAvailable) _initialError = _bridge.AvailabilityError;
        _system.ColorValuesChanged += System_ColorValuesChanged;
        _saveTimer.Tick += (_, _) => Save();
        _previewTimer.Tick += (_, _) => { _previewFrame = (_previewFrame + 1) % Math.Max(1, _previewPoints.Count); DrawPad(); };
        _connectionTimer.Tick += (_, _) => _ = TryConnectArduino();
        Closed += Window_Closed;
        AppWindow.Closing += (_, args) =>
        {
            Save();
            if (_dirty) { args.Cancel = true; _runtime.SetEnabled(false); _bridge.Show(); OnShown(); }
        };
        Root.Loaded += (_, _) =>
        {
            if (_initialError is not null) ShowNotice(_initialError, InfoBarSeverity.Warning);
#if DEBUG
            if (_smoke) _ = SmokeTest();
#endif
        };
        ApplyTheme();
        RebuildTabs();
        RefreshControls();
        _updating = false;
        RefreshStatus();
        RefreshBackendStatus();
        var keyMenu = new MenuFlyout();
        var escapeKey = new MenuFlyoutItem { Text = "Use Escape as toggle key" };
        escapeKey.Click += (_, _) => TryCaptureHotkey(new GlobalHotkey((int)VirtualKey.Escape, HotkeyModifiers.None));
        keyMenu.Items.Add(escapeKey); HotkeyButton.ContextFlyout = keyMenu;
    }

    private void System_ColorValuesChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(ApplyTheme);
    private void ApplyTheme()
    {
        var background = _system.GetColorValue(UIColorType.Background);
        Root.RequestedTheme = background.R + background.G + background.B < 384 ? ElementTheme.Dark : ElementTheme.Light;
        // Windows owns material fallback; custom canvas colors follow the same live theme.
        RebuildPreview();
    }

    private void RebuildTabs()
    {
        var before = _updating; _updating = true;
        ProfileTabs.TabItems.Clear();
        foreach (var profile in _configuration.Profiles)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(ParseColor(profile.Color)), VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock { Text = profile.Name, MaxWidth = 125, TextTrimming = TextTrimming.CharacterEllipsis });
            var tab = new TabViewItem { Header = header, Tag = profile.Id, IsClosable = true };
            ToolTipService.SetToolTip(tab, profile.Name);
            var menu = new MenuFlyout();
            var edit = new MenuFlyoutItem { Text = "Edit name and color", Icon = new SymbolIcon(Symbol.Edit) };
            edit.Click += async (_, _) => await EditProfile(profile.Id);
            menu.Items.Add(edit);
            var duplicate = new MenuFlyoutItem { Text = "Duplicate" };
            duplicate.Click += (_, _) => AddProfile(profile);
            menu.Items.Add(duplicate);
            tab.ContextFlyout = menu;
            tab.DoubleTapped += async (_, _) => await EditProfile(profile.Id);
            ProfileTabs.TabItems.Add(tab);
            if (profile.Id == _configuration.AppSettings.ActiveProfileId) ProfileTabs.SelectedItem = tab;
        }
        _updating = before;
    }

    private void RefreshControls()
    {
        var before = _updating; _updating = true;
        var motion = Current.Motion;
        VerticalSlider.Value = VerticalNumber.Value = Math.Round(motion.Y * 100);
        HorizontalSlider.Value = HorizontalNumber.Value = Math.Round(motion.X * 100);
        HumanSlider.Value = HumanNumber.Value = Math.Round(motion.Humanization * 100);
        ActivationCombo.SelectedIndex = (int)Current.Activation;
        HotkeyButton.Content = _capturing ? "Press a key…" : FormatHotkey(_configuration.AppSettings.ToggleHotkey);
        ModelLabel.Text = $"{motion.Model} · hold to move";
        _updating = before;
        RebuildPreview();
    }

    private void RefreshStatus()
    {
        if (_closed) return;
        var before = _updating; _updating = true;
        EnabledSwitch.IsOn = _runtime.Enabled;
        var status = _runtime.Status;
        StatusText.Text = status is "Disabled" or "Idle" or "Active" or "Paused" ? status : "Disabled";
        ToolTipService.SetToolTip(StatusText, status);
        if (status.Contains("failed", StringComparison.OrdinalIgnoreCase) || status.Contains("error", StringComparison.OrdinalIgnoreCase)) ShowNotice(status, InfoBarSeverity.Error);
        _bridge.UpdateTray(_runtime.Enabled, _runtime.Status, _configuration.Profiles, Current.Id);
        _updating = before;
    }

    private void RefreshBackendStatus()
    {
        if (_closed) return;
        bool arduino = _configuration.AppSettings.InputBackend == InputBackendKind.Arduino;
        bool online = arduino && _runtime.BackendKind == InputBackendKind.Arduino && _runtime.BackendConnected;
        ArduinoStatus.Visibility = arduino ? Visibility.Visible : Visibility.Collapsed;
        ArduinoLamp.Fill = new SolidColorBrush(online ? Color.FromArgb(255, 34, 180, 92) : Color.FromArgb(255, 224, 68, 68));
        ArduinoStatusText.Text = online ? "Online" : "Offline";
        ToolTipService.SetToolTip(ArduinoStatus, online
            ? $"Arduino · {_runtime.BackendPort} · firmware connected. Idle connections are not polled."
            : $"Arduino · {_configuration.AppSettings.ArduinoPort} · waiting for MouseControl firmware.");
        if (arduino && !online && !_smoke) _connectionTimer.Start();
        else _connectionTimer.Stop();
        RefreshStatus();
    }

    private static string[] AvailablePorts() => SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private async Task TryConnectArduino()
    {
        if (_closed || _smoke || _connecting || _configuration.AppSettings.InputBackend != InputBackendKind.Arduino) return;
        var port = _configuration.AppSettings.ArduinoPort;
        try { if (!AvailablePorts().Contains(port, StringComparer.OrdinalIgnoreCase)) return; }
        catch (Exception ex) { ToolTipService.SetToolTip(ArduinoStatus, "Port discovery failed: " + ex.Message); return; }
        int revision = Volatile.Read(ref _backendRevision);
        _connecting = true;
        try
        {
            await Task.Run(() =>
            {
                lock (_backendSetupLock)
                {
                    if (_closed || revision != Volatile.Read(ref _backendRevision)) return;
                    _runtime.TryReconnectArduino(port, out _);
                }
            });
        }
        finally { _connecting = false; if (!_closed) RefreshBackendStatus(); }
    }

    private void CheckDeviceChange()
    {
        if (_closed || _smoke || _configuration.AppSettings.InputBackend != InputBackendKind.Arduino) return;
        try
        {
            if (_runtime.BackendConnected && !AvailablePorts().Contains(_runtime.BackendPort, StringComparer.OrdinalIgnoreCase))
            {
                lock (_backendSetupLock)
                    _runtime.SelectBackendDisconnected(InputBackendKind.Arduino, _configuration.AppSettings.ArduinoPort, out _);
                RefreshBackendStatus();
            }
            if (!_runtime.BackendConnected) _ = TryConnectArduino();
        }
        catch (Exception ex) { ToolTipService.SetToolTip(ArduinoStatus, "Port discovery failed: " + ex.Message); }
    }

    private bool SelectInputBackend(InputBackendKind kind, string port)
    {
        lock (_backendSetupLock)
        {
            Interlocked.Increment(ref _backendRevision);
            if (!_runtime.SelectBackendDisconnected(kind, port, out var error))
            { ShowNotice(error, InfoBarSeverity.Error); return false; }
            _configuration = _configuration with { AppSettings = _configuration.AppSettings with { InputBackend = kind, ArduinoPort = port } };
        }
        MarkDirty(); RefreshBackendStatus();
        if (kind == InputBackendKind.Arduino) _ = TryConnectArduino();
        return true;
    }

    private void ReplaceProfile(Profile profile)
    {
        _configuration = _configuration with { Profiles = _configuration.Profiles.Select(p => p.Id == profile.Id ? profile : p).ToArray() };
        _runtime.SetProfile(Current);
        RefreshControls();
        MarkDirty();
    }
    private void SetVector(double x, double y) => ReplaceProfile(Current with { Motion = Current.Motion with { X = Math.Clamp(x, -1, 1), Y = Math.Clamp(y, -1, 1) } });
    private void Slider_Changed(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_updating || _runtime is null) return;
        if (sender is not Slider slider) return;
        if (slider == VerticalSlider) SetVector(Current.Motion.X, args.NewValue / 100);
        else if (slider == HorizontalSlider) SetVector(args.NewValue / 100, Current.Motion.Y);
        else ReplaceProfile(Current with { Motion = Current.Motion with { Humanization = args.NewValue / 100 } });
    }
    private void Number_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updating || !double.IsFinite(args.NewValue) || _runtime is null) return;
        if (sender == VerticalNumber) SetVector(Current.Motion.X, Math.Clamp(args.NewValue, -100, 100) / 100);
        else if (sender == HorizontalNumber) SetVector(Math.Clamp(args.NewValue, -100, 100) / 100, Current.Motion.Y);
        else ReplaceProfile(Current with { Motion = Current.Motion with { Humanization = Math.Clamp(args.NewValue, 0, 100) / 100 } });
    }
    private void Vertical_Reset(object sender, DoubleTappedRoutedEventArgs e) { SetVector(Current.Motion.X, 0); e.Handled = true; }
    private void Horizontal_Reset(object sender, DoubleTappedRoutedEventArgs e) { SetVector(0, Current.Motion.Y); e.Handled = true; }
    private void Enabled_Toggled(object sender, RoutedEventArgs e) { if (_updating || _runtime is null) return; _runtime.SetEnabled(EnabledSwitch.IsOn); RefreshStatus(); }
    private void Activation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || ActivationCombo.SelectedIndex < 0 || _runtime is null) return;
        var binding = (ActivationBinding)ActivationCombo.SelectedIndex;
        if (BindingCollision(_configuration.AppSettings.ToggleHotkey, binding)) { ShowNotice("The activation binding conflicts with the toggle key.", InfoBarSeverity.Warning); RefreshControls(); return; }
        ReplaceProfile(Current with { Activation = binding });
    }
    private void AddProfile_Click(TabView sender, object args) => AddProfile(Current);
    private void AddProfile(Profile source)
    {
        var suffix = 2; var name = "Profile " + suffix;
        while (_configuration.Profiles.Any(p => p.Name == name)) name = "Profile " + ++suffix;
        var profile = source with { Id = Guid.NewGuid(), Name = name };
        _configuration = _configuration with { Profiles = _configuration.Profiles.Append(profile).ToArray() };
        SelectProfile(profile.Id);
    }
    private void SelectProfile(Guid id)
    {
        if (!_configuration.Profiles.Any(p => p.Id == id)) return;
        CancelCapture();
        _configuration = _configuration with { AppSettings = _configuration.AppSettings with { ActiveProfileId = id } };
        _runtime.SetProfile(Current); RebuildTabs(); RefreshControls(); RefreshStatus(); MarkDirty();
    }
    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && ProfileTabs.SelectedItem is TabViewItem { Tag: Guid id } && id != Current.Id) SelectProfile(id);
    }
    private void Profile_CloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is not Guid id) return;
        _deletedIndex = _configuration.Profiles.ToList().FindIndex(p => p.Id == id);
        _deleted = _configuration.Profiles[_deletedIndex]; _undoReplacement = null;
        var remaining = _configuration.Profiles.Where(p => p.Id != id).ToList();
        if (remaining.Count == 0) { _undoReplacement = ProfileConfiguration.CreateDefault().Profiles[0]; remaining.Add(_undoReplacement); }
        var active = id == Current.Id ? remaining[0].Id : Current.Id;
        _configuration = _configuration with { Profiles = remaining.ToArray() };
        SelectProfile(active);
        var undo = new Button { Content = "Undo" };
        undo.Click += (_, _) => UndoDelete();
        ShowNotice($"Deleted {_deleted.Name}.", InfoBarSeverity.Informational, undo);
    }
    private void UndoDelete()
    {
        if (_deleted is null) return;
        var profiles = _configuration.Profiles.ToList();
        if (_undoReplacement is not null) profiles.Remove(_undoReplacement);
        profiles.Insert(Math.Min(_deletedIndex, profiles.Count), _deleted);
        _configuration = _configuration with { Profiles = profiles.ToArray() };
        var restored = _deleted.Id; _deleted = null; _undoReplacement = null;
        Notice.IsOpen = false; Notice.ActionButton = null;
        SelectProfile(restored);
    }
    private async Task EditProfile(Guid id)
    {
        var profile = _configuration.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null) return;
        CancelCapture(); _runtime.Invalidate();
        var name = new TextBox { Header = "Profile name", Text = profile.Name, MaxLength = 40 };
        var color = new ColorPicker { Color = ParseColor(profile.Color), IsAlphaEnabled = false, IsColorSliderVisible = false, IsColorChannelTextInputVisible = false, MaxHeight = 240 };
        var panel = new StackPanel { Spacing = 14 }; panel.Children.Add(name); panel.Children.Add(color);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Edit profile", Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, e) => { if (string.IsNullOrWhiteSpace(name.Text)) { name.Header = "Enter a profile name"; e.Cancel = true; } };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ReplaceProfile(profile with { Name = name.Text.Trim(), Color = $"#{color.Color.R:X2}{color.Color.G:X2}{color.Color.B:X2}" });
            RebuildTabs(); RefreshStatus();
        }
    }

    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        _capturing = !_capturing; ApplySuspension(); _runtime.Invalidate();
        RefreshControls(); HotkeyButton.Focus(FocusState.Programmatic);
    }
    private void ApplySuspension() => _runtime.SetSuspended(_capturing || _sessionSuspended);
    private void CancelCapture() { if (!_capturing) return; _capturing = false; ApplySuspension(); RefreshControls(); }
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        if (e.Key == VirtualKey.Escape) { CancelCapture(); return; }
        if (e.Key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
        var modifiers = HotkeyModifiers.None;
        if (Native.Down(0x11)) modifiers |= HotkeyModifiers.Control;
        if (Native.Down(0x12)) modifiers |= HotkeyModifiers.Alt;
        if (Native.Down(0x10)) modifiers |= HotkeyModifiers.Shift;
        if (Native.Down(0x5B) || Native.Down(0x5C)) modifiers |= HotkeyModifiers.Win;
        TryCaptureHotkey(new GlobalHotkey((int)e.Key, modifiers));
    }
    private void TryCaptureHotkey(GlobalHotkey candidate)
    {
        if (_configuration.Profiles.Any(p => BindingCollision(candidate, p.Activation))) { ShowNotice("This key conflicts with a profile's activation binding.", InfoBarSeverity.Warning); return; }
        if (!_bridge.TrySetHotkey(candidate, out var error)) { ShowNotice(error, InfoBarSeverity.Warning); return; }
        _configuration = _configuration with { AppSettings = _configuration.AppSettings with { ToggleHotkey = candidate } };
        CancelCapture(); MarkDirty();
    }
    private static bool BindingCollision(GlobalHotkey hotkey, ActivationBinding activation) =>
        hotkey.Modifiers == HotkeyModifiers.None && ((activation == ActivationBinding.Alt && hotkey.VirtualKey is 0x12 or 0xA4 or 0xA5) || (activation == ActivationBinding.Shift && hotkey.VirtualKey is 0x10 or 0xA0 or 0xA1));
    private static string FormatHotkey(GlobalHotkey key)
    {
        var text = ((VirtualKey)key.VirtualKey).ToString();
        if (text.StartsWith("Number", StringComparison.Ordinal)) text = text[6..];
        if (text.StartsWith("Menu", StringComparison.Ordinal)) text = "Alt";
        return (key.Modifiers.HasFlag(HotkeyModifiers.Win) ? "Win + " : "") + (key.Modifiers.HasFlag(HotkeyModifiers.Control) ? "Ctrl + " : "") + (key.Modifiers.HasFlag(HotkeyModifiers.Alt) ? "Alt + " : "") + (key.Modifiers.HasFlag(HotkeyModifiers.Shift) ? "Shift + " : "") + text;
    }

    private void Pad_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(PadCanvas).Properties.IsLeftButtonPressed) return;
        _dragging = true; PadCanvas.CapturePointer(e.Pointer); PadButton.Focus(FocusState.Pointer); DragTo(e); e.Handled = true;
    }
    private void Pad_Moved(object sender, PointerRoutedEventArgs e) { if (_dragging) { DragTo(e); e.Handled = true; } }
    private void DragTo(PointerRoutedEventArgs e) { var p = e.GetCurrentPoint(PadCanvas).Position; SetVector(2 * (p.X - 12) / 210 - 1, 2 * (p.Y - 12) / 210 - 1); }
    private void Pad_Released(object sender, PointerRoutedEventArgs e) { _dragging = false; PadCanvas.ReleasePointerCapture(e.Pointer); e.Handled = true; }
    private void Pad_CaptureLost(object sender, PointerRoutedEventArgs e) => _dragging = false;
    private void Pad_Reset(object sender, DoubleTappedRoutedEventArgs e) { SetVector(0, 0); e.Handled = true; }
    private void Pad_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var step = Native.Down(0x11) ? 0.1 : 0.01;
        var x = Current.Motion.X; var y = Current.Motion.Y;
        switch (e.Key) { case VirtualKey.Left: x -= step; break; case VirtualKey.Right: x += step; break; case VirtualKey.Up: y -= step; break; case VirtualKey.Down: y += step; break; case VirtualKey.Home: x = y = 0; break; default: return; }
        SetVector(x, y); e.Handled = true;
    }
    private void Preview_Click(object sender, RoutedEventArgs e) { _preview = !_preview; RebuildPreview(); }
    private void RebuildPreview()
    {
        if (PadCanvas is null || _configuration is null) return;
        _previewTimer.Stop(); _previewFrame = 0;
        var model = new MotionGenerator(Current.Motion, 2001);
        var points = new List<MotionDelta>(); double x = 0, y = 0;
        for (int i = 0; i < 180; i++) { var d = model.Step(1.0 / 120); x += d.Dx; y += d.Dy; points.Add(new(x, y)); }
        _previewPoints = points;
        PreviewText.Text = _preview ? "Pause preview" : "Preview";
        if (_preview && !_hidden && _system.AnimationsEnabled) _previewTimer.Start();
        DrawPad();
    }
    private void DrawPad()
    {
        if (PadCanvas is null || _closed) return;
        PadCanvas.Children.Clear();
        bool dark = Root.RequestedTheme == ElementTheme.Dark;
        var accent = _system.GetColorValue(UIColorType.Accent);
        var grid = new SolidColorBrush(dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0));
        var axes = new SolidColorBrush(dark ? Color.FromArgb(95, 255, 255, 255) : Color.FromArgb(90, 0, 0, 0));
        for (int i = 0; i <= 8; i++) { var z = 12 + i * 210.0 / 8; Line(z, 12, z, 222, i == 4 ? axes : grid, i == 4 ? 1.2 : 0.7); Line(12, z, 222, z, i == 4 ? axes : grid, i == 4 ? 1.2 : 0.7); }
        var vector = new SolidColorBrush(accent);
        var hx = 12 + (Current.Motion.X + 1) * 105; var hy = 12 + (Current.Motion.Y + 1) * 105;
        Line(117, 117, hx, hy, vector, 1.2);
        if (_previewPoints.Count > 0)
        {
            var trace = new Polyline { Stroke = vector, StrokeThickness = 1.6, Opacity = 0.45, StrokeDashArray = new DoubleCollection { 2, 3 } };
            trace.Points.Add(new Point(117, 117));
            foreach (var p in _previewPoints) trace.Points.Add(new Point(117 + p.Dx * 0.8, 117 + p.Dy * 0.8));
            PadCanvas.Children.Add(trace);
            var position = _previewPoints[_system.AnimationsEnabled ? _previewFrame : _previewPoints.Count - 1];
            Dot(117 + position.Dx * 0.8, 117 + position.Dy * 0.8, 3.5, vector, null);
        }
        Dot(117, 117, 2.4, axes, null);
        Dot(hx, hy, 7, new SolidColorBrush(dark ? Color.FromArgb(255, 33, 37, 45) : Colors.White), vector);
        PadCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 234, 234) };
    }
    private void Line(double x1, double y1, double x2, double y2, Brush brush, double thickness) => PadCanvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness });
    private void Dot(double x, double y, double radius, Brush fill, Brush? stroke)
    { var dot = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Stroke = stroke, StrokeThickness = stroke is null ? 0 : 2.5 }; Canvas.SetLeft(dot, x - radius); Canvas.SetTop(dot, y - radius); PadCanvas.Children.Add(dot); }
    private static Color ParseColor(string hex) => Color.FromArgb(255, byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture(); _runtime.Invalidate();
        var model = new ComboBox { Header = "Motion model", HorizontalAlignment = HorizontalAlignment.Stretch };
        model.Items.Add("Smooth"); model.Items.Add("Organic"); model.SelectedIndex = (int)Current.Motion.Model;
        var rate = new NumberBox { Header = "Maximum component rate (relative input units/s)", Minimum = 1, Maximum = 1000, Value = Math.Clamp(Current.Motion.MaxComponentRate, 1, 1000), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var backend = new ComboBox { Header = "Input method", HorizontalAlignment = HorizontalAlignment.Stretch };
        backend.Items.Add("Windows (SendInput)"); backend.Items.Add("Arduino Leonardo (USB HID)");
        backend.SelectedIndex = (int)_configuration.AppSettings.InputBackend;
        var port = new ComboBox { Header = "Arduino COM port", IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, Text = _configuration.AppSettings.ArduinoPort };
        var refresh = new Button { Content = "Refresh ports" };
        void RefreshPorts()
        {
            var selected = port.Text;
            try { port.ItemsSource = AvailablePorts(); port.Text = selected; }
            catch (Exception ex) { ShowNotice("Port discovery failed: " + ex.Message, InfoBarSeverity.Warning); }
        }
        refresh.Click += (_, _) => RefreshPorts(); RefreshPorts();
        var portPanel = new StackPanel { Spacing = 8 };
        portPanel.Children.Add(port); portPanel.Children.Add(refresh);
        portPanel.Children.Add(new TextBlock { Text = "Waits for the selected port while offline. Requires MouseControl Leonardo firmware.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 });
        void UpdatePortVisibility() => portPanel.Visibility = backend.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        backend.SelectionChanged += (_, _) => UpdatePortVisibility(); UpdatePortVisibility();
        var folder = new Button { Content = "Open data folder" };
        folder.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _dataFolder, UseShellExecute = true }); } catch (Exception ex) { ShowNotice("Data folder could not be opened: " + ex.Message, InfoBarSeverity.Warning); } };
        var panel = new StackPanel { Spacing = 14 }; foreach (var element in new UIElement[] { model, rate, backend, portPanel, folder }) panel.Children.Add(element);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Settings", Content = new ScrollViewer { Content = panel, MaxHeight = 390 }, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!double.IsFinite(rate.Value) || rate.Value < 1 || rate.Value > 1000) args.Cancel = true;
            if (backend.SelectedIndex == 1 && !ProfileConfiguration.IsArduinoPortName(port.Text.Trim()))
            { port.Header = "Enter a COM port, for example COM5"; args.Cancel = true; }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ReplaceProfile(Current with { Motion = Current.Motion with { Model = (MotionModel)model.SelectedIndex, MaxComponentRate = rate.Value } });
            var kind = (InputBackendKind)backend.SelectedIndex;
            var selectedPort = kind == InputBackendKind.Arduino ? port.Text.Trim().ToUpperInvariant() : _configuration.AppSettings.ArduinoPort;
            if (kind != _configuration.AppSettings.InputBackend || selectedPort != _configuration.AppSettings.ArduinoPort)
                SelectInputBackend(kind, selectedPort);
        }
    }
    private void OnShown() { _hidden = false; CancelCapture(); _runtime.Invalidate(); RebuildPreview(); }
    private void OnHidden() { _hidden = true; CancelCapture(); _runtime.Invalidate(); _previewTimer.Stop(); }
    private void Tray_Click(object sender, RoutedEventArgs e) { _bridge.Hide(); if (_bridge.TrayAvailable) OnHidden(); else ShowNotice(_bridge.AvailabilityError, InfoBarSeverity.Warning); }
    private void MarkDirty() { _dirty = true; _saveTimer.Stop(); _saveTimer.Start(); RefreshStatus(); }
    private void Save()
    {
        _saveTimer.Stop(); if (!_dirty) return;
        var result = _store.Save(_configuration);
        if (result.Success) _dirty = false;
        else ShowNotice("Settings could not be saved. Changes remain in this session. " + result.Error, InfoBarSeverity.Error);
    }
    private void ShowNotice(string message, InfoBarSeverity severity, ButtonBase? action = null) { Notice.ActionButton = action; Notice.Message = message; Notice.Severity = severity; Notice.IsOpen = true; }
    private void RequestExit() { _runtime.SetEnabled(false); Save(); if (_dirty) { _bridge.Show(); OnShown(); return; } Close(); }
    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (_closed) return; _closed = true;
        _previewTimer.Stop(); _saveTimer.Stop(); _connectionTimer.Stop();
        lock (_backendSetupLock) { Interlocked.Increment(ref _backendRevision); _runtime.SetEnabled(false); _runtime.Dispose(); }
        _bridge.Dispose();
        _system.ColorValuesChanged -= System_ColorValuesChanged;
        Save();
    }

#if DEBUG
    private async Task SmokeTest()
    {
        await Task.Delay(400);
        var output = Environment.GetEnvironmentVariable("MOUSECONTROL_SMOKE_OUTPUT") ?? System.IO.Path.Combine(_dataFolder, "smoke-result.json");
        try
        {
            SetVector(0.18, 0.42);
            ReplaceProfile(Current with { Motion = Current.Motion with { Humanization = 0.35 } });
            if (HorizontalNumber.Value != 18 || VerticalNumber.Value != 42 || HumanSlider.Value != 35) throw new Exception("Slider synchronization failed.");
            var original = Current.Id; AddProfile(Current); var created = Current.Id;
            if (created == original || _configuration.Profiles.Count != 2) throw new Exception("Profile creation failed.");
            SelectProfile(original);
            VerticalNumber.Value = -23;
            if (Math.Abs(Current.Motion.Y + 0.23) > 1e-9) throw new Exception("Numeric field synchronization failed.");
            Save();
            if (!_store.Load().Success) throw new Exception("Profile roundtrip failed.");
            _bridge.Hide(); OnHidden(); _bridge.Show(); OnShown();
            SetVector(0.18, 0.42);
            if (!FormatHotkey(new GlobalHotkey((int)VirtualKey.F8, HotkeyModifiers.Win)).StartsWith("Win + ", StringComparison.Ordinal)) throw new Exception("Hotkey display failed.");
            _sessionSuspended = true; _capturing = true; CancelCapture();
            if (!_sessionSuspended || _capturing) throw new Exception("Independent suspension failed.");
            _sessionSuspended = false; ApplySuspension();
            if (!SelectInputBackend(InputBackendKind.Arduino, "COM65535")) throw new Exception("Offline backend selection failed.");
            _runtime.SetEnabled(true);
            if (_runtime.Enabled || ArduinoStatus.Visibility != Visibility.Visible || ArduinoStatusText.Text != "Offline") throw new Exception("Disconnected Arduino must remain disabled.");
            if (!SelectInputBackend(InputBackendKind.Windows, "COM65535") || ArduinoStatus.Visibility != Visibility.Collapsed) throw new Exception("Windows backend restoration failed.");
            var captureHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            NativeScreenshot.RaiseForCapture(captureHwnd);
            var screenshot = System.IO.Path.ChangeExtension(output, ".png");
            try
            {
                await Task.Delay(500);
                var capture = NativeScreenshot.Capture(captureHwnd);
                await SaveScreenshot(capture.Pixels, capture.Width, capture.Height, screenshot);
            }
            finally { NativeScreenshot.RestoreAfterCapture(captureHwnd); }
            File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { passed = true, simulation = true, synchronizedControls = true, createdProfile = true, numericEdit = true, profileRoundtrip = true, offlineArduinoBlocked = true, windowsBackendRestored = true, trayAvailable = _bridge.TrayAvailable, screenshot }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!); File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = ex.ToString() })); }
        finally { Close(); }
    }
    private static async Task SaveScreenshot(byte[] bytes, int width, int height, string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(System.IO.Path.GetDirectoryName(path)!);
        var file = await folder.CreateFileAsync(System.IO.Path.GetFileName(path), Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, bytes);
        await encoder.FlushAsync();
    }
#endif
}
