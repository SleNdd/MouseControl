using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MouseControl.Core;

namespace MouseControl.App;

public sealed partial class MainWindow
{
    private bool _tabDragging;
#if DEBUG
    private ContentDialog? _debugDonationDialog;
#endif
    private string NewProfileColor()
    {
        var used = _configuration.Profiles.Select(profile => profile.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var red = Random.Shared.Next(72, 226); var green = Random.Shared.Next(72, 226); var blue = Random.Shared.Next(72, 226);
            if (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) < 48) continue;
            var color = $"#{red:X2}{green:X2}{blue:X2}";
            if (!used.Contains(color)) return color;
        }
    }

    private void Profile_TabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args) => _tabDragging = true;
    private void Profile_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        _tabDragging = false; PersistTabOrder();
        var previous = _updating; _updating = true;
        ProfileTabs.SelectedItem = ProfileTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(tab => tab.Tag is Guid id && id == Current.Id);
        _updating = previous;
    }

    private void PersistTabOrder()
    {
        if (_updating || _closed) return;
        var ids = ProfileTabs.TabItems.OfType<TabViewItem>().Select(tab => tab.Tag).OfType<Guid>().ToArray();
        if (ids.Length != _configuration.Profiles.Count || ids.Distinct().Count() != ids.Length ||
            ids.Any(id => !_configuration.Profiles.Any(profile => profile.Id == id))) return;
        if (ids.SequenceEqual(_configuration.Profiles.Select(profile => profile.Id))) return;
        var profiles = _configuration.Profiles.ToDictionary(profile => profile.Id);
        _configuration = _configuration with { Profiles = ids.Select(id => profiles[id]).ToArray() };
        MarkDirty();
    }

    private void AdjustVertical(int direction)
    {
        if (_closed || _capturing || _dialogOpen) return;
        var previous = Current.Motion.Y * Services.MotionUi.MaximumSpeed;
        var next = Math.Clamp(previous + Math.Sign(direction) * _configuration.AppSettings.VerticalStep,
            -Services.MotionUi.MaximumSpeed, Services.MotionUi.MaximumSpeed);
        if (next == previous) return;
        SetVector(Current.Motion.X, next / Services.MotionUi.MaximumSpeed);
        EmitFeedback($"Vertical {next:+0.##;-0.##;0} units/s");
    }

    private void EmitFeedback(string status)
    {
        try { _feedback?.Show(Current.Name, Current.Color, status, _configuration.AppSettings.SoundNotifications, _configuration.AppSettings.DesktopNotifications); }
        catch (Exception ex)
        {
            _feedback?.Dispose(); _feedback = null;
            ShowNotice("Desktop feedback is unavailable: " + ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)
    {
        if (_closed || _dialogOpen) return ContentDialogResult.None;
        dialog.RequestedTheme = Root.RequestedTheme;
        _dialogOpen = true; ApplySuspension();
        try { return await dialog.ShowAsync(); }
        finally { _dialogOpen = false; if (!_closed) ApplySuspension(); }
    }

    private async Task ShowDonationPrompt()
    {
        if (!Root.IsLoaded)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLoaded(object sender, RoutedEventArgs args) { Root.Loaded -= OnLoaded; loaded.TrySetResult(); }
            Root.Loaded += OnLoaded;
            await loaded.Task;
        }
        if (_closed) return;
        var description = new TextBlock { Text = "WIP — Voluntary donation information will be added here. Supporting the project is optional.", TextWrapping = TextWrapping.Wrap };
        var countdown = new TextBlock { Text = "You can close this in 5 seconds.", FontSize = 12, Opacity = 0.65 };
        var suppress = new CheckBox { Content = "Don't ask again" };
        var content = new StackPanel { Spacing = 16 }; content.Children.Add(description); content.Children.Add(countdown); content.Children.Add(suppress);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Support MouseControl", Content = content,
            PrimaryButtonText = "Continue", IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary };
#if DEBUG
        _debugDonationDialog = dialog;
#endif
        long started = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var remaining = 5 - Stopwatch.GetElapsedTime(started).TotalSeconds;
            if (remaining <= 0) { dialog.IsPrimaryButtonEnabled = true; countdown.Text = "Thank you for using MouseControl."; timer.Stop(); }
            else countdown.Text = $"You can close this in {Math.Ceiling(remaining):0} seconds.";
        };
        dialog.Opened += (_, _) => { started = Stopwatch.GetTimestamp(); timer.Start(); };
        dialog.Closing += (_, args) => { if (started == 0 || Stopwatch.GetElapsedTime(started).TotalSeconds < 5) args.Cancel = true; };
        try
        {
            await ShowDialog(dialog);
            if (!_closed && suppress.IsChecked == true)
            {
                _configuration = _configuration with { AppSettings = _configuration.AppSettings with { SuppressDonationPrompt = true } };
                MarkDirty(); Save();
            }
        }
        finally
        {
            timer.Stop();
#if DEBUG
            _debugDonationDialog = null;
#endif
        }
    }
}
