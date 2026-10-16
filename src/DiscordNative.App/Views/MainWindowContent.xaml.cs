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

    public MainWindowContent()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInterfaceScale();
        UpdateSplashStatus("Vérification des mises à jour...");
        var update = await UpdateService.CheckForUpdatesAsync();
        if (update is not null)
        {
            _availableUpdate = update;
            UpdateSplashStatus($"Mise à jour {update.TagName} détectée...");
            UpdateInfoBar.Title = $"Mise à jour disponible ({update.TagName})";
            UpdateInfoBar.Message = string.IsNullOrWhiteSpace(update.Name) ? "Une nouvelle version de Diswin est prête à être installée." : update.Name;
            UpdateInfoBar.IsOpen = true;
        }
        else
        {
            UpdateSplashStatus("Connexion au compte...");
        }
    }

    private async void OnApplyUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        ApplyUpdateButton.IsEnabled = false;

        await UpdateService.ApplyAutomaticUpdateAsync(_availableUpdate, status =>
        {
            Protocol.App.RunOnUiThread(() =>
            {
                UpdateInfoBar.Message = status;
            });
        });
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
            await ViewModel.SelectChannelAsync(channel);
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

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Activate();
    }

    private void OnDisconnectVoiceClicked(object sender, RoutedEventArgs e)
    {
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

    // --- Édition du Profil Utilisateur ---
    private async void OnEditProfileClicked(object sender, RoutedEventArgs e)
    {
        var stack = new StackPanel { Spacing = 12 };
        var nameBox = new TextBox
        {
            Header = "Nom d'affichage (Display Name)",
            Text = ViewModel.CurrentUserName,
            CornerRadius = new CornerRadius(8)
        };
        var bioBox = new TextBox
        {
            Header = "Bio / À propos de moi",
            Text = ViewModel.CurrentUserBio == "Aucune bio définie." ? "" : ViewModel.CurrentUserBio,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
            MaxLength = 190,
            CornerRadius = new CornerRadius(8)
        };
        stack.Children.Add(nameBox);
        stack.Children.Add(bioBox);

        var dialog = new ContentDialog
        {
            Title = "Modifier mon profil",
            Content = stack,
            PrimaryButtonText = "Enregistrer",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.UpdateProfileAsync(nameBox.Text.Trim(), bioBox.Text.Trim());
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
}
