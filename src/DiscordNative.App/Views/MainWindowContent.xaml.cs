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
using Windows.System;

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
            ControlBitmapInTree(fe, play: true);
        }
    }

    private void OnAvatarPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
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
    private static DiscordMessage? GetMessageFromSender(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DiscordMessage msg) return msg;
        return null;
    }

    private void OnReplyMessageClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null)
        {
            ViewModel.ReplyingToMessage = msg;
            MessageInput.Focus(FocusState.Programmatic);
        }
    }

    private void OnEditMessageClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null)
        {
            ViewModel.EditingMessage = msg;
            MessageInput.Text = msg.Content;
            MessageInput.SelectionStart = MessageInput.Text.Length;
            MessageInput.Focus(FocusState.Programmatic);
        }
    }

    private void OnTogglePinMessageClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.TogglePinMessageAsync(msg);
    }

    private async void OnTranslateMessageClicked(object sender, RoutedEventArgs e)
    {
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
        var msg = GetMessageFromSender(sender);
        if (msg != null && !string.IsNullOrEmpty(msg.Content))
        {
            CopyToClipboard(msg.Content);
        }
    }

    private void OnQuickReactThumbsUpClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "👍");
    }

    private void OnQuickReactHeartClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "❤️");
    }

    private void OnQuickReactJoyClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "😂");
    }

    private void OnQuickReactFireClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.AddReactionAsync(msg.Id, "🔥");
    }

    private void OnDeleteMessageClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null) _ = ViewModel.DeleteMessageAsync(msg.Id);
    }

    private void OnCopyMessageIdClicked(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg != null) CopyToClipboard(msg.Id);
    }

    // --- Menus contextuels Clic Droit : Serveurs ---
    private static DiscordGuild? GetGuildFromSender(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DiscordGuild g) return g;
        return null;
    }

    private void OnMarkGuildReadClicked(object sender, RoutedEventArgs e)
    {
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

    // --- Édition Complète du Profil Utilisateur (avec Recadrage / Zoom interactif de la PP comme sur Discord) ---
    private async void OnEditProfileClicked(object sender, RoutedEventArgs e)
    {
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

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 560,
            Width = 450
        };
        var stack = new StackPanel { Spacing = 14, Padding = new Thickness(0, 0, 12, 0) };

        // 1. Photo de profil (Avatar) avec aperçu interactif Recadrer / Zoomer (drag + slider)
        var mediaHeader = new TextBlock { Text = "Avatar (glissez l'image pour recadrer)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 };

        const double ViewportSize = 140.0;
        var previewCanvas = new Grid
        {
            Width = ViewportSize,
            Height = ViewportSize,
            CornerRadius = new CornerRadius(ViewportSize / 2.0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 31, 34)),
            HorizontalAlignment = HorizontalAlignment.Center,
            ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.TranslateX | Microsoft.UI.Xaml.Input.ManipulationModes.TranslateY
        };

        var previewTransform = new Microsoft.UI.Xaml.Media.CompositeTransform();
        var previewImage = new Image
        {
            Width = ViewportSize,
            Height = ViewportSize,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = previewTransform
        };
        previewCanvas.Children.Add(previewImage);

        // Bordure circulaire guide façon Discord
        var ringGuide = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = ViewportSize,
            Height = ViewportSize,
            Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(180, 88, 101, 242)),
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        previewCanvas.Children.Add(ringGuide);

        var zoomSlider = new Slider
        {
            Header = "Zoom du recadrage",
            Minimum = 1.0,
            Maximum = 3.0,
            StepFrequency = 0.05,
            Value = 1.0,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        void ClampPan()
        {
            double baseScale = ViewportSize / Math.Min(Math.Max(1, imgPixelW), Math.Max(1, imgPixelH));
            double dispW = imgPixelW * baseScale * cropZoom;
            double dispH = imgPixelH * baseScale * cropZoom;
            double maxPanX = Math.Max(0, (dispW - ViewportSize) / 2.0);
            double maxPanY = Math.Max(0, (dispH - ViewportSize) / 2.0);
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
            cropPanX += args.Delta.Translation.X;
            cropPanY += args.Delta.Translation.Y;
            cropChanged = true;
            ClampPan();
        };

        var avatarStatusText = new TextBlock
        {
            Text = "Astuce : déplacez l'image à la souris et utilisez le zoom pour recadrer votre PP actuelle ou une nouvelle image.",
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        async Task LoadAvatarBytesIntoPreviewAsync(byte[] bytes, string ext)
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
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            previewImage.Source = bmp;
            ClampPan();
        }

        // Précharger automatiquement la PP actuelle depuis Discord sans avoir besoin de la ré-uploader !
        if (!string.IsNullOrWhiteSpace(ViewModel.CurrentUser?.Id) && !string.IsNullOrWhiteSpace(ViewModel.CurrentUser?.Avatar))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var url = $"https://cdn.discordapp.com/avatars/{ViewModel.CurrentUser.Id}/{ViewModel.CurrentUser.Avatar}.png?size=512";
                    using var hc = new System.Net.Http.HttpClient();
                    var bytes = await hc.GetByteArrayAsync(url);
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        if (rawAvatarBytes == null && !removeAvatar)
                        {
                            await LoadAvatarBytesIntoPreviewAsync(bytes, ".png");
                        }
                    });
                }
                catch { }
            });
        }

        var pickAvatarBtn = new Button { Content = "Choisir une image...", CornerRadius = new CornerRadius(8) };
        var resetCropBtn = new Button { Content = "Recentrer", CornerRadius = new CornerRadius(8) };
        var removeAvatarBtn = new Button { Content = "Supprimer", CornerRadius = new CornerRadius(8) };

        pickAvatarBtn.Click += async (_, _) =>
        {
            var picked = await PickImageBytesAsync();
            if (picked != null)
            {
                removeAvatar = false;
                await LoadAvatarBytesIntoPreviewAsync(picked.Value.bytes, picked.Value.ext);
                cropChanged = true;
                avatarStatusText.Text = "Nouvelle image chargée — ajustez le zoom et glissez pour recadrer ✓";
            }
        };

        resetCropBtn.Click += (_, _) =>
        {
            cropZoom = 1.0;
            cropPanX = 0.0;
            cropPanY = 0.0;
            zoomSlider.Value = 1.0;
            ClampPan();
        };

        removeAvatarBtn.Click += (_, _) =>
        {
            rawAvatarBytes = null;
            selectedAvatarDataUri = null;
            removeAvatar = true;
            previewImage.Source = null;
            avatarStatusText.Text = "L'avatar sera supprimé";
        };

        var avatarBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        avatarBtnRow.Children.Add(pickAvatarBtn);
        avatarBtnRow.Children.Add(resetCropBtn);
        avatarBtnRow.Children.Add(removeAvatarBtn);

        // Bannière
        var bannerHeader = new TextBlock { Text = "Bannière de profil", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 };
        var bannerStatusText = new TextBlock { Text = "Bannière actuelle conservée", FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var pickBannerBtn = new Button { Content = "Changer la bannière...", CornerRadius = new CornerRadius(8) };
        var removeBannerBtn = new Button { Content = "Supprimer", CornerRadius = new CornerRadius(8) };

        pickBannerBtn.Click += async (_, _) =>
        {
            var dataUri = await PickImageAsDataUriAsync();
            if (dataUri != null)
            {
                selectedBannerDataUri = dataUri;
                removeBanner = false;
                bannerStatusText.Text = "Nouvelle bannière sélectionnée ✓";
            }
        };
        removeBannerBtn.Click += (_, _) =>
        {
            selectedBannerDataUri = null;
            removeBanner = true;
            bannerStatusText.Text = "Bannière sera supprimée";
        };

        var bannerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bannerRow.Children.Add(pickBannerBtn);
        bannerRow.Children.Add(removeBannerBtn);
        bannerRow.Children.Add(bannerStatusText);

        // 2. Identité (Nom d'affichage, Pronoms, Statut personnalisé, Couleur d'accentuation)
        var nameBox = new TextBox
        {
            Header = "Nom d'affichage (Display Name)",
            Text = ViewModel.CurrentUserName,
            CornerRadius = new CornerRadius(8)
        };

        var pronounsBox = new TextBox
        {
            Header = "Pronoms",
            PlaceholderText = "ex: il/lui, they/them...",
            Text = ViewModel.CurrentUserPronouns,
            MaxLength = 40,
            CornerRadius = new CornerRadius(8)
        };

        var customStatusBox = new TextBox
        {
            Header = "Statut personnalisé",
            PlaceholderText = "Que fais-tu en ce moment ?",
            Text = "",
            MaxLength = 128,
            CornerRadius = new CornerRadius(8)
        };

        string currentHex = ViewModel.CurrentUser?.AccentColor is int ac ? $"#{ac:X6}" : "";
        var accentBox = new TextBox
        {
            Header = "Couleur de bannière / d'accentuation (Hex)",
            PlaceholderText = "#5865F2",
            Text = currentHex,
            MaxLength = 7,
            CornerRadius = new CornerRadius(8)
        };

        // 3. Bio / À propos de moi
        var bioBox = new TextBox
        {
            Header = "Bio / À propos de moi (max 190 caractères)",
            Text = ViewModel.CurrentUserBio == "Aucune bio définie." ? "" : ViewModel.CurrentUserBio,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 84,
            MaxLength = 190,
            CornerRadius = new CornerRadius(8)
        };

        // 4. Compte & Sécurité (Nom d'utilisateur @handle et Mot de passe)
        var accountExpander = new Expander
        {
            Header = "Paramètres du compte (@username & mot de passe)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8)
        };
        var accountStack = new StackPanel { Spacing = 10 };
        var usernameBox = new TextBox
        {
            Header = "Nom d'utilisateur (@handle)",
            Text = ViewModel.CurrentUser?.Username ?? "",
            CornerRadius = new CornerRadius(8)
        };
        var currentPassBox = new PasswordBox
        {
            Header = "Mot de passe actuel (requis uniquement pour changer le @handle ou le mot de passe)",
            CornerRadius = new CornerRadius(8)
        };
        var newPassBox = new PasswordBox
        {
            Header = "Nouveau mot de passe (optionnel)",
            CornerRadius = new CornerRadius(8)
        };
        accountStack.Children.Add(usernameBox);
        accountStack.Children.Add(currentPassBox);
        accountStack.Children.Add(newPassBox);
        accountExpander.Content = accountStack;

        stack.Children.Add(mediaHeader);
        stack.Children.Add(previewCanvas);
        stack.Children.Add(zoomSlider);
        stack.Children.Add(avatarBtnRow);
        stack.Children.Add(avatarStatusText);
        stack.Children.Add(bannerHeader);
        stack.Children.Add(bannerRow);
        stack.Children.Add(nameBox);
        stack.Children.Add(pronounsBox);
        stack.Children.Add(customStatusBox);
        stack.Children.Add(accentBox);
        stack.Children.Add(bioBox);
        stack.Children.Add(accountExpander);
        scroll.Content = stack;

        var dialog = new ContentDialog
        {
            Title = "Modifier mon profil",
            Content = scroll,
            PrimaryButtonText = "Enregistrer",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
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
                        selectedAvatarDataUri = await CropAvatarToPngDataUriAsync(rawAvatarBytes, ViewportSize, cropZoom, cropPanX, cropPanY);
                    }
                }

                int? parsedAccent = null;
                var hexText = accentBox.Text.Trim().TrimStart('#');
                if (hexText.Length == 6 && int.TryParse(hexText, System.Globalization.NumberStyles.HexNumber, null, out int colorVal))
                {
                    parsedAccent = colorVal;
                }

                string? newUsername = usernameBox.Text.Trim();
                if (string.Equals(newUsername, ViewModel.CurrentUser?.Username, StringComparison.Ordinal))
                {
                    newUsername = null;
                }

                string? customStatusArg = string.IsNullOrWhiteSpace(customStatusBox.Text) ? null : customStatusBox.Text.Trim();

                await ViewModel.UpdateProfileAsync(
                    newDisplayName: nameBox.Text.Trim(),
                    newBio: bioBox.Text.Trim(),
                    newPronouns: pronounsBox.Text.Trim(),
                    avatarDataUri: selectedAvatarDataUri,
                    removeAvatar: removeAvatar,
                    bannerDataUri: selectedBannerDataUri,
                    removeBanner: removeBanner,
                    accentColor: parsedAccent,
                    customStatusText: customStatusArg,
                    newUsername: newUsername,
                    currentPassword: string.IsNullOrWhiteSpace(currentPassBox.Password) ? null : currentPassBox.Password,
                    newPassword: string.IsNullOrWhiteSpace(newPassBox.Password) ? null : newPassBox.Password);
            }
            catch (Exception ex)
            {
                var err = new ContentDialog
                {
                    Title = "Erreur de modification",
                    Content = $"Impossible de mettre à jour le profil : {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await err.ShowAsync();
            }
        }
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
