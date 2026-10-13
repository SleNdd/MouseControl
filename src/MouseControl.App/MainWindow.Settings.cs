using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MouseControl.Core;
using Windows.System;

namespace MouseControl.App;

public sealed partial class MainWindow
{
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        CancelCapture(); _runtime.Invalidate();
        var settings = _configuration.AppSettings;
        var backend = new ComboBox { Header = "Input method", HorizontalAlignment = HorizontalAlignment.Stretch };
        backend.Items.Add("Windows (SendInput)"); backend.Items.Add("Arduino Leonardo (USB HID)");
        backend.SelectedIndex = (int)settings.InputBackend;
        var selectedPort = settings.ArduinoPort;
        var port = new ComboBox { Header = "Arduino COM port", IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, Text = selectedPort };
        port.SelectionChanged += (_, _) => { if (port.SelectedItem is string value) selectedPort = value; };
        port.TextSubmitted += (_, args) => { selectedPort = args.Text; args.Handled = true; };
        port.Loaded += (_, _) =>
        {
            var editor = FindTemplatePart<TextBox>(port, "PART_EditableTextBox");
            if (editor is not null) editor.TextChanged += (_, _) => selectedPort = editor.Text;
        };
        var refresh = new Button { Content = "Refresh ports" };
        void RefreshPorts()
        {
            var retained = selectedPort;
            try { port.ItemsSource = AvailablePorts(); port.Text = retained; selectedPort = retained; }
            catch (Exception ex) { ShowNotice("Port discovery failed: " + ex.Message, InfoBarSeverity.Warning); }
        }
        refresh.Click += (_, _) => RefreshPorts(); RefreshPorts();
        var portPanel = new StackPanel { Spacing = 8 };
        portPanel.Children.Add(port); portPanel.Children.Add(refresh);
        portPanel.Children.Add(new TextBlock { Text = _connectionError ?? "Select the board's current COM port. Offline connections are retried automatically.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 });
        void UpdatePortVisibility() => portPanel.Visibility = backend.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        backend.SelectionChanged += (_, _) => UpdatePortVisibility(); UpdatePortVisibility();

        var increase = settings.EffectiveVerticalIncreaseHotkey;
        var decrease = settings.EffectiveVerticalDecreaseHotkey;
        var increaseButton = new Button { Content = FormatHotkey(increase), HorizontalAlignment = HorizontalAlignment.Stretch };
        var decreaseButton = new Button { Content = FormatHotkey(decrease), HorizontalAlignment = HorizontalAlignment.Stretch };
        Button? capturing = null;
        void BeginCapture(Button button) { if (capturing is not null) capturing.Content = FormatHotkey(capturing == increaseButton ? increase : decrease); capturing = button; button.Content = "Press a key…"; button.Focus(FocusState.Programmatic); }
        increaseButton.Click += (_, _) => BeginCapture(increaseButton);
        decreaseButton.Click += (_, _) => BeginCapture(decreaseButton);
        var step = new NumberBox { Header = "Vertical step (units/s)", Value = settings.VerticalStep, Minimum = 1, Maximum = 2000, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
        var hotkeys = new StackPanel { Spacing = 8 };
        hotkeys.Children.Add(new TextBlock { Text = "Vertical +", FontSize = 12 }); hotkeys.Children.Add(increaseButton);
        hotkeys.Children.Add(new TextBlock { Text = "Vertical −", FontSize = 12 }); hotkeys.Children.Add(decreaseButton); hotkeys.Children.Add(step);
        hotkeys.Children.Add(new TextBlock { Text = "Add or subtract this step from the active profile's signed vertical speed.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.65 });
        var rates = new[] { 125, 250, 500, 1000 };
        var outputRate = new ComboBox { Header = "Requested output rate", ItemsSource = rates.Select(rate => $"{rate} Hz").ToArray(), SelectedIndex = Array.IndexOf(rates, settings.OutputRateHz), HorizontalAlignment = HorizontalAlignment.Stretch };
        var startInTray = new CheckBox { Content = "Start in tray", IsChecked = settings.StartInTray };
        var closeToTray = new CheckBox { Content = "Close button minimizes to tray", IsChecked = settings.CloseToTray };
        var sound = new CheckBox { Content = "Sound notifications", IsChecked = settings.SoundNotifications };
        var notifications = new CheckBox { Content = "Desktop notifications", IsChecked = settings.DesktopNotifications };
        var folder = new Button { Content = "Open data folder" };
        folder.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _dataFolder, UseShellExecute = true }); } catch (Exception ex) { ShowNotice("Data folder could not be opened: " + ex.Message, InfoBarSeverity.Warning); } };
        string ReadLicense(string name) { using var stream = typeof(MotionSettings).Assembly.GetManifestResourceStream(name); using var reader = stream is null ? null : new StreamReader(stream); return reader?.ReadToEnd() ?? "See THIRD_PARTY_NOTICES.md."; }
        var license = new Expander { Header = "Third-party licenses", HorizontalAlignment = HorizontalAlignment.Stretch, Content = new TextBlock { Text = ReadLicense("MouseControl.GhostCursorSharp.License") + "\n\n" + ReadLicense("MouseControl.GhostCursor.License"), TextWrapping = TextWrapping.Wrap, FontSize = 11 } };
        var panel = new StackPanel { Spacing = 14 };
        foreach (var element in new UIElement[] { backend, portPanel, hotkeys, startInTray, closeToTray, sound, notifications, outputRate, folder, license }) panel.Children.Add(element);
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed) };
        panel.Children.Insert(0, errorText);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Settings", Content = new ScrollViewer { Content = panel, MaxHeight = 390 }, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.KeyDown += (_, args) =>
        {
            if (capturing is null) return;
            args.Handled = true;
            if (args.Key == VirtualKey.Escape) { capturing.Content = FormatHotkey(capturing == increaseButton ? increase : decrease); capturing = null; return; }
            if (args.Key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
            var modifiers = HotkeyModifiers.None;
            if (Native.Down(0x11)) modifiers |= HotkeyModifiers.Control;
            if (Native.Down(0x12)) modifiers |= HotkeyModifiers.Alt;
            if (Native.Down(0x10)) modifiers |= HotkeyModifiers.Shift;
            if (Native.Down(0x5B) || Native.Down(0x5C)) modifiers |= HotkeyModifiers.Win;
            var candidate = new GlobalHotkey((int)args.Key, modifiers);
            if (capturing == increaseButton) increase = candidate; else decrease = candidate;
            capturing.Content = FormatHotkey(candidate); capturing = null;
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            string? error = null;
            if (capturing is not null) error = "Finish or cancel key capture first.";
            else if (backend.SelectedIndex == 1 && !ProfileConfiguration.IsArduinoPortName(selectedPort.Trim())) error = "Enter a COM port, for example COM5.";
            else if (!double.IsFinite(step.Value) || step.Value < 1 || step.Value > 2000 || step.Value != Math.Truncate(step.Value)) error = "Choose a whole vertical step from 1 to 2000.";
            else if (_configuration.Profiles.Any(profile => BindingCollision(increase, profile.Activation) || BindingCollision(decrease, profile.Activation))) error = "A vertical key conflicts with a profile's activation binding.";
            else if ((!_smoke || _interactiveCheck) && !_bridge.TrySetVerticalHotkeys(increase, decrease, out var registrationError)) error = registrationError;
            if (error is not null) { errorText.Text = error; errorText.Visibility = Visibility.Visible; args.Cancel = true; }
        };
        if (await ShowDialog(dialog) != ContentDialogResult.Primary || _closed) return;
        _configuration = _configuration with { AppSettings = _configuration.AppSettings with
        {
            VerticalIncreaseHotkey = increase, VerticalDecreaseHotkey = decrease, VerticalStep = (int)step.Value,
            StartInTray = startInTray.IsChecked == true, CloseToTray = closeToTray.IsChecked == true,
            SoundNotifications = sound.IsChecked == true, DesktopNotifications = notifications.IsChecked == true
        } };
        MarkDirty();
        var requestedRate = rates[outputRate.SelectedIndex];
        if (requestedRate != _configuration.AppSettings.OutputRateHz)
        {
            _runtime.SetOutputRateHz(requestedRate);
            _configuration = _configuration with { AppSettings = _configuration.AppSettings with { OutputRateHz = requestedRate } };
            MarkDirty();
        }
        var kind = (InputBackendKind)backend.SelectedIndex;
        var committedPort = kind == InputBackendKind.Arduino ? selectedPort.Trim().ToUpperInvariant() : settings.ArduinoPort;
        if (kind != settings.InputBackend || committedPort != settings.ArduinoPort) SelectInputBackend(kind, committedPort);
    }
}
