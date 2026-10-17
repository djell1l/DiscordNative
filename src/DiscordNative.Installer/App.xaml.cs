using Microsoft.UI.Xaml;

namespace DiscordNative.Installer;

public partial class App : Application
{
    private Window? _mainWindow;

    public App()
    {
        RequestedTheme = ApplicationTheme.Dark;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}
