using System.Diagnostics;
using DiscordNative.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DiscordNative.App.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsService _settings = App.Settings;
    private bool _isInitializing = true;

    public SettingsWindow()
    {
        InitializeComponent();
        Title = "Diswin — Paramètres";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        SetTitleBar(AppTitleBar);
        SetWindowIcon();

        LoadCurrentSettings();
        _isInitializing = false;
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
                MainWindow.ApplyWin32WindowIcon(hWnd, iconPath);
            }
        }
        catch { }
    }

    private void LoadCurrentSettings()
    {
        var current = _settings.Current;

        ThemeCombo.SelectedItem = ThemeCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == current.ThemeMode) ?? ThemeCombo.Items[0];

        LanguageCombo.SelectedItem = LanguageCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == current.Language) ?? LanguageCombo.Items[0];

        UpdateChannelCombo.SelectedItem = UpdateChannelCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == current.UpdateChannel) ?? UpdateChannelCombo.Items[0];

        ScaleSlider.Value = Math.Clamp(current.InterfaceScale, 0.85, 1.25);
        BubbleStyleToggle.IsOn = current.BubbleMessageStyle;
        CompactModeToggle.IsOn = current.CompactMode;
        AnonymizeUploadsToggle.IsOn = current.AnonymizeUploads;
        ConvertLongMessagesToggle.IsOn = current.ConvertLongMessagesToFile;
        KrispToggle.IsOn = current.EnableKrispNoiseSuppression;
        UpdateKrispStatusText();
        OpacitySlider.Value = current.BackgroundOpacity;
        BlurSlider.Value = current.BackgroundBlurRadius;

        if (!string.IsNullOrEmpty(current.BackgroundImagePath) && System.IO.File.Exists(current.BackgroundImagePath))
        {
            BackgroundPreview.Source = new BitmapImage(new Uri(current.BackgroundImagePath));
        }
    }

    private void UpdateKrispStatusText()
    {
        if (KrispEngineStatusText is null) return;
        // Ensure Krisp processor static check has run
        using var _ = new DiscordNative.Protocol.Voice.KrispAudioProcessor();
        bool nativeOk = DiscordNative.Protocol.Voice.KrispAudioProcessor.IsNativeKrispLoaded;
        string engineDesc = DiscordNative.Protocol.Voice.KrispAudioProcessor.LoadedEngineDescription;

        if (!_settings.Current.EnableKrispNoiseSuppression)
        {
            KrispEngineStatusText.Text = "Krisp est actuellement désactivé.";
        }
        else if (nativeOk)
        {
            KrispEngineStatusText.Text = $"✓ Moteur actif : {engineDesc}";
        }
        else
        {
            KrispEngineStatusText.Text = "✓ Filtre de suppression de bruit adaptatif actif (DSP 48kHz)";
        }
    }

    private void OnKrispToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.Current.EnableKrispNoiseSuppression = KrispToggle.IsOn;
        _settings.Save();
        UpdateKrispStatusText();
        if (App.MainAppWindow is MainWindow mw && mw.Content is MainWindowContent mwc && mwc.ViewModel is not null)
        {
            mwc.ViewModel.SetKrispEnabled(KrispToggle.IsOn);
        }
    }

    private async void OnChooseBackgroundClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".webp");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var path = await _settings.ImportBackgroundImageAsync(file);
        BackgroundPreview.Source = new BitmapImage(new Uri(path));
    }

    private void OnResetBackgroundClicked(object sender, RoutedEventArgs e)
    {
        _settings.ClearBackgroundImage();
        BackgroundPreview.Source = null;
    }

    private void OnScaleChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.Current.InterfaceScale = e.NewValue;
        _settings.Save();
    }

    private void OnBubbleStyleToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.Current.BubbleMessageStyle = BubbleStyleToggle.IsOn;
        _settings.Save();
    }

    private void OnAnonymizeUploadsToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.Current.AnonymizeUploads = AnonymizeUploadsToggle.IsOn;
        _settings.Save();
    }

    private void OnConvertLongMessagesToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.Current.ConvertLongMessagesToFile = ConvertLongMessagesToggle.IsOn;
        _settings.Save();
    }

    private void OnUpdateChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (UpdateChannelCombo.SelectedItem is ComboBoxItem item)
        {
            _settings.Current.UpdateChannel = (string)item.Tag;
            _settings.Save();
        }
    }

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateCheckStatusText.Text = "Recherche de mises à jour sur GitHub...";
        try
        {
            var update = await UpdateService.CheckForUpdatesAsync();
            if (update is not null)
            {
                UpdateCheckStatusText.Text = $"Nouvelle version disponible : {update.TagName}";
                if (App.MainAppWindow is MainWindow mw && mw.Content is MainWindowContent mwc)
                {
                    mwc.SetAvailableUpdate(update);
                }

                var dlg = new ContentDialog
                {
                    Title = $"Mise à jour disponible — {update.TagName}",
                    Content = $"Une nouvelle version ({update.TagName}) est disponible.\nDiswin est open-source : la mise à jour est facultative. Voulez-vous mettre à jour et redémarrer maintenant ?",
                    PrimaryButtonText = "Mettre à jour et redémarrer",
                    CloseButtonText = "Plus tard",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Content.XamlRoot
                };
                if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                {
                    await UpdateService.ApplyAutomaticUpdateAsync(update, status =>
                    {
                        Protocol.App.RunOnUiThread(() => UpdateCheckStatusText.Text = status);
                    });
                }
            }
            else
            {
                UpdateCheckStatusText.Text = $"Diswin est à jour ({UpdateService.CurrentVersion}).";
            }
        }
        catch
        {
            UpdateCheckStatusText.Text = "Impossible de vérifier les mises à jour.";
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void OnExportSettingsClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedFileName = "diswin-settings";
        picker.FileTypeChoices.Add("JSON Settings", new List<string> { ".json" });

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            _settings.ExportSettings(file.Path);
        }
    }

    private async void OnImportSettingsClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".json");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            _settings.ImportSettings(file.Path);
            _isInitializing = true;
            LoadCurrentSettings();
            _isInitializing = false;
        }
    }

    private void OnOpenLogsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log");
            if (!System.IO.File.Exists(logPath))
            {
                System.IO.File.WriteAllText(logPath, $"[{DateTime.Now}] Diswin Diagnostics Log Initialized.\n");
            }
            Process.Start(new ProcessStartInfo("notepad.exe", logPath) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.Current.BackgroundOpacity = e.NewValue;
        _settings.Save();
    }

    private void OnBlurChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.Current.BackgroundBlurRadius = e.NewValue;
        _settings.Save();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem item)
        {
            _settings.Current.ThemeMode = (string)item.Tag;
            _settings.Save();

            var theme = _settings.Current.ThemeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            if (Content is FrameworkElement root) root.RequestedTheme = theme;
            if (App.MainAppWindow?.Content is FrameworkElement mainRoot) mainRoot.RequestedTheme = theme;
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item)
        {
            var lang = (string)item.Tag;
            _settings.Current.Language = lang;
            _settings.Save();

            try
            {
                if (lang != "Default")
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
                    System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(lang);
                    System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(lang);
                }
                else
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
                }
            }
            catch { }
        }
    }

    private void OnCompactModeToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.Current.CompactMode = CompactModeToggle.IsOn;
        _settings.Save();
    }

    private async void OnLogoutClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Se déconnecter ?",
            Content = "Vous devrez ressaisir vos identifiants pour vous reconnecter.",
            PrimaryButtonText = "Se déconnecter",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _settings.ClearToken();
            var loginWindow = new LoginWindow();
            loginWindow.Activate();
            App.MainAppWindow = loginWindow;
            Close();
        }
    }
}
