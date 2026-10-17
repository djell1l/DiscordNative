using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.Storage.Pickers;

namespace DiscordNative.Installer;

public sealed partial class MainWindow : Window
{
    private readonly string _defaultDir;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Installateur Diswin";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        SetTitleBar(AppTitleBar);
        ConfigureWindow();

        _defaultDir = GetExistingInstallLocation()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Diswin");
        InstallPathBox.Text = _defaultDir;
        InstallPathBox.TextChanged += (_, _) => UpdateExistingInstallState();
        UpdateExistingInstallState();
    }

    private void ConfigureWindow()
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(620, 500));

            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
            }

            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
            }

            // Force Windows 11 DWM rounded corners (DWMWCP_ROUND = 2)
            int cornerPreference = 2;
            DwmSetWindowAttribute(hWnd, 33, ref cornerPreference, sizeof(int));

            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
                const uint IMAGE_ICON = 1;
                const uint LR_LOADFROMFILE = 0x00000010;
                const uint WM_SETICON = 0x0080;
                IntPtr hIconSmall = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                IntPtr hIconBig = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 48, 48, LR_LOADFROMFILE);
                if (hIconSmall != IntPtr.Zero) SendMessageW(hWnd, WM_SETICON, (IntPtr)0, hIconSmall);
                if (hIconBig != IntPtr.Zero) SendMessageW(hWnd, WM_SETICON, (IntPtr)1, hIconBig);
            }
        }
        catch { }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private static string? GetExistingInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Diswin");
            var loc = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(loc) && Directory.Exists(loc))
                return loc;
        }
        catch { }
        return null;
    }

    private void UpdateExistingInstallState()
    {
        try
        {
            var dir = InstallPathBox.Text.Trim();
            bool isInstalled = !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "DiscordNative.App.exe"));
            if (isInstalled)
            {
                HeaderTitleText.Text = "Diswin est déjà installé";
                HeaderSubtitleText.Text = "Vous pouvez réinstaller ou désinstaller Diswin.";
                UninstallButton.Visibility = Visibility.Visible;
                InstallButton.Content = "Réinstaller";
            }
            else
            {
                HeaderTitleText.Text = "Installation de Diswin";
                HeaderSubtitleText.Text = "Client Discord natif et ultra-léger pour Windows 11";
                UninstallButton.Visibility = Visibility.Collapsed;
                InstallButton.Content = "Installer maintenant";
            }
        }
        catch { }
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            InstallPathBox.Text = folder.Path;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnUninstallClicked(object sender, RoutedEventArgs e)
    {
        var targetDir = InstallPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDir)) return;

        SetControlsEnabled(false);
        InstallProgress.Visibility = Visibility.Visible;
        InstallProgress.IsIndeterminate = true;
        StatusText.Text = "Désinstallation de Diswin en cours...";

        try
        {
            await Task.Run(() => RunUninstallation(targetDir));

            InstallProgress.IsIndeterminate = false;
            InstallProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = "Diswin a été désinstallé avec succès.";
            UninstallButton.Visibility = Visibility.Collapsed;
            HeaderTitleText.Text = "Désinstallation terminée";
            HeaderSubtitleText.Text = "Tous les fichiers et raccourcis de Diswin ont été supprimés.";
            InstallButton.Content = "Fermer";
            InstallButton.IsEnabled = true;
            InstallButton.Click -= OnInstallClicked;
            InstallButton.Click += (_, _) => Close();
        }
        catch (Exception ex)
        {
            InstallProgress.IsIndeterminate = false;
            InstallProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            SetControlsEnabled(true);
        }
    }

    private static void RunUninstallation(string targetDir)
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("DiscordNative.App"))
            {
                try { proc.Kill(); proc.WaitForExit(2000); } catch { }
            }
        }
        catch { }

        if (Directory.Exists(targetDir))
        {
            try { Directory.Delete(targetDir, recursive: true); } catch { }
        }

        // Supprimer les raccourcis Bureau et Menu Démarrer
        try
        {
            var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Diswin.lnk");
            if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut);
        }
        catch { }

        try
        {
            var startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Diswin.lnk");
            if (File.Exists(startMenuShortcut)) File.Delete(startMenuShortcut);
        }
        catch { }

        // Supprimer du démarrage automatique et de Ajout/Suppression de programmes
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue("Diswin", throwOnMissingValue: false);
        }
        catch { }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Diswin", throwOnMissingSubKey: false);
        }
        catch { }
    }

    private void SetControlsEnabled(bool enabled)
    {
        InstallButton.IsEnabled = enabled;
        UninstallButton.IsEnabled = enabled;
        BrowseButton.IsEnabled = enabled;
        InstallPathBox.IsEnabled = enabled;
        DesktopShortcutCheck.IsEnabled = enabled;
        StartMenuCheck.IsEnabled = enabled;
        StartupCheck.IsEnabled = enabled;
    }

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        var targetDir = InstallPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDir)) return;

        // Lire toutes les propriétés UI sur le thread principal AVANT Task.Run !
        bool createDesktopShortcut = DesktopShortcutCheck.IsChecked == true;
        bool createStartMenuShortcut = StartMenuCheck.IsChecked == true;
        bool launchAtStartup = StartupCheck.IsChecked == true;

        SetControlsEnabled(false);

        InstallProgress.Visibility = Visibility.Visible;
        InstallProgress.IsIndeterminate = false;
        InstallProgress.Value = 0;
        StatusText.Text = "Installation en cours...";

        var progress = new Progress<(double percent, string status)>(report =>
        {
            InstallProgress.Value = report.percent;
            StatusText.Text = report.status;
        });

        try
        {
            await Task.Run(() => RunInstallation(targetDir, createDesktopShortcut, createStartMenuShortcut, launchAtStartup, progress));

            InstallProgress.Value = 100;
            StatusText.Text = "Installation terminée avec succès !";
            InstallProgress.Visibility = Visibility.Collapsed;
            UninstallButton.Visibility = Visibility.Collapsed;
            HeaderTitleText.Text = "Installation terminée !";
            HeaderSubtitleText.Text = "Diswin est prêt à être utilisé.";
            InstallButton.Content = "Lancer Diswin";
            InstallButton.IsEnabled = true;
            InstallButton.Click -= OnInstallClicked;
            InstallButton.Click += (_, _) =>
            {
                var exe = Path.Combine(targetDir, "DiscordNative.App.exe");
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo(exe)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = targetDir
                    });
                }
                Close();
            };
        }
        catch (Exception ex)
        {
            StatusText.Text = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            InstallProgress.Visibility = Visibility.Collapsed;
            SetControlsEnabled(true);
        }
    }

    private static void RunInstallation(
        string targetDir,
        bool createDesktopShortcut,
        bool createStartMenuShortcut,
        bool launchAtStartup,
        IProgress<(double percent, string status)> progress)
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("DiscordNative.App"))
            {
                try { proc.Kill(); proc.WaitForExit(2000); } catch { }
            }
        }
        catch { }

        Directory.CreateDirectory(targetDir);

        var payloadZip = Path.Combine(AppContext.BaseDirectory, "Payload.zip");
        var baseSource = Path.Combine(AppContext.BaseDirectory, "Payload");

        if (File.Exists(payloadZip))
        {
            using var archive = ZipFile.OpenRead(payloadZip);
            int total = Math.Max(1, archive.Entries.Count);
            int current = 0;

            foreach (var entry in archive.Entries)
            {
                current++;
                var destPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                if (!destPath.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                    continue;
                }

                var parent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                entry.ExtractToFile(destPath, overwrite: true);

                if (current % 10 == 0 || current == total)
                {
                    double pct = Math.Round((double)current / total * 100.0);
                    progress.Report((pct, $"Installation en cours... ({pct:0}%)"));
                }
            }
        }
        else if (Directory.Exists(baseSource))
        {
            var files = Directory.GetFiles(baseSource, "*.*", SearchOption.AllDirectories);
            int total = Math.Max(1, files.Length);
            int current = 0;

            foreach (var file in files)
            {
                current++;
                var relative = Path.GetRelativePath(baseSource, file);
                var destination = Path.Combine(targetDir, relative);
                var parent = Path.GetDirectoryName(destination);
                if (parent != null) Directory.CreateDirectory(parent);
                File.Copy(file, destination, overwrite: true);

                if (current % 10 == 0 || current == total)
                {
                    double pct = Math.Round((double)current / total * 100.0);
                    progress.Report((pct, $"Installation en cours... ({pct:0}%)"));
                }
            }
        }
        else if (File.Exists(Path.Combine(AppContext.BaseDirectory, "DiscordNative.App.exe")))
        {
            var currentDir = AppContext.BaseDirectory;
            var excludedRootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Setup.exe", "Setup.dll", "Setup.pdb", "Setup.pri", "Setup.deps.json", "Setup.runtimeconfig.json",
                "MainWindow.xbf", "AppIcon.png"
            };

            var files = Directory.GetFiles(currentDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var rel = Path.GetRelativePath(currentDir, f);
                    if (rel.StartsWith("AppXbf" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return false;
                    // Only exclude Setup files and root-level AppIcon.png (preserve Assets\AppIcon.png!)
                    if (!rel.Contains(Path.DirectorySeparatorChar) && excludedRootNames.Contains(Path.GetFileName(f)))
                        return false;
                    return true;
                })
                .ToArray();

            int total = Math.Max(1, files.Length);
            int current = 0;

            foreach (var file in files)
            {
                current++;
                var relative = Path.GetRelativePath(currentDir, file);
                var destination = Path.Combine(targetDir, relative);
                var parent = Path.GetDirectoryName(destination);
                if (parent != null) Directory.CreateDirectory(parent);
                File.Copy(file, destination, overwrite: true);

                if (current % 10 == 0 || current == total)
                {
                    double pct = Math.Round((double)current / total * 100.0);
                    progress.Report((pct, $"Installation en cours... ({pct:0}%)"));
                }
            }

            var appXbfDir = Path.Combine(currentDir, "AppXbf");
            if (Directory.Exists(appXbfDir))
            {
                foreach (var file in Directory.GetFiles(appXbfDir, "*.*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(appXbfDir, file);
                    var destination = Path.Combine(targetDir, relative);
                    var parent = Path.GetDirectoryName(destination);
                    if (parent != null) Directory.CreateDirectory(parent);
                    File.Copy(file, destination, overwrite: true);
                }
            }
        }
        else
        {
            throw new DirectoryNotFoundException("Fichiers d'installation introuvables (Payload).");
        }

        var mainExe = Path.Combine(targetDir, "DiscordNative.App.exe");
        var iconPath = Path.Combine(targetDir, "app.ico");

        if (createDesktopShortcut)
        {
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(mainExe, Path.Combine(desktopPath, "Diswin.lnk"), iconPath);
            }
            catch { }
        }

        if (createStartMenuShortcut)
        {
            try
            {
                var startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                Directory.CreateDirectory(startMenuPath);
                CreateShortcut(mainExe, Path.Combine(startMenuPath, "Diswin.lnk"), iconPath);
            }
            catch { }
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                if (launchAtStartup)
                {
                    key.SetValue("Diswin", $"\"{mainExe}\"");
                }
                else
                {
                    key.DeleteValue("Diswin", throwOnMissingValue: false);
                }
            }
        }
        catch { }

        RegisterUninstaller(targetDir, mainExe, iconPath);
    }

    private static void RegisterUninstaller(string targetDir, string mainExe, string iconPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Diswin");
            if (key == null) return;
            key.SetValue("DisplayName", "Diswin");
            key.SetValue("DisplayVersion", "0.4.3");
            key.SetValue("Publisher", "Diswin");
            key.SetValue("InstallLocation", targetDir);
            key.SetValue("DisplayIcon", File.Exists(iconPath) ? iconPath : mainExe);
            key.SetValue("UninstallString", $"cmd.exe /c rmdir /s /q \"{targetDir}\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void CreateShortcut(string targetPath, string shortcutPath, string iconPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(targetPath);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
            link.SetDescription("Diswin - Client Discord natif pour Windows 11");
            if (File.Exists(iconPath))
            {
                link.SetIconLocation(iconPath, 0);
            }
            var file = (IPersistFile)link;
            file.Save(shortcutPath, false);
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
