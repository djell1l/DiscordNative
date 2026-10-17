using System.Runtime.InteropServices.WindowsRuntime;
using DiscordNative.App.Services;
using DiscordNative.App.ViewModels;
using DiscordNative.Protocol.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.System;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DiscordNative.App.Views;

public sealed partial class MainWindowContent : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(MainViewModel), typeof(MainWindowContent), new PropertyMetadata(null));

    public MainViewModel ViewModel
    {
        get => (MainViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty FullProfileUserProperty =
        DependencyProperty.Register(nameof(FullProfileUser), typeof(DiscordUser), typeof(MainWindowContent), new PropertyMetadata(null));

    public DiscordUser? FullProfileUser
    {
        get => (DiscordUser?)GetValue(FullProfileUserProperty);
        set => SetValue(FullProfileUserProperty, value);
    }

    public FrameworkElement TitleBarElement => AppTitleBar;

    public void RefreshBackground()
    {
        BackgroundHost.ApplyFromSettings();
        ApplyInterfaceScale();
        if (ViewModel != null && ViewModel.IsKrispEnabled != App.Settings.Current.EnableKrispNoiseSuppression)
        {
            ViewModel.SetKrispEnabled(App.Settings.Current.EnableKrispNoiseSuppression);
        }
    }

    public void ApplyInterfaceScale()
    {
        if (RootLayoutGrid is null) return;
        var scale = Math.Clamp(App.Settings.Current.InterfaceScale, 0.85, 1.25);
        RootLayoutGrid.Scale = new System.Numerics.Vector3((float)scale, (float)scale, 1.0f);
    }

    public void UpdateSplashStatus(string text)
    {
        Protocol.App.RunOnUiThread(() =>
        {
            if (SplashStatusText != null)
            {
                SplashStatusText.Text = text;
            }
        });
    }

    public async System.Threading.Tasks.Task DismissSplashAsync()
    {
        await System.Threading.Tasks.Task.Delay(300);
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        Protocol.App.RunOnUiThread(() =>
        {
            if (StartupSplash == null)
            {
                tcs.SetResult(true);
                return;
            }
            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(400))
            };
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            sb.Children.Add(fade);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, StartupSplash);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Completed += (_, _) =>
            {
                StartupSplash.Visibility = Visibility.Collapsed;
                tcs.TrySetResult(true);
            };
            sb.Begin();
        });
        await tcs.Task;
    }

    private UpdateInfo? _availableUpdate;
    private bool _updateCheckDone;

    public MainWindowContent()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                if (AvatarZoomOverlay.Visibility == Visibility.Visible)
                {
                    HideAvatarZoom();
                    e.Handled = true;
                    return;
                }
                if (FullUserProfileOverlay.Visibility == Visibility.Visible)
                {
                    HideFullUserProfileModal();
                    e.Handled = true;
                    return;
                }
                if (ProfileEditorOverlay?.Visibility == Visibility.Visible)
                {
                    HideProfileEditor();
                    e.Handled = true;
                    return;
                }
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInterfaceScale();
        HookMessageListScroll();
    }

    public async System.Threading.Tasks.Task CheckUpdatesDuringSplashAsync()
    {
        if (_updateCheckDone) return;
        _updateCheckDone = true;

        UpdateSplashStatus("Vérification des mises à jour...");
        var update = await UpdateService.CheckForUpdatesAsync();
        if (update is not null)
        {
            SetAvailableUpdate(update);
            UpdateSplashStatus($"Mise à jour {update.TagName} disponible !");
            await System.Threading.Tasks.Task.Delay(500);
        }
    }

    public void SetAvailableUpdate(UpdateInfo update)
    {
        _availableUpdate = update;
        Protocol.App.RunOnUiThread(() =>
        {
            TopLeftUpdateButton.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(TopLeftUpdateButton, $"Mise à jour {update.TagName} disponible ! Cliquez pour mettre à jour ou ignorer.");
            UpdateInfoBar.Title = $"Mise à jour disponible ({update.TagName})";
            UpdateInfoBar.Message = string.IsNullOrWhiteSpace(update.Name)
                ? "Une nouvelle version de Diswin est disponible (optionnelle)."
                : update.Name;
            UpdateInfoBar.IsOpen = true;
        });
    }

    private async void OnTopLeftUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        await ShowUpdatePromptDialogAsync(_availableUpdate);
    }

    private async void OnApplyUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        await ShowUpdatePromptDialogAsync(_availableUpdate);
    }

    public async System.Threading.Tasks.Task ShowUpdatePromptDialogAsync(UpdateInfo update)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Une nouvelle version ({update.TagName}) est disponible sur GitHub.",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(update.Body))
        {
            var notesBorder = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                MaxHeight = 180
            };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = update.Body.Trim(),
                    FontSize = 12,
                    Opacity = 0.85,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            notesBorder.Child = scroll;
            panel.Children.Add(notesBorder);
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Diswin est 100 % open-source : la mise à jour est entièrement facultative. Voulez-vous télécharger la mise à jour et redémarrer l'application maintenant ?",
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        });

        var statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0x5A)),
            Visibility = Visibility.Collapsed
        };
        var progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0)
        };
        panel.Children.Add(statusText);
        panel.Children.Add(progressBar);

        var dialog = new ContentDialog
        {
            Title = $"Mise à jour disponible — {update.TagName}",
            Content = panel,
            PrimaryButtonText = "Mettre à jour et redémarrer",
            CloseButtonText = "Plus tard",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.IsSecondaryButtonEnabled = false;
            statusText.Visibility = Visibility.Visible;
            progressBar.Visibility = Visibility.Visible;

            await UpdateService.ApplyAutomaticUpdateAsync(update, status =>
            {
                Protocol.App.RunOnUiThread(() =>
                {
                    statusText.Text = status;
                    UpdateInfoBar.Message = status;
                });
            });
            deferral.Complete();
        };

        await dialog.ShowAsync();
    }

    private async void OnGuildSelected(object sender, SelectionChangedEventArgs e)
    {
        if (GuildList.SelectedItem is DiscordGuild guild)
        {
            await ViewModel.SelectGuildAsync(guild);
        }
    }

    private async void OnChannelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: DiscordChannel channel })
        {
            HookMessageListScroll();
            await ViewModel.SelectChannelAsync(channel);
            if (ViewModel.Messages.Count > 0)
            {
                MessageList.ScrollIntoView(ViewModel.Messages[^1]);
            }
        }
    }

    private void OnFriendsClicked(object sender, RoutedEventArgs e)
    {
        GuildList.SelectedItem = null;
        ViewModel.ShowFriendsView();
    }

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void OnAttachFileClicked(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.SendFileAsync(file.Path);
        }
    }

    private async void OnPasteClipboardAttachmentClicked(object sender, RoutedEventArgs e)
    {
        await TrySendClipboardImageOrFileAsync();
    }

    private async System.Threading.Tasks.Task<bool> TrySendClipboardImageOrFileAsync()
    {
        try
        {
            var data = Clipboard.GetContent();
            if (data.Contains(StandardDataFormats.StorageItems))
            {
                var items = await data.GetStorageItemsAsync();
                var firstFile = items.OfType<Windows.Storage.StorageFile>().FirstOrDefault();
                if (firstFile is not null)
                {
                    await ViewModel.SendFileAsync(firstFile.Path);
                    return true;
                }
            }
            if (data.Contains(StandardDataFormats.Bitmap))
            {
                var streamRef = await data.GetBitmapAsync();
                using var stream = await streamRef.OpenReadAsync();
                var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);
                await stream.ReadAsync(buffer, (uint)stream.Size, Windows.Storage.Streams.InputStreamOptions.None);
                var bytes = buffer.ToArray();
                await ViewModel.SendBytesAsync(bytes, "clipboard.png", MessageInput.Text);
                MessageInput.Text = string.Empty;
                return true;
            }
        }
        catch { }
        return false;
    }

    private readonly ScreenCaptureService _screenCapture = new();

    private void OnMessageInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            ViewModel.NotifyTypingInCurrentChannel();
        }
    }

    private async void OnMessageInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && ViewModel.HasComposerBanner)
        {
            e.Handled = true;
            ViewModel.CancelComposerAction();
            MessageInput.Text = string.Empty;
            return;
        }
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SendCurrentMessageAsync();
        }
    }

    private async System.Threading.Tasks.Task SendCurrentMessageAsync()
    {
        var content = MessageInput.Text;
        if (string.IsNullOrWhiteSpace(content)) return;
        MessageInput.Text = string.Empty;
        await ViewModel.SendMessageAsync(content);
    }

    private void OnCancelComposerActionClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelComposerAction();
        MessageInput.Text = string.Empty;
    }

    private void OnQuickEmojiInsertClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string emoji })
        {
            MessageInput.Text += emoji;
            MessageInput.SelectionStart = MessageInput.Text.Length;
            MessageInput.Focus(FocusState.Programmatic);
        }
    }

    private async void OnCreatePollClicked(object sender, RoutedEventArgs e)
    {
        var stack = new StackPanel { Spacing = 10, Width = 420 };
        var questionBox = new TextBox
        {
            Header = "Question du sondage",
            PlaceholderText = "Posez une question...",
            MaxLength = 300,
            CornerRadius = new CornerRadius(8)
        };
        stack.Children.Add(questionBox);

        var answersLabel = new TextBlock
        {
            Text = "Réponses (2 minimum, jusqu'à 5)",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.8
        };
        stack.Children.Add(answersLabel);

        var answerBoxes = new List<TextBox>();
        for (int i = 1; i <= 4; i++)
        {
            var ab = new TextBox
            {
                PlaceholderText = $"Option {i}" + (i <= 2 ? " (requis)" : " (optionnel)"),
                MaxLength = 55,
                CornerRadius = new CornerRadius(8)
            };
            answerBoxes.Add(ab);
            stack.Children.Add(ab);
        }

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        var durationCombo = new ComboBox
        {
            Header = "Durée",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8)
        };
        durationCombo.Items.Add(new ComboBoxItem { Content = "1 heure", Tag = 1 });
        durationCombo.Items.Add(new ComboBoxItem { Content = "8 heures", Tag = 8 });
        durationCombo.Items.Add(new ComboBoxItem { Content = "24 heures", Tag = 24 });
        durationCombo.Items.Add(new ComboBoxItem { Content = "3 jours", Tag = 72 });
        durationCombo.Items.Add(new ComboBoxItem { Content = "1 semaine", Tag = 168 });
        durationCombo.SelectedIndex = 2;
        Grid.SetColumn(durationCombo, 0);

        var multiToggle = new ToggleSwitch
        {
            Header = "Choix multiples",
            IsOn = false,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetColumn(multiToggle, 1);

        row.Children.Add(durationCombo);
        row.Children.Add(multiToggle);
        stack.Children.Add(row);

        var dialog = new ContentDialog
        {
            Title = "Créer un sondage",
            Content = stack,
            PrimaryButtonText = "Publier le sondage",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var q = questionBox.Text.Trim();
            var validAnswers = answerBoxes.Select(b => b.Text.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (string.IsNullOrWhiteSpace(q) || validAnswers.Count < 2) return;

            int hours = (durationCombo.SelectedItem as ComboBoxItem)?.Tag is int h ? h : 24;
            try
            {
                await ViewModel.CreatePollInCurrentChannelAsync(q, validAnswers, hours, multiToggle.IsOn);
            }
            catch { }
        }
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Activate();
    }

    private async void OnStartCallClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartOrJoinVoiceCallInCurrentChannelAsync();
    }

    private void OnDisconnectVoiceClicked(object sender, RoutedEventArgs e)
    {
        _screenCapture.StopCapture();
        if (LiveScreenShareImage != null) LiveScreenShareImage.Source = null;
        ViewModel.DisconnectVoice();
    }

    private void OnToggleMuteClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleMute();
    }

    private void OnToggleDeafenClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDeafen();
    }

    private void OnToggleKrispClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleKrisp();
    }

    private async void OnToggleCameraClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleCameraAsync();
    }

    private async void OnScreenShareClicked(object sender, RoutedEventArgs e)
    {
        await ShowScreenSharePickerDialogAsync();
    }

    private async void OnChangeScreenShareSourceClicked(object sender, RoutedEventArgs e)
    {
        await ShowScreenSharePickerDialogAsync();
    }

    private async void OnStopScreenShareClicked(object sender, RoutedEventArgs e)
    {
        _screenCapture.StopCapture();
        if (LiveScreenShareImage != null) LiveScreenShareImage.Source = null;
        await ViewModel.StopScreenShareAsync();
    }

    private async System.Threading.Tasks.Task ShowScreenSharePickerDialogAsync()
    {
        var sources = ScreenCaptureService.GetAvailableSources();
        ScreenShareSourceItem? selectedSource = sources.FirstOrDefault();

        var rootStack = new StackPanel { Spacing = 14, Width = 540 };
        rootStack.Children.Add(new TextBlock
        {
            Text = "Sélectionnez l'écran ou la fenêtre d'application que vous souhaitez diffuser en direct :",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        });

        // Onglets Écrans / Applications
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var screensBtn = new Button { Content = "Écrans", CornerRadius = new CornerRadius(8), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var windowsBtn = new Button { Content = "Applications / Fenêtres", CornerRadius = new CornerRadius(8) };
        tabRow.Children.Add(screensBtn);
        tabRow.Children.Add(windowsBtn);
        rootStack.Children.Add(tabRow);

        var gridScroll = new ScrollViewer
        {
            MaxHeight = 270,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var itemsPanel = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal,
            MaximumRowsOrColumns = 2,
            ItemWidth = 255,
            ItemHeight = 148
        };
        gridScroll.Content = itemsPanel;
        rootStack.Children.Add(gridScroll);

        var cardBorders = new List<(Border border, ScreenShareSourceItem src)>();

        void PopulateCards(bool showWindows)
        {
            itemsPanel.Children.Clear();
            cardBorders.Clear();
            var filtered = sources.Where(s => s.IsWindow == showWindows).ToList();
            if (filtered.Count == 0) filtered = sources;

            foreach (var src in filtered)
            {
                var thumb = src.Thumbnail ?? ScreenCaptureService.CaptureThumbnail(src, 240, 135);
                var img = new Image { Source = thumb, Stretch = Stretch.UniformToFill, Height = 100 };
                var imgBorder = new Border { CornerRadius = new CornerRadius(8, 8, 0, 0), Child = img, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 18, 20)) };

                var titleBlock = new TextBlock
                {
                    Text = src.Title,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(8, 6, 8, 6)
                };

                var cardStack = new StackPanel();
                cardStack.Children.Add(imgBorder);
                cardStack.Children.Add(titleBlock);

                bool isSel = selectedSource?.Id == src.Id;
                var cardBorder = new Border
                {
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(2),
                    BorderBrush = new SolidColorBrush(isSel
                        ? Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0x5A)
                        : Windows.UI.Color.FromArgb(80, 255, 255, 255)),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 31, 34)),
                    Child = cardStack
                };

                var capturedSrc = src;
                cardBorder.PointerPressed += (_, _) =>
                {
                    selectedSource = capturedSrc;
                    foreach (var (b, s) in cardBorders)
                    {
                        b.BorderBrush = new SolidColorBrush(s.Id == selectedSource.Id
                            ? Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0x5A)
                            : Windows.UI.Color.FromArgb(80, 255, 255, 255));
                    }
                };

                cardBorders.Add((cardBorder, src));
                itemsPanel.Children.Add(cardBorder);
            }
        }

        screensBtn.Click += (_, _) =>
        {
            screensBtn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            windowsBtn.Style = null;
            selectedSource = sources.FirstOrDefault(s => !s.IsWindow) ?? sources.FirstOrDefault();
            PopulateCards(showWindows: false);
        };
        windowsBtn.Click += (_, _) =>
        {
            windowsBtn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            screensBtn.Style = null;
            selectedSource = sources.FirstOrDefault(s => s.IsWindow) ?? sources.FirstOrDefault();
            PopulateCards(showWindows: true);
        };

        PopulateCards(showWindows: false);

        // Paramètres de Qualité (Résolution, FPS, Audio système) — 1080p 60 FPS débloqué gratuitement !
        var qualityGrid = new Grid { ColumnSpacing = 12 };
        qualityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        qualityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var resCombo = new ComboBox
        {
            Header = "Résolution (100% Gratuit)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8)
        };
        resCombo.Items.Add(new ComboBoxItem { Content = "720p (HD)", Tag = (1280, 720, "720p") });
        resCombo.Items.Add(new ComboBoxItem { Content = "1080p (Full HD — Sans Nitro)", Tag = (1920, 1080, "1080p") });
        resCombo.SelectedIndex = 1;
        Grid.SetColumn(resCombo, 0);

        var fpsCombo = new ComboBox
        {
            Header = "Fréquence d'images",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8)
        };
        fpsCombo.Items.Add(new ComboBoxItem { Content = "15 FPS", Tag = 15 });
        fpsCombo.Items.Add(new ComboBoxItem { Content = "30 FPS", Tag = 30 });
        fpsCombo.Items.Add(new ComboBoxItem { Content = "60 FPS (Fluide — Sans Nitro)", Tag = 60 });
        fpsCombo.SelectedIndex = 2;
        Grid.SetColumn(fpsCombo, 1);

        qualityGrid.Children.Add(resCombo);
        qualityGrid.Children.Add(fpsCombo);
        rootStack.Children.Add(qualityGrid);

        var audioToggle = new ToggleSwitch
        {
            Header = "Partager l'audio du système / de l'application (Loopback WASAPI)",
            IsOn = true
        };
        rootStack.Children.Add(audioToggle);

        var dialog = new ContentDialog
        {
            Title = "Partager votre écran (Go Live)",
            Content = rootStack,
            PrimaryButtonText = "Passer en direct",
            SecondaryButtonText = ViewModel.IsScreenSharing ? "Arrêter le partage" : string.Empty,
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && selectedSource is not null)
        {
            var (w, h, resLabel) = (resCombo.SelectedItem as ComboBoxItem)?.Tag is ValueTuple<int, int, string> rt ? rt : (1920, 1080, "1080p");
            int fps = (fpsCombo.SelectedItem as ComboBoxItem)?.Tag is int f ? f : 60;
            string badge = $"{resLabel} • {fps} FPS";

            _screenCapture.StartCapture(selectedSource, w, h, fps);
            if (LiveScreenShareImage != null)
            {
                LiveScreenShareImage.Source = _screenCapture.LiveBitmap;
            }
            await ViewModel.StartScreenShareAsync(selectedSource.Title, badge, audioToggle.IsOn);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            _screenCapture.StopCapture();
            if (LiveScreenShareImage != null) LiveScreenShareImage.Source = null;
            await ViewModel.StopScreenShareAsync();
        }
    }

    private void OnChannelSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            ViewModel.ApplySearchFilter(tb.Text);
        }
    }

    private void OnStatusOnlineClicked(object sender, RoutedEventArgs e) => ViewModel.SetStatus("online");
    private void OnStatusIdleClicked(object sender, RoutedEventArgs e) => ViewModel.SetStatus("idle");
    private void OnStatusDndClicked(object sender, RoutedEventArgs e) => ViewModel.SetStatus("dnd");
    private void OnStatusInvisibleClicked(object sender, RoutedEventArgs e) => ViewModel.SetStatus("invisible");
    private void OnCopyCurrentUserIdClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.CurrentUser?.Id))
        {
            CopyToClipboard(ViewModel.CurrentUser.Id);
        }
    }

    // --- Actions En-tête de Salon : Invitations, Messages Épinglés, Mentions, Membres ---
    private void OnOpenPinsFlyoutClicked(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.LoadPinnedMessagesAsync();
    }

    private async void OnUnpinFromFlyoutClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null)
        {
            await ViewModel.TogglePinMessageAsync(msg);
            await ViewModel.LoadPinnedMessagesAsync();
        }
    }

    private void OnOpenMentionsFlyoutClicked(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.LoadMentionsAsync();
    }

    private void OnToggleMemberListClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleMemberList();
    }

    private async void OnMemberItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiscordUser user })
        {
            await ViewModel.OpenOrSendDirectMessageAsync(user);
        }
    }

    private async void OnQuickProfileMessageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is TextBox { Tag: DiscordUser user } tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            e.Handled = true;
            var msg = tb.Text.Trim();
            tb.Text = string.Empty;
            await ViewModel.OpenOrSendDirectMessageAsync(user, msg);
        }
    }

    private async void OnCreateChannelInviteClicked(object sender, RoutedEventArgs e)
    {
        await ShowCreatedInviteDialogAsync();
    }

    private async void OnCreateChannelInviteFromMenuClicked(object sender, RoutedEventArgs e)
    {
        var ch = GetChannelFromSender(sender);
        if (ch != null)
        {
            await ViewModel.SelectChannelAsync(ch);
            await ShowCreatedInviteDialogAsync();
        }
    }

    private async System.Threading.Tasks.Task ShowCreatedInviteDialogAsync()
    {
        try
        {
            var url = await ViewModel.CreateInviteForCurrentChannelAsync();
            if (string.IsNullOrEmpty(url)) return;
            CopyToClipboard(url);
            var dialog = new ContentDialog
            {
                Title = "Lien d'invitation créé",
                Content = $"Le lien a été copié dans votre presse-papiers :\n{url}",
                CloseButtonText = "Fermer",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var err = new ContentDialog
            {
                Title = "Impossible de créer l'invitation",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await err.ShowAsync();
        }
    }

    // --- Rejoindre / Quitter un serveur ---
    private async void OnJoinServerClicked(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            PlaceholderText = "https://discord.gg/exemple ou code d'invitation",
            CornerRadius = new CornerRadius(8)
        };
        var dialog = new ContentDialog
        {
            Title = "Rejoindre un serveur",
            Content = input,
            PrimaryButtonText = "Rejoindre",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            try
            {
                await ViewModel.JoinServerByInviteAsync(input.Text);
            }
            catch (Exception ex)
            {
                var err = new ContentDialog
                {
                    Title = "Invitation invalide",
                    Content = $"Impossible de rejoindre le serveur : {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await err.ShowAsync();
            }
        }
    }

    private async void OnLeaveGuildClicked(object sender, RoutedEventArgs e)
    {
        var g = GetGuildFromSender(sender);
        if (g is null) return;

        var dialog = new ContentDialog
        {
            Title = $"Quitter '{g.Name}' ?",
            Content = "Êtes-vous sûr de vouloir quitter ce serveur ?",
            PrimaryButtonText = "Quitter le serveur",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.LeaveGuildAsync(g);
            }
            catch { }
        }
    }

    // --- Animation au survol des avatars et icônes ---
    private void OnAvatarPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is DiscordGuild g)
            {
                g.IsHovered = true;
            }
            ControlBitmapInTree(fe, play: true);
        }
    }

    private void OnAvatarPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is DiscordGuild g)
            {
                g.IsHovered = false;
            }
            ControlBitmapInTree(fe, play: false);
        }
    }

    private static void ControlBitmapInTree(FrameworkElement fe, bool play)
    {
        if (fe is Image img && img.Source is BitmapImage bi)
        {
            if (play) bi.Play(); else bi.Stop();
            return;
        }
        if (fe is Microsoft.UI.Xaml.Shapes.Shape shape && shape.Fill is ImageBrush ib && ib.ImageSource is BitmapImage sbi)
        {
            if (play) sbi.Play(); else sbi.Stop();
            return;
        }
        if (fe is Border b && b.Background is ImageBrush bib && bib.ImageSource is BitmapImage bbi)
        {
            if (play) bbi.Play(); else bbi.Stop();
            return;
        }
        if (fe is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (child is FrameworkElement cfe) ControlBitmapInTree(cfe, play);
            }
        }
        else if (fe is ContentControl cc && cc.Content is FrameworkElement cfe)
        {
            ControlBitmapInTree(cfe, play);
        }
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }
        catch { }
    }

    // --- Menus contextuels Clic Droit : Messages ---
    private static void CloseParentFlyout(object sender)
    {
        if (sender is DependencyObject dobj)
        {
            DependencyObject? curr = dobj;
            while (curr != null)
            {
                if (curr is FlyoutPresenter fp)
                {
                    if (VisualTreeHelper.GetParent(fp) is Popup popup)
                    {
                        popup.IsOpen = false;
                        return;
                    }
                    if (fp.Parent is Popup p2)
                    {
                        p2.IsOpen = false;
                        return;
                    }
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
        }
    }

    private static DiscordMessage? GetMessageFromSender(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is DiscordMessage msg) return msg;
            if (fe.Tag is DiscordMessage tagMsg) return tagMsg;
            DependencyObject? curr = fe;
            while (curr != null)
            {
                if (curr is FrameworkElement parentFe)
                {
                    if (parentFe.DataContext is DiscordMessage parentMsg) return parentMsg;
                    if (parentFe.Tag is DiscordMessage parentTagMsg) return parentTagMsg;
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
        }
        return null;
    }

    private void OnReplyMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null)
        {
            ViewModel.ReplyingToMessage = msg;
            MessageInput.Focus(FocusState.Programmatic);
        }
    }

    private void OnEditMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null)
        {
            ViewModel.EditingMessage = msg;
            MessageInput.Text = msg.Content;
            MessageInput.SelectionStart = MessageInput.Text.Length;
            MessageInput.Focus(FocusState.Programmatic);
        }
    }

    private void OnForwardMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null && !string.IsNullOrWhiteSpace(msg.Content))
        {
            MessageInput.Text = msg.Content;
            MessageInput.SelectionStart = MessageInput.Text.Length;
            MessageInput.Focus(FocusState.Programmatic);
        }
    }

    private void OnTogglePinMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.TogglePinMessageAsync(msg);
    }

    private async void OnTranslateMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg is null || string.IsNullOrWhiteSpace(msg.Content)) return;

        try
        {
            using var http = new System.Net.Http.HttpClient();
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=fr&dt=t&q={Uri.EscapeDataString(msg.Content)}";
            var json = await http.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var sb = new System.Text.StringBuilder();
            foreach (var segment in doc.RootElement[0].EnumerateArray())
            {
                if (segment.GetArrayLength() > 0 && segment[0].ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    sb.Append(segment[0].GetString());
                }
            }
            var translated = sb.ToString();
            var dialog = new ContentDialog
            {
                Title = $"Traduction ({msg.Author.DisplayName})",
                Content = string.IsNullOrWhiteSpace(translated) ? msg.Content : translated,
                PrimaryButtonText = "Copier",
                CloseButtonText = "Fermer",
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                CopyToClipboard(translated);
            }
        }
        catch { }
    }

    private void OnCopyMessageTextClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null && !string.IsNullOrEmpty(msg.Content))
        {
            CopyToClipboard(msg.Content);
        }
    }

    private void OnBookmarkMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null)
        {
            var guildPart = string.IsNullOrWhiteSpace(msg.GuildId) ? "@me" : msg.GuildId;
            var link = $"https://discord.com/channels/{guildPart}/{msg.ChannelId}/{msg.Id}";
            CopyToClipboard(link);
        }
    }

    private void OnCopyMessageLinkClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null)
        {
            var guildPart = string.IsNullOrWhiteSpace(msg.GuildId) ? "@me" : msg.GuildId;
            var link = $"https://discord.com/channels/{guildPart}/{msg.ChannelId}/{msg.Id}";
            CopyToClipboard(link);
        }
    }

    private void OnMarkUnreadClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
    }

    private static SpeechSynthesizer? _speechSynthesizer;
    private static MediaPlayer? _ttsPlayer;

    private async void OnSpeakMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg == null || string.IsNullOrWhiteSpace(msg.Content)) return;
        try
        {
            _speechSynthesizer ??= new SpeechSynthesizer();
            _ttsPlayer ??= new MediaPlayer();
            var stream = await _speechSynthesizer.SynthesizeTextToStreamAsync(msg.Content);
            _ttsPlayer.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _ttsPlayer.Play();
        }
        catch { }
    }

    private void OnQuickReactClapClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "👏");
    }

    private void OnQuickReactThumbsUpClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "👍");
    }

    private void OnQuickReactHeartClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "❤️");
    }

    private void OnQuickReactJoyClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "😂");
    }

    private void OnQuickReactFireClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "🔥");
    }

    private void OnAddCustomReactionClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg == null) return;
        string emoji = (sender as Button)?.Content?.ToString() ?? (sender as FrameworkElement)?.Tag?.ToString() ?? "👍";
        _ = ViewModel.AddReactionAsync(msg.Id, emoji);
    }

    private void OnCreateReminder20mClicked(object sender, RoutedEventArgs e) => CloseParentFlyout(sender);
    private void OnCreateReminder1hClicked(object sender, RoutedEventArgs e) => CloseParentFlyout(sender);
    private void OnCreateReminder3hClicked(object sender, RoutedEventArgs e) => CloseParentFlyout(sender);
    private void OnCreateReminderTomorrowClicked(object sender, RoutedEventArgs e) => CloseParentFlyout(sender);
    private void OnCreateReminderNextWeekClicked(object sender, RoutedEventArgs e) => CloseParentFlyout(sender);

    private void OnDeleteMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.DeleteMessageAsync(msg.Id);
    }

    private void OnReportMessageClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
    }

    private void OnCopyMessageIdClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        var msg = GetMessageFromSender(sender);
        if (msg != null) CopyToClipboard(msg.Id);
    }

    // --- Vue Complète du Profil (Full User Profile Modal) ---
    public void ShowFullUserProfileModal(DiscordUser? user)
    {
        if (user == null) return;
        FullProfileUser = user;

        if (!user.HasProfileEnriched && ViewModel?.Client?.Rest != null)
        {
            _ = ViewModel.Client.Rest.EnrichUserProfileAsync(user);
        }

        if (ProfileUserNoteTextBox != null)
        {
            ProfileUserNoteTextBox.Text = App.Settings.Current.UserNotes.TryGetValue(user.Id, out var note) ? note : string.Empty;
        }

        SelectProfileTab("Board");
        FullUserProfileOverlay.Visibility = Visibility.Visible;
    }

    public void HideFullUserProfileModal()
    {
        FullUserProfileOverlay.Visibility = Visibility.Collapsed;
        FullProfileUser = null;
    }

    private void OnCloseFullUserProfileClicked(object sender, RoutedEventArgs e) => HideFullUserProfileModal();
    private void OnFullUserProfileBackdropPointerPressed(object sender, PointerRoutedEventArgs e) => HideFullUserProfileModal();
    private void OnFullUserProfileModalContainerPointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;

    public void ShowAvatarZoom(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return;
        try
        {
            AvatarZoomImage.Source = new BitmapImage(new Uri(avatarUrl));
            AvatarZoomOverlay.Visibility = Visibility.Visible;
        }
        catch { }
    }

    public void HideAvatarZoom()
    {
        AvatarZoomOverlay.Visibility = Visibility.Collapsed;
        AvatarZoomImage.Source = null;
    }

    private void OnZoomAvatarClicked(object sender, RoutedEventArgs e)
    {
        if (FullProfileUser != null && !string.IsNullOrWhiteSpace(FullProfileUser.AvatarUrl))
        {
            ShowAvatarZoom(FullProfileUser.AvatarUrl);
        }
    }

    private void OnCloseAvatarZoomClicked(object sender, RoutedEventArgs e) => HideAvatarZoom();
    private void OnAvatarZoomBackdropPointerPressed(object sender, PointerRoutedEventArgs e) => HideAvatarZoom();
    private void OnAvatarZoomImagePointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;

    public void HideProfileEditor()
    {
        ProfileEditorOverlay.Visibility = Visibility.Collapsed;
        ProfileEditorOverlay.Children.Clear();
    }

    private void OnProfileEditorBackdropPointerPressed(object sender, PointerRoutedEventArgs e) => HideProfileEditor();

    private void OnOpenFullUserProfileFromPopoutClicked(object sender, RoutedEventArgs e)
    {
        CloseParentFlyout(sender);
        DiscordUser? user = null;
        if (sender is FrameworkElement fe)
        {
            user = fe.Tag as DiscordUser ?? fe.DataContext as DiscordUser;
        }
        if (user != null)
        {
            ShowFullUserProfileModal(user);
        }
    }

    private void OnAvatarDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        CloseParentFlyout(sender);
        if (sender is FrameworkElement fe)
        {
            var user = (fe.DataContext as DiscordMessage)?.Author ?? fe.Tag as DiscordUser ?? fe.DataContext as DiscordUser;
            if (user != null)
            {
                ShowFullUserProfileModal(user);
            }
        }
    }

    private void OnMemberItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.Tag is DiscordUser user)
        {
            ShowFullUserProfileModal(user);
        }
    }

    private void OnCurrentUserAvatarDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (ViewModel?.CurrentUser != null)
        {
            ShowFullUserProfileModal(ViewModel.CurrentUser);
        }
    }

    private void OnProfileTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tabName)
        {
            SelectProfileTab(tabName);
        }
    }

    private void SelectProfileTab(string tabName)
    {
        if (ProfileBoardContent == null) return;

        ProfileBoardContent.Visibility = tabName == "Board" ? Visibility.Visible : Visibility.Collapsed;
        ProfileActivityContent.Visibility = tabName == "Activity" ? Visibility.Visible : Visibility.Collapsed;
        ProfileWishlistContent.Visibility = tabName == "Wishlist" ? Visibility.Visible : Visibility.Collapsed;
        ProfileMutualsContent.Visibility = tabName == "Mutuals" ? Visibility.Visible : Visibility.Collapsed;

        ProfileBoardTabIndicator.Visibility = tabName == "Board" ? Visibility.Visible : Visibility.Collapsed;
        ProfileActivityTabIndicator.Visibility = tabName == "Activity" ? Visibility.Visible : Visibility.Collapsed;
        ProfileWishlistTabIndicator.Visibility = tabName == "Wishlist" ? Visibility.Visible : Visibility.Collapsed;
        ProfileMutualsTabIndicator.Visibility = tabName == "Mutuals" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnAddFriendFromProfileClicked(object sender, RoutedEventArgs e)
    {
        if (FullProfileUser != null && ViewModel != null)
        {
            try
            {
                var targetTag = FullProfileUser.Discriminator is null or "0" ? FullProfileUser.Username : $"{FullProfileUser.Username}#{FullProfileUser.Discriminator}";
                await ViewModel.SendFriendRequestAsync(targetTag);
            }
            catch { }
        }
    }

    private async void OnSendMessageFromProfileClicked(object sender, RoutedEventArgs e)
    {
        if (FullProfileUser != null && ViewModel != null)
        {
            var target = FullProfileUser;
            HideFullUserProfileModal();
            await ViewModel.OpenOrSendDirectMessageAsync(target);
        }
    }

    private void OnProfileMoreActionsClicked(object sender, RoutedEventArgs e)
    {
        if (FullProfileUser != null)
        {
            CopyToClipboard(FullProfileUser.UserTag);
        }
    }

    private void OnProfileUserNoteLostFocus(object sender, RoutedEventArgs e)
    {
        if (FullProfileUser != null && sender is TextBox tb)
        {
            App.Settings.Current.UserNotes[FullProfileUser.Id] = tb.Text;
            App.Settings.Save();
        }
    }

    // --- Menus contextuels Clic Droit : Serveurs ---
    private static DiscordGuild? GetGuildFromSender(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DiscordGuild g) return g;
        return null;
    }

    private void OnMarkGuildReadClicked(object sender, RoutedEventArgs e)
    {
        var g = GetGuildFromSender(sender);
        if (g != null)
        {
            g.MentionCount = 0;
            g.HasUnread = false;
        }
    }

    private void OnCopyGuildNameClicked(object sender, RoutedEventArgs e)
    {
        var g = GetGuildFromSender(sender);
        if (g != null) CopyToClipboard(g.Name);
    }

    private void OnCopyGuildIdClicked(object sender, RoutedEventArgs e)
    {
        var g = GetGuildFromSender(sender);
        if (g != null) CopyToClipboard(g.Id);
    }

    // --- Menus contextuels Clic Droit : Salons ---
    private static DiscordChannel? GetChannelFromSender(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DiscordChannel ch) return ch;
        return null;
    }

    private void OnCopyChannelNameClicked(object sender, RoutedEventArgs e)
    {
        var ch = GetChannelFromSender(sender);
        if (ch != null) CopyToClipboard(ch.DisplayName);
    }

    private void OnCopyChannelIdClicked(object sender, RoutedEventArgs e)
    {
        var ch = GetChannelFromSender(sender);
        if (ch != null) CopyToClipboard(ch.Id);
    }

    private void OnOpenDmChannelClicked(object sender, RoutedEventArgs e)
    {
        var ch = GetChannelFromSender(sender);
        if (ch != null) _ = ViewModel.SelectChannelAsync(ch);
    }

    // --- Filtres & Menus contextuels Clic Droit : Amis ---
    private void OnFilterAllFriendsClicked(object sender, RoutedEventArgs e) => ViewModel.SetFriendsFilter("All");
    private void OnFilterPendingFriendsClicked(object sender, RoutedEventArgs e) => ViewModel.SetFriendsFilter("Pending");
    private void OnFilterBlockedFriendsClicked(object sender, RoutedEventArgs e) => ViewModel.SetFriendsFilter("Blocked");

    private async void OnAddFriendClicked(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            PlaceholderText = "Nom d'utilisateur Discord (ex: pseudo)",
            CornerRadius = new CornerRadius(8)
        };
        var dialog = new ContentDialog
        {
            Title = "Ajouter un ami",
            Content = input,
            PrimaryButtonText = "Envoyer une demande",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            try
            {
                await ViewModel.SendFriendRequestAsync(input.Text.Trim());
            }
            catch (Exception ex)
            {
                var err = new ContentDialog
                {
                    Title = "Impossible d'envoyer la demande",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await err.ShowAsync();
            }
        }
    }

    private static DiscordRelationship? GetFriendFromSender(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DiscordRelationship rel) return rel;
        return null;
    }

    private void OnFriendProfileClicked(object sender, RoutedEventArgs e)
    {
        var rel = GetFriendFromSender(sender);
        if (rel != null)
        {
            _ = ViewModel.OpenOrSendDirectMessageAsync(rel.User);
        }
    }

    private void OnAcceptFriendClicked(object sender, RoutedEventArgs e)
    {
        var rel = GetFriendFromSender(sender);
        if (rel != null) _ = ViewModel.AcceptFriendRequestAsync(rel);
    }

    private void OnBlockFriendClicked(object sender, RoutedEventArgs e)
    {
        var rel = GetFriendFromSender(sender);
        if (rel != null) _ = ViewModel.RemoveOrBlockRelationshipAsync(rel, block: true);
    }

    private void OnRemoveFriendClicked(object sender, RoutedEventArgs e)
    {
        var rel = GetFriendFromSender(sender);
        if (rel != null) _ = ViewModel.RemoveOrBlockRelationshipAsync(rel, block: false);
    }

    private void OnCopyFriendUsernameClicked(object sender, RoutedEventArgs e)
    {
        var rel = GetFriendFromSender(sender);
        if (rel != null) CopyToClipboard(rel.User.DisplayName);
    }

    private void OnCopyFriendIdClicked(object sender, RoutedEventArgs e)
    {
        var rel = GetFriendFromSender(sender);
        if (rel != null) CopyToClipboard(rel.User.Id);
    }

    // --- Éditeur de Profil Plein Écran 100% fidèle à Discord (Main Profile / Server Profiles + Cosmétiques Nitro grisés si non-Nitro, sans aucun popup Nitro) ---
    private void OnEditProfileClicked(object sender, RoutedEventArgs e)
    {
        ShowDiscordFullProfileEditor();
    }

    private void ShowDiscordFullProfileEditor()
    {
        if (ProfileEditorOverlay is null) return;
        ProfileEditorOverlay.Children.Clear();
        ProfileEditorOverlay.Visibility = Visibility.Visible;

        bool hasNitro = ViewModel.CurrentUser?.HasFullNitro == true;
        DiscordGuild? selectedGuildProfile = null; // null = Main Profile

        byte[]? rawAvatarBytes = null;
        string? rawAvatarExt = null;
        string? selectedAvatarDataUri = null;
        bool removeAvatar = false;
        bool cropChanged = false;
        double cropZoom = 1.0;
        double cropPanX = 0.0;
        double cropPanY = 0.0;
        int imgPixelW = 256;
        int imgPixelH = 256;

        string? selectedBannerDataUri = null;
        bool removeBanner = false;

        var modalDialog = new Border
        {
            Width = 1180,
            Height = 720,
            MaxWidth = 1240,
            MaxHeight = 760,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x31, 0x33, 0x38)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16)
        };
        modalDialog.PointerPressed += (s, e) => e.Handled = true;

        var rootGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // =========================================================================
        // COLONNE GAUCHE (240px) : Main Profile v (dropdown serveurs), Nameplate, Avatar & Decoration, Banner Color, Profile Effect & Frame, Display Name Style
        // =========================================================================
        var leftSidebarBorder = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            CornerRadius = new CornerRadius(16, 0, 0, 16)
        };
        Grid.SetColumn(leftSidebarBorder, 0);

        var leftScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16, 18, 16, 24)
        };
        var leftStack = new StackPanel { Spacing = 18 };
        leftScroll.Content = leftStack;
        leftSidebarBorder.Child = leftScroll;

        // 1. Sélecteur "Main Profile v" avec tous les serveurs (grisés si !hasNitro, zéro popup Nitro)
        var profileSelectorHeaderGrid = new Grid();
        profileSelectorHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profileSelectorHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var profileDropdownBtn = new Button
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var profileDropdownLabel = new TextBlock
        {
            Text = "Main Profile",
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 15,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        var profileDropdownBtnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        profileDropdownBtnContent.Children.Add(profileDropdownLabel);
        profileDropdownBtnContent.Children.Add(new FontIcon { Glyph = "\uE70D", FontSize = 11, Opacity = 0.8 });
        profileDropdownBtn.Content = profileDropdownBtnContent;

        var collapseSidebarIcon = new FontIcon
        {
            Glyph = "\uE970",
            FontSize = 13,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(profileDropdownBtn, 0);
        Grid.SetColumn(collapseSidebarIcon, 1);
        profileSelectorHeaderGrid.Children.Add(profileDropdownBtn);
        profileSelectorHeaderGrid.Children.Add(collapseSidebarIcon);
        leftStack.Children.Add(profileSelectorHeaderGrid);

        // helper pour créer une carte cosmétique Nitro (grisée si !hasNitro, sans aucun popup)
        Border WrapNitroCosmetic(FrameworkElement child, string tooltipWhenLocked)
        {
            var container = new Border
            {
                Opacity = hasNitro ? 1.0 : 0.42,
                IsHitTestVisible = hasNitro
            };
            if (!hasNitro)
            {
                ToolTipService.SetToolTip(container, tooltipWhenLocked);
            }
            container.Child = child;
            return container;
        }

        // 2. Section Nameplate (Nitro)
        var nameplateSection = new StackPanel { Spacing = 8 };
        var nameplateHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nameplateHeader.Children.Add(new TextBlock { Text = "Nameplate", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });
        if (!hasNitro)
        {
            nameplateHeader.Children.Add(new FontIcon { Glyph = "\uE72E", FontSize = 11, Opacity = 0.65 });
        }
        nameplateSection.Children.Add(nameplateHeader);

        var nameplateBox = new Border
        {
            Height = 46,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 10, 0)
        };
        var nameplateGrid = new Grid();
        nameplateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameplateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameplateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var npAvatar = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 26,
            Height = 26,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x58, 0x65, 0xF2)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var npBar = new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4E, 0x50, 0x58)),
            Margin = new Thickness(10, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var npPlus = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE710",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(npAvatar, 0);
        Grid.SetColumn(npBar, 1);
        Grid.SetColumn(npPlus, 2);
        nameplateGrid.Children.Add(npAvatar);
        nameplateGrid.Children.Add(npBar);
        nameplateGrid.Children.Add(npPlus);
        nameplateBox.Child = nameplateGrid;
        nameplateSection.Children.Add(WrapNitroCosmetic(nameplateBox, "Nameplate (Disponible avec Nitro)"));
        leftStack.Children.Add(nameplateSection);

        // 3. Section Avatar & Decoration
        var avatarDecoSection = new StackPanel { Spacing = 8 };
        avatarDecoSection.Children.Add(new TextBlock { Text = "Avatar & Decoration", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });

        var avatarDecoGrid = new Grid { ColumnSpacing = 10 };
        avatarDecoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        avatarDecoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Tuile gauche : Avatar cliquable (ouvre le sélecteur + recadrage)
        var leftAvatarTileImg = new Image { Width = 68, Height = 68, Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(ViewModel.CurrentUserAvatarUrl))
        {
            leftAvatarTileImg.Source = new BitmapImage(new Uri(ViewModel.CurrentUserAvatarUrl));
        }
        var leftAvatarClip = new Border
        {
            Width = 68,
            Height = 68,
            CornerRadius = new CornerRadius(34),
            Child = leftAvatarTileImg,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var leftAvatarTileBtn = new Button
        {
            Height = 96,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(0),
            Content = leftAvatarClip
        };
        ToolTipService.SetToolTip(leftAvatarTileBtn, "Cliquez pour changer ou recadrer votre Avatar");
        Grid.SetColumn(leftAvatarTileBtn, 0);

        // Tuile droite : Décoration d'avatar (grisée si non-Nitro)
        var decoCircle = new Grid { Width = 68, Height = 68, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        decoCircle.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 68,
            Height = 68,
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4E, 0x50, 0x58)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0x24, 0x28))
        });
        decoCircle.Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE710",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var rightDecoTile = new Border
        {
            Height = 96,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = decoCircle
        };
        var wrappedDecoTile = WrapNitroCosmetic(rightDecoTile, "Avatar Decoration (Disponible avec Nitro)");
        Grid.SetColumn(wrappedDecoTile, 1);

        avatarDecoGrid.Children.Add(leftAvatarTileBtn);
        avatarDecoGrid.Children.Add(wrappedDecoTile);
        avatarDecoSection.Children.Add(avatarDecoGrid);
        leftStack.Children.Add(avatarDecoSection);

        // 4. Section Banner Color & Image Banner (Nitro pour image/GIF)
        var bannerColorSection = new StackPanel { Spacing = 8 };
        bannerColorSection.Children.Add(new TextBlock { Text = "Banner Color", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });

        string currentHex = ViewModel.CurrentUser?.AccentColor is int ac ? $"#{ac:X6}" : "#000000";
        Windows.UI.Color initialBannerColor = Windows.UI.Color.FromArgb(255, 0x11, 0x12, 0x14);
        if (ViewModel.CurrentUser?.AccentColor is int acVal)
        {
            initialBannerColor = Windows.UI.Color.FromArgb(255, (byte)((acVal >> 16) & 0xFF), (byte)((acVal >> 8) & 0xFF), (byte)(acVal & 0xFF));
        }

        var bannerColorPreviewBorder = new Border
        {
            Height = 42,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(initialBannerColor),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
            BorderThickness = new Thickness(1)
        };
        var accentHexInput = new TextBox
        {
            PlaceholderText = "#000000",
            Text = currentHex,
            MaxLength = 7,
            FontSize = 12,
            CornerRadius = new CornerRadius(8)
        };
        bannerColorSection.Children.Add(bannerColorPreviewBorder);
        bannerColorSection.Children.Add(accentHexInput);

        var changeBannerImgBtn = new Button
        {
            Content = hasNitro ? "Changer l'image de bannière..." : "🔒 Image de bannière (Nitro)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            FontSize = 12,
            IsEnabled = hasNitro,
            Opacity = hasNitro ? 1.0 : 0.45
        };
        bannerColorSection.Children.Add(changeBannerImgBtn);
        leftStack.Children.Add(bannerColorSection);

        // 5. Section Profile Effect & Frame (Nitro — grisé sans popup si !hasNitro)
        var effectFrameSection = new StackPanel { Spacing = 8 };
        var effectHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        effectHeader.Children.Add(new TextBlock { Text = "Profile Effect & Frame", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });
        if (!hasNitro)
        {
            effectHeader.Children.Add(new FontIcon { Glyph = "\uE72E", FontSize = 11, Opacity = 0.65 });
        }
        effectFrameSection.Children.Add(effectHeader);

        var effectFrameGrid = new Grid { ColumnSpacing = 10 };
        effectFrameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        effectFrameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border CreatePlusMockTile()
        {
            var inner = new Border
            {
                Width = 64,
                Height = 76,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0x24, 0x28)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
                BorderThickness = new Thickness(1),
                Child = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new FontIcon
                    {
                        Glyph = "\uE710",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            return new Border
            {
                Height = 96,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = inner
            };
        }

        var effectTile = WrapNitroCosmetic(CreatePlusMockTile(), "Profile Effect (Disponible avec Nitro)");
        var frameTile = WrapNitroCosmetic(CreatePlusMockTile(), "Profile Frame (Disponible avec Nitro)");
        Grid.SetColumn(effectTile, 0);
        Grid.SetColumn(frameTile, 1);
        effectFrameGrid.Children.Add(effectTile);
        effectFrameGrid.Children.Add(frameTile);
        effectFrameSection.Children.Add(effectFrameGrid);
        leftStack.Children.Add(effectFrameSection);

        // 6. Séparateur cadenas discret & Display Name Style (comme en bas à gauche de la capture, SANS bouton/popup Nitro intrusif)
        var lockDividerGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        lockDividerGrid.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!hasNitro)
        {
            lockDividerGrid.Children.Add(new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new FontIcon { Glyph = "\uE72E", FontSize = 12, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
        }
        leftStack.Children.Add(lockDividerGrid);

        if (!hasNitro)
        {
            var nitroCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2A, 0x1F, 0x45)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x48, 0x36, 0x73)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10)
            };
            var nitroStack = new StackPanel { Spacing = 8 };
            nitroStack.Children.Add(new TextBlock
            {
                Text = "Level up your look, only with Nitro",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextWrapping = TextWrapping.Wrap
            });
            var previewNitroBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Padding = new Thickness(8, 5, 8, 5)
            };
            var previewBtnStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            previewBtnStack.Children.Add(new FontIcon { Glyph = "\uE7B3", FontSize = 12 });
            previewBtnStack.Children.Add(new TextBlock { Text = "Preview Nitro", FontSize = 12 });
            previewNitroBtn.Content = previewBtnStack;
            nitroStack.Children.Add(previewNitroBtn);
            nitroCard.Child = nitroStack;
            leftStack.Children.Add(nitroCard);
        }

        var dnsSection = new StackPanel { Spacing = 8 };
        dnsSection.Children.Add(new TextBlock { Text = "Display Name Style", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });
        var dnsPreviewText = new TextBlock
        {
            Text = ViewModel.CurrentUserName,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x9B, 0x84, 0xEE)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var dnsTile = new Border
        {
            Height = 48,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
            BorderThickness = new Thickness(1),
            Child = dnsPreviewText
        };
        dnsSection.Children.Add(WrapNitroCosmetic(dnsTile, "Display Name Style (Disponible avec Nitro)"));
        leftStack.Children.Add(dnsSection);

        rootGrid.Children.Add(leftSidebarBorder);

        // =========================================================================
        // ZONE PRINCIPALE DROITE : Carte Centrale Interactive du Profil + Colonne Droite (Board / Activity / Wishlist) + Bouton Fermer (X)
        // =========================================================================
        var mainRightArea = new Grid { Padding = new Thickness(36, 28, 36, 24) };
        mainRightArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainRightArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(mainRightArea, 1);

        var contentColumns = new Grid { ColumnSpacing = 32, HorizontalAlignment = HorizontalAlignment.Center };
        contentColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        contentColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
        Grid.SetRow(contentColumns, 0);

        // -------------------------------------------------------------------------
        // CARTE CENTRALE DU PROFIL (400px, fidèle à 100% au screenshot 1)
        // -------------------------------------------------------------------------
        var centerCardBorder = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0x24, 0x28)),
            CornerRadius = new CornerRadius(18),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(centerCardBorder, 0);

        var centerCardScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var centerCardRootGrid = new Grid();
        centerCardScroll.Content = centerCardRootGrid;
        centerCardBorder.Child = centerCardScroll;

        // Bannière supérieure (135px) avec bouton / pill cliquable pour changer la bannière
        var cardBannerBorder = new Border
        {
            Height = 135,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(18, 18, 0, 0),
            Background = new SolidColorBrush(initialBannerColor)
        };
        var cardBannerGrid = new Grid();
        var cardBannerImg = new Image { Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(ViewModel.CurrentUserBannerUrl))
        {
            cardBannerImg.Source = new BitmapImage(new Uri(ViewModel.CurrentUserBannerUrl));
        }
        cardBannerGrid.Children.Add(cardBannerImg);

        var bannerEditPill = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB3, 0x11, 0x12, 0x14)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 5, 12, 5),
            Margin = new Thickness(0, 10, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        var bannerEditPillContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        bannerEditPillContent.Children.Add(new FontIcon { Glyph = "\uE70F", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
        bannerEditPillContent.Children.Add(new TextBlock { Text = "Changer la bannière", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
        bannerEditPill.Child = bannerEditPillContent;
        cardBannerGrid.Children.Add(bannerEditPill);

        cardBannerBorder.Child = cardBannerGrid;
        centerCardRootGrid.Children.Add(cardBannerBorder);

        // Avatar rond (112x112) qui chevauche la bannière + recadrage par glisser/zoomer direct !
        const double CardAvatarSize = 112.0;
        var cardAvatarContainer = new Grid
        {
            Width = CardAvatarSize + 12,
            Height = CardAvatarSize + 12,
            Margin = new Thickness(22, 74, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        var cardAvatarOuterRing = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = CardAvatarSize + 12,
            Height = CardAvatarSize + 12,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0x24, 0x28))
        };
        cardAvatarContainer.Children.Add(cardAvatarOuterRing);

        var previewCanvas = new Grid
        {
            Width = CardAvatarSize,
            Height = CardAvatarSize,
            CornerRadius = new CornerRadius(CardAvatarSize / 2.0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY
        };
        var previewTransform = new CompositeTransform();
        var previewImage = new Image
        {
            Width = CardAvatarSize,
            Height = CardAvatarSize,
            Stretch = Stretch.UniformToFill,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = previewTransform
        };
        var avatarPencilOverlay = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x11, 0x12, 0x14)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE70F", // Pencil / Edit
                FontSize = 14,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        previewCanvas.Children.Add(previewImage);
        previewCanvas.Children.Add(avatarPencilOverlay);
        ToolTipService.SetToolTip(previewCanvas, "Cliquez pour changer la photo de profil, ou glissez pour ajuster le cadrage");
        ToolTipService.SetToolTip(avatarPencilOverlay, "Changer la photo de profil");
        cardAvatarContainer.Children.Add(previewCanvas);

        // Pastille verte de statut sur l'avatar
        var cardStatusDot = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 26,
            Height = 26,
            Fill = ViewModel.StatusDotBrush,
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0x24, 0x28)),
            StrokeThickness = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 4)
        };
        cardAvatarContainer.Children.Add(cardStatusDot);
        centerCardRootGrid.Children.Add(cardAvatarContainer);

        // Bulle de pensée Statut Personnalisé ("+ Best emoji to describe your day?") à droite de l'avatar
        var customStatusBubble = new Border
        {
            Margin = new Thickness(150, 118, 18, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 6, 10, 6)
        };
        var customStatusInput = new TextBox
        {
            PlaceholderText = "⊕ Best emoji to describe your day?",
            Text = ViewModel.CurrentUser?.CustomStatusActivity?.State ?? "",
            FontSize = 12,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2)
        };
        customStatusBubble.Child = customStatusInput;
        centerCardRootGrid.Children.Add(customStatusBubble);

        // Contenu sous l'avatar dans la carte centrale
        var cardBodyStack = new StackPanel
        {
            Margin = new Thickness(24, 198, 24, 24),
            Spacing = 12
        };

        // Slider de zoom discret pour le recadrage de la PP
        var zoomSlider = new Slider
        {
            Header = "Zoom du recadrage PP",
            Minimum = 1.0,
            Maximum = 3.0,
            StepFrequency = 0.05,
            Value = 1.0,
            FontSize = 11,
            Visibility = Visibility.Collapsed
        };

        void ClampPan()
        {
            double baseScale = CardAvatarSize / Math.Min(Math.Max(1, imgPixelW), Math.Max(1, imgPixelH));
            double dispW = imgPixelW * baseScale * cropZoom;
            double dispH = imgPixelH * baseScale * cropZoom;
            double maxPanX = Math.Max(0, (dispW - CardAvatarSize) / 2.0);
            double maxPanY = Math.Max(0, (dispH - CardAvatarSize) / 2.0);
            cropPanX = Math.Clamp(cropPanX, -maxPanX, maxPanX);
            cropPanY = Math.Clamp(cropPanY, -maxPanY, maxPanY);
            previewTransform.ScaleX = cropZoom;
            previewTransform.ScaleY = cropZoom;
            previewTransform.TranslateX = cropPanX;
            previewTransform.TranslateY = cropPanY;
        }

        zoomSlider.ValueChanged += (_, args) =>
        {
            cropZoom = args.NewValue;
            cropChanged = true;
            ClampPan();
        };

        previewCanvas.ManipulationDelta += (_, args) =>
        {
            zoomSlider.Visibility = Visibility.Visible;
            cropPanX += args.Delta.Translation.X;
            cropPanY += args.Delta.Translation.Y;
            cropChanged = true;
            ClampPan();
        };

        async Task LoadAvatarBytesIntoCardAsync(byte[] bytes, string ext)
        {
            rawAvatarBytes = bytes;
            rawAvatarExt = ext;
            cropZoom = 1.0;
            cropPanX = 0.0;
            cropPanY = 0.0;
            zoomSlider.Value = 1.0;
            cropChanged = false;

            using var ms = new MemoryStream(bytes);
            var ras = ms.AsRandomAccessStream();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
            imgPixelW = (int)decoder.PixelWidth;
            imgPixelH = (int)decoder.PixelHeight;

            ms.Position = 0;
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            previewImage.Source = bmp;
            leftAvatarTileImg.Source = bmp;
            ClampPan();
        }

        async Task DownloadAndLoadAvatarUrlAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                using var hc = new System.Net.Http.HttpClient();
                var bytes = await hc.GetByteArrayAsync(url);
                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (!removeAvatar)
                    {
                        await LoadAvatarBytesIntoCardAsync(bytes, ".png");
                    }
                });
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(ViewModel.CurrentUser?.Id) && !string.IsNullOrWhiteSpace(ViewModel.CurrentUser?.Avatar))
        {
            var mainUrl = $"https://cdn.discordapp.com/avatars/{ViewModel.CurrentUser.Id}/{ViewModel.CurrentUser.Avatar}.png?size=512";
            _ = Task.Run(() => DownloadAndLoadAvatarUrlAsync(mainUrl));
        }

        async Task TriggerChangeAvatarAsync()
        {
            var picked = await PickImageBytesAsync();
            if (picked != null)
            {
                removeAvatar = false;
                await LoadAvatarBytesIntoCardAsync(picked.Value.bytes, picked.Value.ext);
                cropChanged = true;
                zoomSlider.Visibility = Visibility.Visible;
            }
        }

        async Task TriggerChangeBannerImageAsync()
        {
            if (!hasNitro) return;
            var picked = await PickImageBytesAsync();
            if (picked != null)
            {
                var mime = picked.Value.ext switch
                {
                    ".gif" => "image/gif",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".webp" => "image/webp",
                    _ => "image/png"
                };
                selectedBannerDataUri = $"data:{mime};base64,{Convert.ToBase64String(picked.Value.bytes)}";
                removeBanner = false;
                using var ms = new MemoryStream(picked.Value.bytes);
                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(ms.AsRandomAccessStream());
                cardBannerImg.Source = bmp;
            }
        }

        // Clic direct sur l'avatar pour changer l'image (soit sur la tuile, soit sur l'avatar dans la carte, soit sur l'icône crayon)
        leftAvatarTileBtn.Click += async (_, _) => await TriggerChangeAvatarAsync();
        previewCanvas.Tapped += async (_, _) => await TriggerChangeAvatarAsync();
        avatarPencilOverlay.Tapped += async (s, e) => { e.Handled = true; await TriggerChangeAvatarAsync(); };

        // Clic sur le bouton Changer la bannière dans le panneau gauche
        changeBannerImgBtn.Click += async (_, _) => await TriggerChangeBannerImageAsync();

        // Menu flyout au clic sur la bannière dans la carte de profil
        var bannerFlyout = new MenuFlyout();
        var pickBannerFlyoutItem = new MenuFlyoutItem
        {
            Text = hasNitro ? "Changer l'image de bannière..." : "Image de bannière (Nitro requis)",
            Icon = new FontIcon { Glyph = "\uEB9F" },
            IsEnabled = hasNitro
        };
        pickBannerFlyoutItem.Click += async (_, _) => await TriggerChangeBannerImageAsync();

        var changeColorFlyoutItem = new MenuFlyoutItem
        {
            Text = "Modifier la couleur de bannière...",
            Icon = new FontIcon { Glyph = "\uE790" }
        };
        changeColorFlyoutItem.Click += (_, _) =>
        {
            accentHexInput.Focus(FocusState.Programmatic);
            accentHexInput.SelectAll();
        };

        var removeBannerFlyoutItem = new MenuFlyoutItem
        {
            Text = "Supprimer l'image de bannière",
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        removeBannerFlyoutItem.Click += (_, _) =>
        {
            cardBannerImg.Source = null;
            selectedBannerDataUri = null;
            removeBanner = true;
        };

        bannerFlyout.Items.Add(pickBannerFlyoutItem);
        bannerFlyout.Items.Add(changeColorFlyoutItem);
        bannerFlyout.Items.Add(new MenuFlyoutSeparator());
        bannerFlyout.Items.Add(removeBannerFlyoutItem);

        cardBannerBorder.ContextFlyout = bannerFlyout;
        cardBannerBorder.Tapped += (s, e) => bannerFlyout.ShowAt(cardBannerBorder);
        bannerEditPill.Tapped += (s, e) => { e.Handled = true; bannerFlyout.ShowAt(bannerEditPill); };
        ToolTipService.SetToolTip(cardBannerBorder, "Cliquez pour changer la bannière (image ou couleur)");

        accentHexInput.TextChanged += (_, _) =>
        {
            var hex = accentHexInput.Text.Trim().TrimStart('#');
            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int cVal))
            {
                var col = Windows.UI.Color.FromArgb(255, (byte)((cVal >> 16) & 0xFF), (byte)((cVal >> 8) & 0xFF), (byte)(cVal & 0xFF));
                bannerColorPreviewBorder.Background = new SolidColorBrush(col);
                cardBannerBorder.Background = new SolidColorBrush(col);
            }
        };

        // Nom d'affichage (ex: "Jay" en grand gras)
        var displayNameBox = new TextBox
        {
            Text = ViewModel.CurrentUserName,
            PlaceholderText = "Display Name",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6)
        };
        displayNameBox.TextChanged += (_, _) => dnsPreviewText.Text = displayNameBox.Text;
        cardBodyStack.Children.Add(displayNameBox);
        cardBodyStack.Children.Add(zoomSlider);

        // Ligne : username • [🌸 DSKY v] + Badges officiels
        var handleBadgesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        handleBadgesRow.Children.Add(new TextBlock
        {
            Text = ViewModel.CurrentUser?.Username ?? "user",
            FontSize = 13,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center
        });
        handleBadgesRow.Children.Add(new TextBlock
        {
            Text = "•",
            FontSize = 13,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (ViewModel.CurrentUser?.HasClan == true)
        {
            var clanPill = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            var clanInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (!string.IsNullOrWhiteSpace(ViewModel.CurrentUserClanBadgeUrl))
            {
                clanInner.Children.Add(new Image
                {
                    Width = 14,
                    Height = 14,
                    Source = new BitmapImage(new Uri(ViewModel.CurrentUserClanBadgeUrl)),
                    Stretch = Stretch.Uniform
                });
            }
            clanInner.Children.Add(new TextBlock
            {
                Text = ViewModel.CurrentUserClanTag,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            clanPill.Child = clanInner;
            handleBadgesRow.Children.Add(clanPill);
        }

        foreach (var badge in ViewModel.CurrentUserBadges)
        {
            var bImg = new Image
            {
                Width = 18,
                Height = 18,
                Source = new BitmapImage(new Uri(badge.IconUrl)),
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(bImg, badge.Description);
            handleBadgesRow.Children.Add(bImg);
        }
        cardBodyStack.Children.Add(handleBadgesRow);

        // Champ Pronoms
        var pronounsInput = new TextBox
        {
            Header = "Pronouns",
            PlaceholderText = "Add your pronouns",
            Text = ViewModel.CurrentUserPronouns,
            MaxLength = 40,
            FontSize = 12,
            CornerRadius = new CornerRadius(8)
        };
        cardBodyStack.Children.Add(pronounsInput);

        // Boutons d'aperçu façon Discord : [ 💬 Message ] [ 🏪 ] [ ⋯ ]
        var actionButtonsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var msgPreviewBtn = new Button
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x58, 0x65, 0xF2)),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 7, 16, 7),
            BorderThickness = new Thickness(0)
        };
        var msgBtnStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        msgBtnStack.Children.Add(new FontIcon { Glyph = "\uE8BD", FontSize = 13 });
        msgBtnStack.Children.Add(new TextBlock { Text = "Message", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        msgPreviewBtn.Content = msgBtnStack;
        actionButtonsRow.Children.Add(msgPreviewBtn);
        actionButtonsRow.Children.Add(new Button
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE719", FontSize = 13 }
        });
        actionButtonsRow.Children.Add(new Button
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE712", FontSize = 13 }
        });
        cardBodyStack.Children.Add(actionButtonsRow);

        // Section Bio (avec barre verticale à gauche comme sur le screenshot)
        var bioSection = new StackPanel { Spacing = 4 };
        bioSection.Children.Add(new TextBlock { Text = "Bio", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.7 });
        var bioQuoteBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4E, 0x50, 0x58)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0)
        };
        var bioInput = new TextBox
        {
            Text = ViewModel.CurrentUserBio == "Aucune bio définie." ? "" : ViewModel.CurrentUserBio,
            PlaceholderText = "Write a bio...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = 190,
            MinHeight = 56,
            FontSize = 13,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        bioQuoteBorder.Child = bioInput;
        bioSection.Children.Add(bioQuoteBorder);
        cardBodyStack.Children.Add(bioSection);

        // Section Member Since
        var memberSinceSection = new StackPanel { Spacing = 2 };
        memberSinceSection.Children.Add(new TextBlock { Text = "Member Since", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.7 });
        memberSinceSection.Children.Add(new TextBlock
        {
            Text = ViewModel.CurrentUser?.MemberSinceText ?? "Sep 21, 2021",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium
        });
        cardBodyStack.Children.Add(memberSinceSection);

        // Section Connections (avec jay-space.com ↗ etc.)
        var connectionsSection = new StackPanel { Spacing = 6 };
        connectionsSection.Children.Add(new TextBlock { Text = "Connections", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.7 });
        foreach (var conn in ViewModel.CurrentUserConnectedAccounts)
        {
            var connRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            connRow.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Child = new TextBlock
                {
                    Text = conn.PlatformBadgeText,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            connRow.Children.Add(new TextBlock
            {
                Text = conn.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (conn.HasUrl)
            {
                connRow.Children.Add(new FontIcon { Glyph = "\uE8A7", FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            }
            connectionsSection.Children.Add(connRow);
        }
        connectionsSection.Children.Add(new TextBlock
        {
            Text = "+ Add Connection",
            FontSize = 12,
            Opacity = 0.65,
            Margin = new Thickness(0, 2, 0, 0)
        });
        cardBodyStack.Children.Add(connectionsSection);

        // Section Note (only visible to you)
        var noteSection = new StackPanel { Spacing = 2 };
        noteSection.Children.Add(new TextBlock { Text = "Note (only visible to you)", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.7 });
        noteSection.Children.Add(new TextBox
        {
            PlaceholderText = "Click to add a note",
            FontSize = 12,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 2, 0, 2)
        });
        cardBodyStack.Children.Add(noteSection);

        centerCardRootGrid.Children.Add(cardBodyStack);
        contentColumns.Children.Add(centerCardBorder);

        // -------------------------------------------------------------------------
        // COLONNE DROITE (460px) : Onglets Board | Activity | Wishlist + Cartes Games I Like & Favorite Game
        // -------------------------------------------------------------------------
        var rightWidgetsColumn = new StackPanel { Spacing = 18 };

        // Onglets Board | Activity | Wishlist
        var tabsHeaderRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24,
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 10)
        };
        var boardTab = new StackPanel { Spacing = 4 };
        boardTab.Children.Add(new TextBlock { Text = "Board", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        boardTab.Children.Add(new Border { Height = 2, Background = new SolidColorBrush(Microsoft.UI.Colors.White), CornerRadius = new CornerRadius(1) });
        tabsHeaderRow.Children.Add(boardTab);
        tabsHeaderRow.Children.Add(new TextBlock { Text = "Activity", FontSize = 14, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Top });
        tabsHeaderRow.Children.Add(new TextBlock { Text = "Wishlist", FontSize = 14, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Top });
        rightWidgetsColumn.Children.Add(tabsHeaderRow);

        // En-tête "Your Widgets" + bouton "+ Add Widget"
        var yourWidgetsHeader = new Grid();
        yourWidgetsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        yourWidgetsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        yourWidgetsHeader.Children.Add(new TextBlock
        {
            Text = "Your Widgets",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        });
        var addWidgetBtn = new Button
        {
            Content = "+ Add Widget",
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x3A, 0x40)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetColumn(addWidgetBtn, 1);
        yourWidgetsHeader.Children.Add(addWidgetBtn);
        rightWidgetsColumn.Children.Add(yourWidgetsHeader);

        // Carte 1 : "Games I Like" (Add up to 20 games)
        var gamesILikeCard = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0x24, 0x28)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18)
        };
        var gamesILikeStack = new StackPanel { Spacing = 12 };
        var gamesILikeHeader = new Grid();
        gamesILikeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gamesILikeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var gamesILikeTitleStack = new StackPanel { Spacing = 2 };
        gamesILikeTitleStack.Children.Add(new TextBlock { Text = "Games I Like", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        gamesILikeTitleStack.Children.Add(new TextBlock { Text = "Add up to 20 games", FontSize = 12, Opacity = 0.6 });
        gamesILikeHeader.Children.Add(gamesILikeTitleStack);

        var plusGameBtn = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            Child = new FontIcon { Glyph = "\uE710", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        Grid.SetColumn(plusGameBtn, 1);
        gamesILikeHeader.Children.Add(plusGameBtn);
        gamesILikeStack.Children.Add(gamesILikeHeader);

        var gamesCoversRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var gw in ViewModel.CurrentUserGameWidgets.Take(4))
        {
            var coverBorder = new Border
            {
                Width = 96,
                Height = 128,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
                Child = new Image
                {
                    Source = new BitmapImage(new Uri(gw.IconUrl)),
                    Stretch = Stretch.UniformToFill
                }
            };
            ToolTipService.SetToolTip(coverBorder, gw.TooltipText);
            gamesCoversRow.Children.Add(coverBorder);
        }
        gamesILikeStack.Children.Add(gamesCoversRow);
        gamesILikeCard.Child = gamesILikeStack;
        rightWidgetsColumn.Children.Add(gamesILikeCard);

        // Carte 2 : "Favorite Game" (Choose 1 game)
        var favGame = ViewModel.CurrentUserFavoriteGame ?? ViewModel.CurrentUserGameWidgets.FirstOrDefault();
        if (favGame is not null)
        {
            var favGameCard = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0x24, 0x28)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18)
            };
            var favStack = new StackPanel { Spacing = 12 };
            var favHeaderStack = new StackPanel { Spacing = 2 };
            favHeaderStack.Children.Add(new TextBlock { Text = "Favorite Game", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            favHeaderStack.Children.Add(new TextBlock { Text = "Choose 1 game", FontSize = 12, Opacity = 0.6 });
            favStack.Children.Add(favHeaderStack);

            var favContentGrid = new Grid { ColumnSpacing = 14 };
            favContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            favContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var favCover = new Border
            {
                Width = 92,
                Height = 118,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x1F, 0x22)),
                Child = new Image
                {
                    Source = new BitmapImage(new Uri(favGame.IconUrl)),
                    Stretch = Stretch.UniformToFill
                }
            };
            Grid.SetColumn(favCover, 0);
            favContentGrid.Children.Add(favCover);

            var favDetails = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            favDetails.Children.Add(new TextBlock { Text = favGame.Name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(favGame.Comment))
            {
                favDetails.Children.Add(new TextBlock { Text = favGame.Comment, FontSize = 13, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            }
            favDetails.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = "+ Tags", FontSize = 11, Opacity = 0.75 }
            });
            Grid.SetColumn(favDetails, 1);
            favContentGrid.Children.Add(favDetails);

            favStack.Children.Add(favContentGrid);
            favGameCard.Child = favStack;
            rightWidgetsColumn.Children.Add(favGameCard);
        }

        var rightWidgetsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rightWidgetsColumn
        };
        Grid.SetColumn(rightWidgetsScroll, 1);
        contentColumns.Children.Add(rightWidgetsScroll);
        mainRightArea.Children.Add(contentColumns);

        // -------------------------------------------------------------------------
        // MENU DÉROULANT "Main Profile v" (Image 2 : Main Profile + liste des serveurs, grisés si !hasNitro)
        // -------------------------------------------------------------------------
        var dropdownFlyout = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
        var dropdownScroll = new ScrollViewer { MaxHeight = 320, Width = 220 };
        var dropdownStack = new StackPanel { Spacing = 4 };
        dropdownScroll.Content = dropdownStack;
        dropdownFlyout.Content = dropdownScroll;
        profileDropdownBtn.Flyout = dropdownFlyout;

        async Task SwitchEditingTargetAsync(DiscordGuild? targetGuild)
        {
            selectedGuildProfile = targetGuild;
            dropdownFlyout.Hide();
            if (targetGuild is null)
            {
                profileDropdownLabel.Text = "Main Profile";
                displayNameBox.PlaceholderText = "Display Name";
                displayNameBox.Text = ViewModel.CurrentUserName;
                pronounsInput.Text = ViewModel.CurrentUserPronouns;
                bioInput.Text = ViewModel.CurrentUserBio == "Aucune bio définie." ? "" : ViewModel.CurrentUserBio;
                if (!string.IsNullOrWhiteSpace(ViewModel.CurrentUser?.Id) && !string.IsNullOrWhiteSpace(ViewModel.CurrentUser?.Avatar))
                {
                    var mainUrl = $"https://cdn.discordapp.com/avatars/{ViewModel.CurrentUser.Id}/{ViewModel.CurrentUser.Avatar}.png?size=512";
                    await DownloadAndLoadAvatarUrlAsync(mainUrl);
                }
            }
            else
            {
                profileDropdownLabel.Text = targetGuild.Name;
                displayNameBox.PlaceholderText = $"Pseudo sur {targetGuild.Name}";
                var (nick, gBio, gPronouns, gAvatar, _) = await ViewModel.GetGuildMemberProfileAsync(targetGuild.Id);
                displayNameBox.Text = nick ?? ViewModel.CurrentUserName;
                pronounsInput.Text = gPronouns ?? "";
                bioInput.Text = gBio ?? "";
                if (!string.IsNullOrWhiteSpace(gAvatar) && !string.IsNullOrWhiteSpace(ViewModel.CurrentUser?.Id))
                {
                    var gUrl = $"https://cdn.discordapp.com/guilds/{targetGuild.Id}/users/{ViewModel.CurrentUser.Id}/avatars/{gAvatar}.png?size=512";
                    await DownloadAndLoadAvatarUrlAsync(gUrl);
                }
            }
        }

        // Entrée "Main Profile ✓"
        var mainProfItemBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 7, 8, 7)
        };
        var mainProfRow = new Grid();
        mainProfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainProfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainProfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainProfRow.Children.Add(new FontIcon { Glyph = "\uE77B", FontSize = 14, Margin = new Thickness(0, 0, 10, 0) });
        var mpText = new TextBlock { Text = "Main Profile", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(mpText, 1);
        mainProfRow.Children.Add(mpText);
        var mpCheck = new FontIcon { Glyph = "\uE73E", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(mpCheck, 2);
        mainProfRow.Children.Add(mpCheck);
        mainProfItemBtn.Content = mainProfRow;
        mainProfItemBtn.Click += async (_, _) => await SwitchEditingTargetAsync(null);
        dropdownStack.Children.Add(mainProfItemBtn);

        dropdownStack.Children.Add(new MenuFlyoutSeparator());

        // Liste de tous les serveurs de l'utilisateur (actifs si Nitro, grisés si pas Nitro, aucun popup)
        foreach (var g in ViewModel.Guilds)
        {
            var gBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 7, 8, 7),
                IsEnabled = hasNitro,
                Opacity = hasNitro ? 1.0 : 0.42
            };
            if (!hasNitro)
            {
                ToolTipService.SetToolTip(gBtn, "Profils de serveur personnalisés (Disponible avec Nitro)");
            }

            var gRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var gIconBorder = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31))
            };
            if (g.HasIcon && !string.IsNullOrWhiteSpace(g.IconUrl))
            {
                gIconBorder.Child = new Image { Source = new BitmapImage(new Uri(g.IconUrl)), Stretch = Stretch.UniformToFill };
            }
            gRow.Children.Add(gIconBorder);
            gRow.Children.Add(new TextBlock
            {
                Text = g.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 140,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!hasNitro)
            {
                gRow.Children.Add(new FontIcon { Glyph = "\uE72E", FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            }

            gBtn.Content = gRow;
            var capturedGuild = g;
            gBtn.Click += async (_, _) =>
            {
                if (!hasNitro) return;
                await SwitchEditingTargetAsync(capturedGuild);
            };
            dropdownStack.Children.Add(gBtn);
        }

        // -------------------------------------------------------------------------
        // BARRE D'ENREGISTREMENT EN BAS + BOUTON FERMER (X) EN HAUT À DROITE
        // -------------------------------------------------------------------------
        var closeTopRightBtn = new Button
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new FontIcon { Glyph = "\uE711", FontSize = 14 }
        };
        closeTopRightBtn.Click += (_, _) => HideProfileEditor();
        mainRightArea.Children.Add(closeTopRightBtn);

        var bottomSaveBar = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x11, 0x12, 0x14)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2B, 0x2D, 0x31)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 860
        };
        Grid.SetRow(bottomSaveBar, 1);

        var saveBarGrid = new Grid();
        saveBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        saveBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var saveStatusText = new TextBlock
        {
            Text = "Modifiez directement votre profil ci-dessus puis cliquez sur Enregistrer les modifications.",
            FontSize = 13,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(saveStatusText, 0);
        saveBarGrid.Children.Add(saveStatusText);

        var saveBtnsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var cancelBtn = new Button
        {
            Content = "Réinitialiser / Fermer",
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 7, 14, 7)
        };
        cancelBtn.Click += (_, _) => HideProfileEditor();

        var saveBtn = new Button
        {
            Content = "Enregistrer les modifications",
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0x5A)),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18, 7, 18, 7),
            BorderThickness = new Thickness(0)
        };

        saveBtn.Click += async (_, _) =>
        {
            saveBtn.IsEnabled = false;
            saveStatusText.Text = "Enregistrement en cours sur Discord...";
            try
            {
                if (!removeAvatar && rawAvatarBytes != null && cropChanged)
                {
                    if (rawAvatarExt == ".gif" && Math.Abs(cropZoom - 1.0) < 0.01 && Math.Abs(cropPanX) < 1 && Math.Abs(cropPanY) < 1)
                    {
                        selectedAvatarDataUri = $"data:image/gif;base64,{Convert.ToBase64String(rawAvatarBytes)}";
                    }
                    else
                    {
                        selectedAvatarDataUri = await CropAvatarToPngDataUriAsync(rawAvatarBytes, CardAvatarSize, cropZoom, cropPanX, cropPanY);
                    }
                }

                int? parsedAccent = null;
                var hexText = accentHexInput.Text.Trim().TrimStart('#');
                if (hexText.Length == 6 && int.TryParse(hexText, System.Globalization.NumberStyles.HexNumber, null, out int colorVal))
                {
                    parsedAccent = colorVal;
                }

                if (selectedGuildProfile is null)
                {
                    string? customStatusArg = customStatusInput.Text.Trim();
                    await ViewModel.UpdateProfileAsync(
                        newDisplayName: displayNameBox.Text.Trim(),
                        newBio: bioInput.Text.Trim(),
                        newPronouns: pronounsInput.Text.Trim(),
                        avatarDataUri: selectedAvatarDataUri,
                        removeAvatar: removeAvatar,
                        bannerDataUri: selectedBannerDataUri,
                        removeBanner: removeBanner,
                        accentColor: parsedAccent,
                        customStatusText: customStatusArg);
                }
                else
                {
                    await ViewModel.UpdateGuildMemberProfileAsync(
                        selectedGuildProfile.Id,
                        nick: displayNameBox.Text.Trim(),
                        bio: bioInput.Text.Trim(),
                        pronouns: pronounsInput.Text.Trim(),
                        avatarDataUri: selectedAvatarDataUri,
                        removeAvatar: removeAvatar,
                        bannerDataUri: selectedBannerDataUri,
                        removeBanner: removeBanner,
                        accentColor: parsedAccent);
                }

                HideProfileEditor();
            }
            catch (Exception ex)
            {
                saveBtn.IsEnabled = true;
                saveStatusText.Text = $"Erreur : {ex.Message}";
            }
        };

        saveBtnsRow.Children.Add(cancelBtn);
        saveBtnsRow.Children.Add(saveBtn);
        Grid.SetColumn(saveBtnsRow, 1);
        saveBarGrid.Children.Add(saveBtnsRow);
        bottomSaveBar.Child = saveBarGrid;
        mainRightArea.Children.Add(bottomSaveBar);

        rootGrid.Children.Add(mainRightArea);
        modalDialog.Child = rootGrid;
        ProfileEditorOverlay.Children.Add(modalDialog);
    }

    private static async Task<string> CropAvatarToPngDataUriAsync(byte[] sourceBytes, double viewportSize, double zoom, double panX, double panY)
    {
        using var inMs = new MemoryStream(sourceBytes);
        var inStream = inMs.AsRandomAccessStream();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(inStream);
        uint origW = decoder.PixelWidth;
        uint origH = decoder.PixelHeight;

        double baseScale = viewportSize / Math.Min(Math.Max(1.0, origW), Math.Max(1.0, origH));
        double totalScale = baseScale * zoom;

        double cropSizeInSrc = viewportSize / totalScale;
        double centerXInSrc = (origW / 2.0) - (panX / totalScale);
        double centerYInSrc = (origH / 2.0) - (panY / totalScale);

        double srcX = Math.Clamp(centerXInSrc - (cropSizeInSrc / 2.0), 0, Math.Max(0, origW - cropSizeInSrc));
        double srcY = Math.Clamp(centerYInSrc - (cropSizeInSrc / 2.0), 0, Math.Max(0, origH - cropSizeInSrc));
        uint cropW = (uint)Math.Max(1, Math.Min(origW - srcX, Math.Round(cropSizeInSrc)));
        uint cropH = (uint)Math.Max(1, Math.Min(origH - srcY, Math.Round(cropSizeInSrc)));

        uint scaledW = (uint)Math.Max(1, Math.Round(origW * (512.0 / cropW)));
        uint scaledH = (uint)Math.Max(1, Math.Round(origH * (512.0 / cropH)));
        uint boundX = (uint)Math.Clamp(Math.Round(srcX * (512.0 / cropW)), 0, Math.Max(0, (int)scaledW - 512));
        uint boundY = (uint)Math.Clamp(Math.Round(srcY * (512.0 / cropH)), 0, Math.Max(0, (int)scaledH - 512));
        uint outW = Math.Min(512u, scaledW - boundX);
        uint outH = Math.Min(512u, scaledH - boundY);

        var transform = new Windows.Graphics.Imaging.BitmapTransform
        {
            ScaledWidth = scaledW,
            ScaledHeight = scaledH,
            InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant,
            Bounds = new Windows.Graphics.Imaging.BitmapBounds
            {
                X = boundX,
                Y = boundY,
                Width = outW,
                Height = outH
            }
        };

        var pixelData = await decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            transform,
            Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);

        var pixels = pixelData.DetachPixelData();
        using var outMs = new MemoryStream();
        var outStream = outMs.AsRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, outStream);
        encoder.SetPixelData(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            outW,
            outH,
            96,
            96,
            pixels);
        await encoder.FlushAsync();

        return $"data:image/png;base64,{Convert.ToBase64String(outMs.ToArray())}";
    }

    private static async Task<(byte[] bytes, string ext)?> PickImageBytesAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            if (App.MainAppWindow != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".webp");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return null;

            var bytes = await System.IO.File.ReadAllBytesAsync(file.Path);
            var ext = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
            return (bytes, ext);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> PickImageAsDataUriAsync()
    {
        var picked = await PickImageBytesAsync();
        if (picked == null) return null;
        var mime = picked.Value.ext switch
        {
            ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
        return $"data:{mime};base64,{Convert.ToBase64String(picked.Value.bytes)}";
    }

    private async void OnConfigureRichPresenceClicked(object sender, RoutedEventArgs e)
    {
        var current = ViewModel.CurrentUserRichPresence;
        var stack = new StackPanel { Spacing = 12, Width = 420 };

        stack.Children.Add(new TextBlock
        {
            Text = "Les jeux et applications lancés sur votre PC (VS Code, Steam, etc.) sont détectés automatiquement via le pont local Discord IPC. Vous pouvez aussi définir un Rich Presence personnalisé ci-dessous :",
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        });

        var typeCombo = new ComboBox
        {
            Header = "Type d'activité",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8)
        };
        typeCombo.Items.Add(new ComboBoxItem { Content = "Joue à...", Tag = 0 });
        typeCombo.Items.Add(new ComboBoxItem { Content = "En direct (Stream)...", Tag = 1 });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Écoute...", Tag = 2 });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Regarde...", Tag = 3 });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Participe à...", Tag = 5 });
        typeCombo.SelectedIndex = current?.Type switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            5 => 4,
            _ => 0
        };

        var nameBox = new TextBox
        {
            Header = "Nom du jeu ou de l'application",
            PlaceholderText = "ex: Visual Studio Code, Cyberpunk 2077, Diswin...",
            Text = current?.Name ?? "",
            CornerRadius = new CornerRadius(8)
        };

        var detailsBox = new TextBox
        {
            Header = "Détails (ligne 1)",
            PlaceholderText = "ex: Édite MainWindowContent.xaml",
            Text = current?.Details ?? "",
            CornerRadius = new CornerRadius(8)
        };

        var stateBox = new TextBox
        {
            Header = "État / Partie (ligne 2)",
            PlaceholderText = "ex: Workspace : Diswin",
            Text = current?.State ?? "",
            CornerRadius = new CornerRadius(8)
        };

        var imageBox = new TextBox
        {
            Header = "URL de l'image principale (optionnel)",
            PlaceholderText = "https://...",
            Text = current?.Assets?.LargeImage ?? "",
            CornerRadius = new CornerRadius(8)
        };

        var timerToggle = new ToggleSwitch
        {
            Header = "Afficher le temps écoulé (00:00 écoulées)",
            IsOn = current?.Timestamps?.Start is > 0 || current is null
        };

        stack.Children.Add(typeCombo);
        stack.Children.Add(nameBox);
        stack.Children.Add(detailsBox);
        stack.Children.Add(stateBox);
        stack.Children.Add(imageBox);
        stack.Children.Add(timerToggle);

        var dialog = new ContentDialog
        {
            Title = "Activité & Rich Presence (RPC)",
            Content = stack,
            PrimaryButtonText = "Appliquer le RPC",
            SecondaryButtonText = "Effacer le RPC personnalisé",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                await ViewModel.SetCustomRichPresenceAsync(null);
                return;
            }

            int actType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag is int t ? t : 0;
            var act = new DiscordActivity
            {
                Name = nameBox.Text.Trim(),
                Type = actType,
                Details = string.IsNullOrWhiteSpace(detailsBox.Text) ? null : detailsBox.Text.Trim(),
                State = string.IsNullOrWhiteSpace(stateBox.Text) ? null : stateBox.Text.Trim(),
                Timestamps = timerToggle.IsOn
                    ? new DiscordActivityTimestamps { Start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                    : null,
                Assets = string.IsNullOrWhiteSpace(imageBox.Text)
                    ? null
                    : new DiscordActivityAssets { LargeImage = imageBox.Text.Trim() }
            };
            await ViewModel.SetCustomRichPresenceAsync(act);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await ViewModel.SetCustomRichPresenceAsync(null);
        }
    }

    private void OnUserProfileFlyoutOpening(object sender, object e)
    {
        if (sender is Flyout { Content: FrameworkElement { Tag: DiscordUser user } })
        {
            _ = ViewModel.EnsureUserProfileEnrichedAsync(user);
        }
    }

    private async void OnConnectedAccountClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiscordConnectedAccount acc })
        {
            if (!string.IsNullOrWhiteSpace(acc.ProfileUrl) && Uri.TryCreate(acc.ProfileUrl, UriKind.Absolute, out var uri))
            {
                await Launcher.LaunchUriAsync(uri);
            }
            else if (!string.IsNullOrWhiteSpace(acc.Name))
            {
                CopyToClipboard(acc.Name);
            }
        }
    }

    private ScrollViewer? _messagesScrollViewer;
    private void HookMessageListScroll()
    {
        if (_messagesScrollViewer != null || MessageList == null) return;
        _messagesScrollViewer = FindVisualChild<ScrollViewer>(MessageList);
        if (_messagesScrollViewer != null)
        {
            _messagesScrollViewer.ViewChanged += async (s, e) =>
            {
                if (_messagesScrollViewer.VerticalOffset <= 40)
                {
                    var prevHeight = _messagesScrollViewer.ExtentHeight;
                    var added = await ViewModel.LoadOlderMessagesAsync();
                    if (added > 0)
                    {
                        // Repositionner le scroll pour éviter un saut brutal
                        Protocol.App.RunOnUiThread(() =>
                        {
                            var newHeight = _messagesScrollViewer.ExtentHeight;
                            var delta = newHeight - prevHeight;
                            if (delta > 0)
                            {
                                _messagesScrollViewer.ChangeView(null, _messagesScrollViewer.VerticalOffset + delta, null, disableAnimation: true);
                            }
                        });
                    }
                }
            };
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var sub = FindVisualChild<T>(child);
            if (sub != null) return sub;
        }
        return null;
    }
}
