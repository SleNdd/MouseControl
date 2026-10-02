using Microsoft.UI.Xaml;

namespace MouseControl.App;

public partial class App : Application
{
    private MainWindow? _window;
    private static Mutex? _instance;
    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool smoke = false;
#if DEBUG
        smoke = Environment.GetCommandLineArgs().Contains("--smoke-test");
#endif
        _instance = new Mutex(true, smoke ? "SleNdd.MouseControl.Smoke" : "SleNdd.MouseControl", out bool first);
        if (!first)
        {
            Native.PostMessage((nint)0xffff, Native.RegisterWindowMessage("SleNdd.MouseControl.ShowWindow"), 0, 0);
            Exit(); return;
        }
        try
        {
            _window = new MainWindow(smoke);
            _window.Activate();
            _window.ApplyStartupVisibility();
        }
        catch (Exception ex)
        {
            Native.MessageBox(0, ex.Message, "MouseControl could not start", 0x10);
            Exit();
        }
    }
}
