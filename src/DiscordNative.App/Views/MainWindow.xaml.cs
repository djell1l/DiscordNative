using DiscordNative.App.ViewModels;
using DiscordNative.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiscordNative.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly DiscordClient _client;
    public MainViewModel ViewModel { get; }

    public MainWindow(string token)
    {
        InitializeComponent();
        Title = "Diswin";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        SetWindowIcon();

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.Settings.Current.ThemeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        _client = new DiscordClient(token);
        ViewModel = new MainViewModel(_client);
        RootContent.ViewModel = ViewModel;

        SetTitleBar(RootContent.TitleBarElement);

        Activated += OnWindowActivatedFirstTime;
    }

    private void SetWindowIcon()
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
                ApplyWin32WindowIcon(hWnd, iconPath);
            }
        }
        catch { }
    }

    internal static void ApplyWin32WindowIcon(IntPtr hWnd, string iconPath)
    {
        try
        {
            const uint IMAGE_ICON = 1;
            const uint LR_LOADFROMFILE = 0x00000010;
            const uint WM_SETICON = 0x0080;
            IntPtr hIconSmall = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            IntPtr hIconBig = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 48, 48, LR_LOADFROMFILE);
            if (hIconSmall != IntPtr.Zero) SendMessageW(hWnd, WM_SETICON, (IntPtr)0, hIconSmall);
            if (hIconBig != IntPtr.Zero) SendMessageW(hWnd, WM_SETICON, (IntPtr)1, hIconBig);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private async void OnWindowActivatedFirstTime(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnWindowActivatedFirstTime;
        Closed += (_, _) => App.Trace("MainWindow fermée");
        try
        {
            await RootContent.CheckUpdatesDuringSplashAsync();
            RootContent.UpdateSplashStatus("Connexion à Discord...");
            App.Trace("StartAsync...");
            await _client.StartAsync();
            App.Trace($"StartAsync OK: user={_client.CurrentUser?.DisplayName} guilds={_client.Guilds.Count} dms={_client.PrivateChannels.Count} friends={_client.Friends.Count}");
            _client.ConnectionStateChanged += s => App.Trace($"Gateway: {s} guilds={_client.Guilds.Count}");
            ViewModel.OnUserLoaded();
            RootContent.RefreshBackground();
            
            if (_client.PrivateChannels.Count > 0)
            {
                ViewModel.ShowFriendsView();
                _ = ViewModel.SelectChannelAsync(_client.PrivateChannels[0]);
            }

            RootContent.UpdateSplashStatus("Prêt !");
            await RootContent.DismissSplashAsync();
        }
        catch (Exception ex)
        {
            App.LogCrash("StartAsync", ex);
            var dialog = new ContentDialog
            {
                Title = "Connexion impossible",
                Content = $"Le token enregistré semble invalide ou expiré : {ex.Message}",
                CloseButtonText = "Se reconnecter",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
            App.Settings.ClearToken();
            var loginWindow = new LoginWindow();
            loginWindow.Activate();
            App.MainAppWindow = loginWindow;
            Close();
        }
    }
}
