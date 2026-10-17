using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using DiscordNative.Protocol;
using DiscordNative.Protocol.Models;

namespace DiscordNative.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DiscordClient _client;
    private DiscordGuild? _selectedGuild;
    private DiscordChannel? _selectedChannel;
    private bool _showingFriends;
    private string _friendsFilter = "All";

    public ObservableCollection<DiscordGuild> Guilds => _client.Guilds;
    public ObservableCollection<DiscordChannel> Channels { get; } = new();
    public ObservableCollection<DiscordRelationship> Friends => _client.Friends;
    public ObservableCollection<DiscordRelationship> FilteredFriends { get; } = new();
    public ObservableCollection<DiscordChannel> PrivateChannels => _client.PrivateChannels;
    public ObservableCollection<DiscordMessage> Messages { get; } = new();
    public ObservableCollection<DiscordMessage> PinnedMessages { get; } = new();
    public ObservableCollection<DiscordMessage> RecentMentions { get; } = new();
    public ObservableCollection<DiscordUser> ChannelMembers { get; } = new();

    [ObservableProperty]
    private string selectedGuildName = "Sélectionnez un serveur";

    [ObservableProperty]
    private string selectedChannelName = "Sélectionnez un salon";

    [ObservableProperty]
    private string selectedChannelTopic = string.Empty;

    [ObservableProperty]
    private string currentUserName = "...";

    [ObservableProperty]
    private bool isVoiceConnected;

    [ObservableProperty]
    private string connectedVoiceChannelName = string.Empty;

    [ObservableProperty]
    private bool isVoiceChannelSelected;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private bool isDeafened;

    [ObservableProperty]
    private bool isKrispEnabled = true;

    [ObservableProperty]
    private bool isScreenSharing;

    [ObservableProperty]
    private bool isCameraEnabled;

    [ObservableProperty]
    private string screenShareTitle = string.Empty;

    [ObservableProperty]
    private string screenShareQualityBadge = "1080p • 60 FPS";

    [ObservableProperty]
    private string currentStatus = "online";

    [ObservableProperty]
    private string searchFilterText = string.Empty;

    [ObservableProperty]
    private bool isMemberListOpen = true;

    private DiscordMessage? _replyingToMessage;
    public DiscordMessage? ReplyingToMessage
    {
        get => _replyingToMessage;
        set
        {
            if (SetProperty(ref _replyingToMessage, value))
            {
                if (value is not null) _editingMessage = null;
                NotifyComposerBannerChanged();
            }
        }
    }

    private DiscordMessage? _editingMessage;
    public DiscordMessage? EditingMessage
    {
        get => _editingMessage;
        set
        {
            if (SetProperty(ref _editingMessage, value))
            {
                if (value is not null) _replyingToMessage = null;
                NotifyComposerBannerChanged();
            }
        }
    }

    public bool HasComposerBanner => ReplyingToMessage is not null || EditingMessage is not null;
    public Microsoft.UI.Xaml.Visibility ComposerBannerVisibility => HasComposerBanner ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string ComposerBannerText => EditingMessage is not null
        ? $"Modification du message : {EditingMessage.Content}"
        : ReplyingToMessage is not null
            ? $"Réponse à @{ReplyingToMessage.Author.DisplayName} : {ReplyingToMessage.ReplyPreview}"
            : string.Empty;

    private void NotifyComposerBannerChanged()
    {
        OnPropertyChanged(nameof(HasComposerBanner));
        OnPropertyChanged(nameof(ComposerBannerVisibility));
        OnPropertyChanged(nameof(ComposerBannerText));
    }

    public void CancelComposerAction()
    {
        ReplyingToMessage = null;
        EditingMessage = null;
    }

    public ObservableCollection<DiscordChannel> FilteredChannels { get; } = new();
    public ObservableCollection<DiscordUser> VoiceParticipants { get; } = new();

    public Microsoft.UI.Xaml.Media.SolidColorBrush StatusDotBrush => new(CurrentStatus switch
    {
        "online" => Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0x5A),
        "idle" => Windows.UI.Color.FromArgb(255, 0xF0, 0xB2, 0x32),
        "dnd" => Windows.UI.Color.FromArgb(255, 0xF2, 0x3F, 0x43),
        _ => Windows.UI.Color.FromArgb(255, 0x80, 0x84, 0x8E)
    });

    public string CurrentStatusDisplayName => CurrentStatus switch
    {
        "online" => "En ligne",
        "idle" => "Inactif",
        "dnd" => "Ne pas déranger",
        "invisible" => "Invisible",
        _ => CurrentStatus
    };

    public string CurrentUserSubStatusText => !string.IsNullOrWhiteSpace(_client.CurrentUser?.ActivitySummaryText)
        ? _client.CurrentUser.ActivitySummaryText
        : CurrentStatusDisplayName;

    public DiscordActivity? CurrentUserRichPresence => _client.CurrentUser?.PrimaryActivity;
    public Microsoft.UI.Xaml.Visibility CurrentUserRichPresenceVisibility => CurrentUserRichPresence is not null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string CurrentUserRpcHeader => CurrentUserRichPresence?.HeaderLabel ?? "EN TRAIN DE JOUER";
    public string CurrentUserRpcName => CurrentUserRichPresence?.Name ?? string.Empty;
    public string CurrentUserRpcDetails => CurrentUserRichPresence?.Details ?? string.Empty;
    public Microsoft.UI.Xaml.Visibility CurrentUserRpcDetailsVisibility => (CurrentUserRichPresence?.HasDetails == true) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string CurrentUserRpcState => CurrentUserRichPresence?.State ?? string.Empty;
    public Microsoft.UI.Xaml.Visibility CurrentUserRpcStateVisibility => (CurrentUserRichPresence?.HasState == true) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string CurrentUserRpcElapsed => CurrentUserRichPresence?.ElapsedText ?? string.Empty;
    public Microsoft.UI.Xaml.Visibility CurrentUserRpcElapsedVisibility => (CurrentUserRichPresence?.HasElapsed == true) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string? CurrentUserRpcLargeImageUrl => CurrentUserRichPresence?.LargeImageUrl;
    public Microsoft.UI.Xaml.Visibility CurrentUserRpcLargeImageVisibility => (CurrentUserRichPresence?.HasLargeImage == true) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string? CurrentUserRpcSmallImageUrl => CurrentUserRichPresence?.SmallImageUrl;
    public Microsoft.UI.Xaml.Visibility CurrentUserRpcSmallImageVisibility => (CurrentUserRichPresence?.HasSmallImage == true) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public double MuteOpacity => IsMuted ? 0.4 : 1.0;
    public double DeafenOpacity => IsDeafened ? 0.4 : 1.0;
    public double KrispOpacity => IsKrispEnabled ? 1.0 : 0.4;
    public Microsoft.UI.Xaml.Media.SolidColorBrush KrispIconBrush => new(IsKrispEnabled
        ? Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0x5A)
        : Windows.UI.Color.FromArgb(255, 0x94, 0x9B, 0xA4));
    public string KrispTooltipText => IsKrispEnabled
        ? "Suppression de bruit Krisp : Activée (100% Gratuit • Cliquez pour désactiver)"
        : "Suppression de bruit Krisp : Désactivée (Cliquez pour activer)";
    public double CurrentUserSpeakingRingOpacity => (_client.CurrentUser?.IsSpeaking == true) ? 1.0 : 0.0;

    public Microsoft.UI.Xaml.Visibility ScreenShareStageVisibility => IsScreenSharing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public double ScreenShareOpacity => IsScreenSharing ? 1.0 : 0.75;
    public Microsoft.UI.Xaml.Media.SolidColorBrush ScreenShareIconBrush => new(IsScreenSharing
        ? Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0x5A)
        : Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF));
    public string ScreenShareTooltipText => IsScreenSharing
        ? $"Partage d'écran en cours ({ScreenShareTitle}) — Cliquez pour modifier ou arrêter"
        : "Partager votre écran (Go Live 720p/1080p 60 FPS + Audio système)";

    public double CameraOpacity => IsCameraEnabled ? 1.0 : 0.75;
    public Microsoft.UI.Xaml.Media.SolidColorBrush CameraIconBrush => new(IsCameraEnabled
        ? Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0x5A)
        : Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF));
    public string CameraTooltipText => IsCameraEnabled
        ? "Caméra activée — Cliquez pour désactiver"
        : "Activer la caméra vidéo";

    public Microsoft.UI.Xaml.Visibility VoiceViewVisibility => IsVoiceChannelSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TextViewVisibility => IsVoiceChannelSelected ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility MemberListVisibility => IsMemberListOpen && !IsVoiceChannelSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TopicVisibility => string.IsNullOrWhiteSpace(SelectedChannelTopic) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public DiscordUser? CurrentUser => _client.CurrentUser;
    public string? CurrentUserAvatarUrl => _client.CurrentUser?.AvatarUrl;
    public string? CurrentUserBannerUrl => _client.CurrentUser?.BannerUrl;
    public bool CurrentUserHasBanner => _client.CurrentUser?.HasBanner ?? false;
    public string CurrentUserTag => _client.CurrentUser?.UserTag ?? "@user";
    public string CurrentUserBio => string.IsNullOrWhiteSpace(_client.CurrentUser?.Bio) ? "Aucune bio définie." : _client.CurrentUser.Bio;
    public string CurrentUserPronouns => _client.CurrentUser?.Pronouns ?? string.Empty;
    public Microsoft.UI.Xaml.Visibility CurrentUserPronounsVisibility => string.IsNullOrWhiteSpace(_client.CurrentUser?.Pronouns) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public string CurrentUserClanTag => _client.CurrentUser?.ClanTag ?? string.Empty;
    public string? CurrentUserClanBadgeUrl => _client.CurrentUser?.ClanBadgeUrl;
    public Microsoft.UI.Xaml.Visibility CurrentUserClanVisibility => (_client.CurrentUser?.HasClan == true) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public ObservableCollection<DiscordBadge> CurrentUserBadges { get; } = new();
    public Microsoft.UI.Xaml.Visibility CurrentUserBadgesVisibility => CurrentUserBadges.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public ObservableCollection<DiscordWidgetGame> CurrentUserGameWidgets { get; } = new();
    public Microsoft.UI.Xaml.Visibility CurrentUserGameWidgetsVisibility => CurrentUserGameWidgets.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public ObservableCollection<DiscordConnectedAccount> CurrentUserConnectedAccounts { get; } = new();
    public Microsoft.UI.Xaml.Visibility CurrentUserConnectedAccountsVisibility => CurrentUserConnectedAccounts.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public DiscordWidgetGame? CurrentUserFavoriteGame => CurrentUserGameWidgets.FirstOrDefault(g => g.IsFavorite);
    public Microsoft.UI.Xaml.Visibility CurrentUserFavoriteGameVisibility => CurrentUserFavoriteGame is not null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility ShowingGuildChannels => _showingFriends ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility ShowingFriendsView => _showingFriends ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public MainViewModel(DiscordClient client)
    {
        _client = client;
        IsKrispEnabled = App.Settings.Current.EnableKrispNoiseSuppression;
        _client.IsKrispEnabled = IsKrispEnabled;
        _client.MessageReceived += OnMessageReceived;
        _client.MessageUpdated += OnMessageUpdated;
        _client.MessageDeleted += OnMessageDeleted;
        _client.ReactionAdded += OnReactionAdded;
        _client.VoiceParticipantsChanged += OnVoiceParticipantsChanged;
        _client.VoiceSpeakingChanged += OnVoiceSpeakingChanged;
        _client.PresenceUpdated += OnPresenceUpdated;
        _client.Friends.CollectionChanged += (_, _) => RefreshFilteredFriends();
    }

    private void OnVoiceSpeakingChanged(string userId, bool isSpeaking)
    {
        if (userId == _client.CurrentUser?.Id)
        {
            OnPropertyChanged(nameof(CurrentUserSpeakingRingOpacity));
        }
        foreach (var vp in VoiceParticipants.Where(u => u.Id == userId))
        {
            vp.IsSpeaking = isSpeaking;
        }
    }

    private void OnPresenceUpdated(string userId)
    {
        if (userId == _client.CurrentUser?.Id)
        {
            NotifyCurrentUserRichPresenceChanged();
        }
        foreach (var member in ChannelMembers.Where(m => m.Id == userId))
        {
            _client.ApplyCachedPresenceToUser(member);
        }
    }

    private void NotifyCurrentUserRichPresenceChanged()
    {
        OnPropertyChanged(nameof(CurrentStatusDisplayName));
        OnPropertyChanged(nameof(CurrentUserSubStatusText));
        OnPropertyChanged(nameof(CurrentUserRichPresence));
        OnPropertyChanged(nameof(CurrentUserRichPresenceVisibility));
        OnPropertyChanged(nameof(CurrentUserRpcHeader));
        OnPropertyChanged(nameof(CurrentUserRpcName));
        OnPropertyChanged(nameof(CurrentUserRpcDetails));
        OnPropertyChanged(nameof(CurrentUserRpcDetailsVisibility));
        OnPropertyChanged(nameof(CurrentUserRpcState));
        OnPropertyChanged(nameof(CurrentUserRpcStateVisibility));
        OnPropertyChanged(nameof(CurrentUserRpcElapsed));
        OnPropertyChanged(nameof(CurrentUserRpcElapsedVisibility));
        OnPropertyChanged(nameof(CurrentUserRpcLargeImageUrl));
        OnPropertyChanged(nameof(CurrentUserRpcLargeImageVisibility));
        OnPropertyChanged(nameof(CurrentUserRpcSmallImageUrl));
        OnPropertyChanged(nameof(CurrentUserRpcSmallImageVisibility));
    }

    public void OnUserLoaded()
    {
        CurrentUserName = _client.CurrentUser?.DisplayName ?? "Utilisateur inconnu";

        CurrentUserBadges.Clear();
        if (_client.CurrentUser?.Badges is not null)
        {
            foreach (var b in _client.CurrentUser.Badges) CurrentUserBadges.Add(b);
        }

        CurrentUserGameWidgets.Clear();
        if (_client.CurrentUser?.GameWidgets is not null)
        {
            foreach (var gw in _client.CurrentUser.GameWidgets) CurrentUserGameWidgets.Add(gw);
        }

        CurrentUserConnectedAccounts.Clear();
        if (_client.CurrentUser?.ConnectedAccounts is not null)
        {
            foreach (var ca in _client.CurrentUser.ConnectedAccounts) CurrentUserConnectedAccounts.Add(ca);
        }

        OnPropertyChanged(nameof(CurrentUser));
        OnPropertyChanged(nameof(CurrentUserAvatarUrl));
        OnPropertyChanged(nameof(CurrentUserBannerUrl));
        OnPropertyChanged(nameof(CurrentUserHasBanner));
        OnPropertyChanged(nameof(CurrentUserTag));
        OnPropertyChanged(nameof(CurrentUserBio));
        OnPropertyChanged(nameof(CurrentUserPronouns));
        OnPropertyChanged(nameof(CurrentUserPronounsVisibility));
        OnPropertyChanged(nameof(CurrentUserClanTag));
        OnPropertyChanged(nameof(CurrentUserClanBadgeUrl));
        OnPropertyChanged(nameof(CurrentUserClanVisibility));
        OnPropertyChanged(nameof(CurrentUserBadgesVisibility));
        OnPropertyChanged(nameof(CurrentUserGameWidgetsVisibility));
        OnPropertyChanged(nameof(CurrentUserConnectedAccountsVisibility));
        OnPropertyChanged(nameof(CurrentUserFavoriteGame));
        OnPropertyChanged(nameof(CurrentUserFavoriteGameVisibility));
        NotifyCurrentUserRichPresenceChanged();
        RefreshFilteredFriends();
    }

    public async Task EnsureUserProfileEnrichedAsync(DiscordUser user)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.Id) || user.HasProfileEnriched) return;
        await _client.Rest.EnrichUserProfileAsync(user);
    }

    public void SetFriendsFilter(string filter)
    {
        _friendsFilter = filter;
        RefreshFilteredFriends();
    }

    public void RefreshFilteredFriends()
    {
        FilteredFriends.Clear();
        IEnumerable<DiscordRelationship> query = _client.Friends;
        query = _friendsFilter switch
        {
            "Pending" => query.Where(r => r.IsPending),
            "Blocked" => query.Where(r => r.IsBlocked),
            _ => query.Where(r => r.IsFriend || r.IsPending),
        };
        foreach (var rel in query)
        {
            FilteredFriends.Add(rel);
        }
    }

    public void ToggleMemberList()
    {
        IsMemberListOpen = !IsMemberListOpen;
        OnPropertyChanged(nameof(MemberListVisibility));
    }

    public async Task UpdateProfileAsync(
        string? newDisplayName = null,
        string? newBio = null,
        string? newPronouns = null,
        string? avatarDataUri = null,
        bool removeAvatar = false,
        string? bannerDataUri = null,
        bool removeBanner = false,
        int? accentColor = null,
        string? customStatusText = null,
        string? newUsername = null,
        string? currentPassword = null,
        string? newPassword = null)
    {
        try
        {
            if (customStatusText is not null)
            {
                await _client.Rest.UpdateCustomStatusAsync(customStatusText);
                if (!string.IsNullOrWhiteSpace(customStatusText))
                {
                    CurrentStatus = customStatusText;
                }
            }

            var updated = await _client.Rest.ModifyCurrentUserAsync(
                globalName: newDisplayName,
                username: newUsername,
                currentPassword: currentPassword,
                newPassword: newPassword,
                bio: newBio,
                pronouns: newPronouns,
                avatarDataUri: avatarDataUri,
                removeAvatar: removeAvatar,
                bannerDataUri: bannerDataUri,
                removeBanner: removeBanner,
                accentColor: accentColor);

            _client.CurrentUser = updated;
            OnUserLoaded();
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                $"[{DateTime.Now}] UpdateProfileAsync failed: {ex}\n\n");
            throw;
        }
    }

    public async Task<(string? nick, string? bio, string? pronouns, string? avatarHash, string? bannerHash)> GetGuildMemberProfileAsync(string guildId)
    {
        if (_client.CurrentUser is null || string.IsNullOrWhiteSpace(guildId)) return (null, null, null, null, null);
        return await _client.Rest.GetGuildMemberProfileAsync(guildId, _client.CurrentUser.Id);
    }

    public async Task UpdateGuildMemberProfileAsync(
        string guildId,
        string? nick = null,
        string? bio = null,
        string? pronouns = null,
        string? avatarDataUri = null,
        bool removeAvatar = false,
        string? bannerDataUri = null,
        bool removeBanner = false,
        int? accentColor = null)
    {
        if (string.IsNullOrWhiteSpace(guildId)) return;
        await _client.Rest.ModifyGuildMemberProfileAsync(
            guildId,
            nick: nick,
            bio: bio,
            pronouns: pronouns,
            avatarDataUri: avatarDataUri,
            removeAvatar: removeAvatar,
            bannerDataUri: bannerDataUri,
            removeBanner: removeBanner,
            accentColor: accentColor);
    }

    public void ShowFriendsView()
    {
        _showingFriends = true;
        SelectedGuildName = "Amis & Messages Privés";
        OnPropertyChanged(nameof(ShowingGuildChannels));
        OnPropertyChanged(nameof(ShowingFriendsView));
        RefreshFilteredFriends();
    }

    public async Task SelectGuildAsync(DiscordGuild guild)
    {
        _showingFriends = false;
        OnPropertyChanged(nameof(ShowingGuildChannels));
        OnPropertyChanged(nameof(ShowingFriendsView));
        _selectedGuild = guild;
        SelectedGuildName = guild.Name;
        Channels.Clear();

        if (guild.Channels.Count > 0)
        {
            foreach (var channel in guild.Channels.Where(c => c.IsTextLike || c.Type == DiscordChannelType.GuildVoice).OrderBy(c => c.Position))
            {
                channel.GuildId ??= guild.Id;
                Channels.Add(channel);
            }
        }
        else
        {
            try
            {
                var fetchedChannels = await _client.Rest.GetGuildChannelsAsync(guild.Id);
                guild.Channels = fetchedChannels;
                foreach (var channel in fetchedChannels.Where(c => c.IsTextLike || c.Type == DiscordChannelType.GuildVoice).OrderBy(c => c.Position))
                {
                    channel.GuildId ??= guild.Id;
                    Channels.Add(channel);
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                    $"[{DateTime.Now}] GetGuildChannelsAsync failed: {ex}\n\n");
            }
        }

        FilteredChannels.Clear();
        foreach (var ch in Channels) FilteredChannels.Add(ch);
    }

    public async Task SelectChannelAsync(DiscordChannel channel)
    {
        _selectedChannel = channel;
        SelectedChannelName = channel.DisplayName;
        SelectedChannelTopic = channel.Topic ?? string.Empty;
        OnPropertyChanged(nameof(TopicVisibility));
        CancelComposerAction();
        Messages.Clear();
        ChannelMembers.Clear();

        if (channel.Type == DiscordChannelType.GuildVoice)
        {
            IsVoiceChannelSelected = true;
            OnPropertyChanged(nameof(VoiceViewVisibility));
            OnPropertyChanged(nameof(TextViewVisibility));
            OnPropertyChanged(nameof(MemberListVisibility));

            if (channel.GuildId != null)
            {
                _ = _client.JoinVoiceChannelAsync(channel.GuildId, channel.Id, IsMuted, IsDeafened, IsCameraEnabled, IsScreenSharing);
                IsVoiceConnected = true;
                ConnectedVoiceChannelName = channel.DisplayName;
            }

            RefreshVoiceParticipants(channel.Id);
        }
        else
        {
            IsVoiceChannelSelected = false;
            OnPropertyChanged(nameof(VoiceViewVisibility));
            OnPropertyChanged(nameof(TextViewVisibility));
            OnPropertyChanged(nameof(MemberListVisibility));

            try
            {
                var history = await _client.Rest.GetMessagesAsync(channel.Id, limit: 50);

                DiscordMessage? prev = null;
                var seenUsers = new HashSet<string>();
                if (_client.CurrentUser is not null && seenUsers.Add(_client.CurrentUser.Id))
                {
                    ChannelMembers.Add(_client.CurrentUser);
                }
                if (channel.Recipients is not null)
                {
                    foreach (var r in channel.Recipients)
                    {
                        _client.ApplyCachedPresenceToUser(r);
                        if (seenUsers.Add(r.Id)) ChannelMembers.Add(r);
                    }
                }

                foreach (var message in history.OrderBy(m => m.Timestamp))
                {
                    _client.ApplyCachedPresenceToUser(message.Author);
                    ProcessMessageForGrouping(message, prev);
                    Messages.Add(message);
                    if (seenUsers.Add(message.Author.Id))
                    {
                        ChannelMembers.Add(message.Author);
                    }
                    prev = message;
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private bool _isLoadingOlderMessages;
    public async Task<int> LoadOlderMessagesAsync()
    {
        if (_selectedChannel is null || _isLoadingOlderMessages) return 0;
        var oldest = Messages.FirstOrDefault();
        if (oldest is null) return 0;

        _isLoadingOlderMessages = true;
        try
        {
            var older = await _client.Rest.GetMessagesAsync(_selectedChannel.Id, limit: 50, beforeId: oldest.Id);
            if (older.Count == 0) return 0;

            var ordered = older.OrderBy(m => m.Timestamp).ToList();
            DiscordMessage? prev = null;
            var seenUsers = new HashSet<string>(ChannelMembers.Select(u => u.Id));

            // Insérer au début de la collection
            for (int i = 0; i < ordered.Count; i++)
            {
                var msg = ordered[i];
                ProcessMessageForGrouping(msg, prev);
                Messages.Insert(i, msg);
                if (seenUsers.Add(msg.Author.Id))
                {
                    ChannelMembers.Add(msg.Author);
                }
                prev = msg;
            }

            // Mettre à jour l'en-tête du message qui était précédemment le premier
            if (Messages.Count > ordered.Count)
            {
                ProcessMessageForGrouping(Messages[ordered.Count], prev);
            }

            return older.Count;
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                $"[{DateTime.Now}] LoadOlderMessagesAsync failed: {ex}\n\n");
            return 0;
        }
        finally
        {
            _isLoadingOlderMessages = false;
        }
    }

    public async Task SendMessageAsync(string content)
    {
        if (_selectedChannel is null || string.IsNullOrWhiteSpace(content)) return;
        try
        {
            if (EditingMessage is not null)
            {
                var targetId = EditingMessage.Id;
                CancelComposerAction();
                var updated = await _client.Rest.EditMessageAsync(_selectedChannel.Id, targetId, content);
                OnMessageUpdated(updated);
                return;
            }

            // Auto-conversion en message.txt si > 4000 caractères (fonctionnalité SakuraCord)
            if (App.Settings.Current.ConvertLongMessagesToFile && content.Length > 4000)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                CancelComposerAction();
                await _client.Rest.SendBytesAsync(_selectedChannel.Id, bytes, "message.txt", "Message long envoyé sous forme de fichier :");
                return;
            }

            var replyId = ReplyingToMessage?.Id;
            CancelComposerAction();
            await _client.Rest.SendMessageAsync(_selectedChannel.Id, content, replyToMessageId: replyId);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                $"[{DateTime.Now}] SendMessageAsync failed: {ex}\n\n");
        }
    }

    public async Task SendFileAsync(string filePath)
    {
        if (_selectedChannel is null || string.IsNullOrWhiteSpace(filePath)) return;
        try
        {
            await _client.Rest.SendFileAsync(_selectedChannel.Id, filePath, anonymizeFilename: App.Settings.Current.AnonymizeUploads);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                $"[{DateTime.Now}] SendFileAsync failed: {ex}\n\n");
        }
    }

    public async Task SendBytesAsync(byte[] bytes, string fileName, string? caption = null)
    {
        if (_selectedChannel is null || bytes.Length == 0) return;
        try
        {
            var finalName = App.Settings.Current.AnonymizeUploads
                ? $"clipboard_{Guid.NewGuid().ToString("N")[..8]}{System.IO.Path.GetExtension(fileName)}"
                : fileName;
            await _client.Rest.SendBytesAsync(_selectedChannel.Id, bytes, finalName, caption);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                $"[{DateTime.Now}] SendBytesAsync failed: {ex}\n\n");
        }
    }

    public async Task LoadPinnedMessagesAsync()
    {
        if (_selectedChannel is null) return;
        PinnedMessages.Clear();
        try
        {
            var pins = await _client.Rest.GetPinnedMessagesAsync(_selectedChannel.Id);
            foreach (var pin in pins) PinnedMessages.Add(pin);
        }
        catch { }
    }

    public async Task TogglePinMessageAsync(DiscordMessage message)
    {
        if (_selectedChannel is null) return;
        try
        {
            if (message.Pinned)
            {
                await _client.Rest.UnpinMessageAsync(_selectedChannel.Id, message.Id);
                message.Pinned = false;
            }
            else
            {
                await _client.Rest.PinMessageAsync(_selectedChannel.Id, message.Id);
                message.Pinned = true;
            }
        }
        catch { }
    }

    public async Task LoadMentionsAsync()
    {
        RecentMentions.Clear();
        try
        {
            var mentions = await _client.Rest.GetMentionsAsync(25);
            foreach (var m in mentions) RecentMentions.Add(m);
        }
        catch { }
    }

    public async Task JoinServerByInviteAsync(string inviteCodeOrUrl)
    {
        await _client.Rest.JoinInviteAsync(inviteCodeOrUrl);
        await _client.RefreshGuildsAsync();
    }

    public async Task LeaveGuildAsync(DiscordGuild guild)
    {
        await _client.Rest.LeaveGuildAsync(guild.Id);
        Guilds.Remove(guild);
        if (_selectedGuild?.Id == guild.Id)
        {
            ShowFriendsView();
        }
    }

    public async Task<string> CreateInviteForCurrentChannelAsync()
    {
        if (_selectedChannel is null) return string.Empty;
        return await _client.Rest.CreateChannelInviteAsync(_selectedChannel.Id);
    }

    public async Task SendFriendRequestAsync(string username)
    {
        await _client.Rest.SendFriendRequestAsync(username);
        var updated = await _client.Rest.GetRelationshipsAsync();
        _client.Friends.Clear();
        foreach (var r in updated) _client.Friends.Add(r);
        RefreshFilteredFriends();
    }

    public async Task AcceptFriendRequestAsync(DiscordRelationship rel)
    {
        await _client.Rest.PutRelationshipAsync(rel.User.Id, 1);
        rel.Type = DiscordRelationshipType.Friend;
        RefreshFilteredFriends();
    }

    public async Task RemoveOrBlockRelationshipAsync(DiscordRelationship rel, bool block = false)
    {
        if (block)
        {
            await _client.Rest.PutRelationshipAsync(rel.User.Id, 2);
            rel.Type = DiscordRelationshipType.Blocked;
        }
        else
        {
            await _client.Rest.RemoveRelationshipAsync(rel.User.Id);
            _client.Friends.Remove(rel);
        }
        RefreshFilteredFriends();
    }

    public async Task OpenOrSendDirectMessageAsync(DiscordUser targetUser, string? initialMessage = null)
    {
        try
        {
            var existingDm = PrivateChannels.FirstOrDefault(c => c.Recipients?.Any(u => u.Id == targetUser.Id) == true);
            if (existingDm is null)
            {
                existingDm = await _client.Rest.CreateDmAsync(targetUser.Id);
                if (existingDm is not null && PrivateChannels.All(c => c.Id != existingDm.Id))
                {
                    PrivateChannels.Insert(0, existingDm);
                }
            }
            if (existingDm is not null)
            {
                await SelectChannelAsync(existingDm);
                if (!string.IsNullOrWhiteSpace(initialMessage))
                {
                    await _client.Rest.SendMessageAsync(existingDm.Id, initialMessage);
                }
            }
        }
        catch { }
    }

    public async Task AddReactionAsync(string messageId, string emoji)
    {
        if (_selectedChannel is null) return;
        try
        {
            await _client.Rest.AddReactionAsync(_selectedChannel.Id, messageId, emoji);
        }
        catch (Exception) { }
    }

    public async Task DeleteMessageAsync(string messageId)
    {
        if (_selectedChannel is null) return;
        try
        {
            await _client.Rest.DeleteMessageAsync(_selectedChannel.Id, messageId);
        }
        catch (Exception) { }
    }

    private void OnMessageReceived(DiscordMessage message)
    {
        if (message.ChannelId != _selectedChannel?.Id) return;
        Protocol.App.RunOnUiThread(() =>
        {
            _client.ApplyCachedPresenceToUser(message.Author);
            ProcessMessageForGrouping(message, Messages.LastOrDefault());
            Messages.Add(message);
            if (ChannelMembers.All(u => u.Id != message.Author.Id))
            {
                ChannelMembers.Add(message.Author);
            }
        });
    }

    private void OnMessageUpdated(DiscordMessage message)
    {
        if (message.ChannelId != _selectedChannel?.Id) return;
        Protocol.App.RunOnUiThread(() =>
        {
            var existing = Messages.FirstOrDefault(m => m.Id == message.Id);
            if (existing != null)
            {
                var idx = Messages.IndexOf(existing);
                message.ShowHeader = existing.ShowHeader;
                Messages[idx] = message;
            }
        });
    }

    private void OnMessageDeleted(string channelId, string messageId)
    {
        if (channelId != _selectedChannel?.Id) return;
        Protocol.App.RunOnUiThread(() =>
        {
            var existing = Messages.FirstOrDefault(m => m.Id == messageId);
            if (existing != null) Messages.Remove(existing);
        });
    }

    private void OnReactionAdded(string channelId, string messageId, DiscordEmoji emoji)
    {
        if (channelId != _selectedChannel?.Id) return;
        Protocol.App.RunOnUiThread(() =>
        {
            var existing = Messages.FirstOrDefault(m => m.Id == messageId);
            if (existing != null)
            {
                var reaction = existing.Reactions.FirstOrDefault(r => r.Emoji.Name == emoji.Name);
                if (reaction != null)
                {
                    reaction.Count++;
                }
                else
                {
                    existing.Reactions.Add(new DiscordReaction { Count = 1, Emoji = emoji });
                }
                var idx = Messages.IndexOf(existing);
                Messages[idx] = existing;
            }
        });
    }

    private void ProcessMessageForGrouping(DiscordMessage current, DiscordMessage? previous)
    {
        if (previous == null) return;
        if (!current.IsNormal || !previous.IsNormal) return;
        if (current.IsReply) return; // Toujours afficher l'en-tête pour une réponse
        if (current.Author.Id != previous.Author.Id) return;

        var timeDiff = current.Timestamp - previous.Timestamp;
        if (timeDiff.TotalMinutes < 7)
        {
            current.ShowHeader = false;
        }
    }

    private void OnVoiceParticipantsChanged(string channelId)
    {
        if (channelId != _selectedChannel?.Id) return;
        Protocol.App.RunOnUiThread(() => RefreshVoiceParticipants(channelId));
    }

    private void RefreshVoiceParticipants(string channelId)
    {
        VoiceParticipants.Clear();
        var seen = new HashSet<string>();
        foreach (var user in _client.GetVoiceChannelParticipants(channelId))
        {
            if (seen.Add(user.Id))
            {
                VoiceParticipants.Add(user);
            }
        }
        if (IsVoiceConnected && _selectedChannel?.Id == channelId && _client.CurrentUser is not null && seen.Add(_client.CurrentUser.Id))
        {
            VoiceParticipants.Insert(0, _client.CurrentUser);
        }
    }

    public void DisconnectVoice()
    {
        _ = _client.LeaveVoiceChannelAsync();
        IsVoiceConnected = false;
        IsScreenSharing = false;
        IsCameraEnabled = false;
        ScreenShareTitle = string.Empty;
        ConnectedVoiceChannelName = string.Empty;
        VoiceParticipants.Clear();
        NotifyScreenShareAndCameraChanged();
        if (IsVoiceChannelSelected)
        {
            IsVoiceChannelSelected = false;
            OnPropertyChanged(nameof(VoiceViewVisibility));
            OnPropertyChanged(nameof(TextViewVisibility));
            OnPropertyChanged(nameof(MemberListVisibility));
        }
    }

    public async Task StartOrJoinVoiceCallInCurrentChannelAsync()
    {
        if (_selectedChannel is null) return;
        IsVoiceChannelSelected = true;
        OnPropertyChanged(nameof(VoiceViewVisibility));
        OnPropertyChanged(nameof(TextViewVisibility));
        OnPropertyChanged(nameof(MemberListVisibility));

        await _client.JoinVoiceChannelAsync(_selectedChannel.GuildId, _selectedChannel.Id, IsMuted, IsDeafened, IsCameraEnabled, IsScreenSharing);
        IsVoiceConnected = true;
        ConnectedVoiceChannelName = _selectedChannel.DisplayName;
        RefreshVoiceParticipants(_selectedChannel.Id);
    }

    public async Task StartScreenShareAsync(string title, string qualityBadge, bool shareSystemAudio)
    {
        if (!IsVoiceConnected && _selectedChannel is not null)
        {
            await StartOrJoinVoiceCallInCurrentChannelAsync();
        }
        if (!IsVoiceConnected) return;

        IsScreenSharing = true;
        ScreenShareTitle = title;
        ScreenShareQualityBadge = qualityBadge;
        await _client.StartScreenShareAsync(shareSystemAudio);
        NotifyScreenShareAndCameraChanged();
    }

    public async Task StopScreenShareAsync()
    {
        if (!IsScreenSharing) return;
        IsScreenSharing = false;
        ScreenShareTitle = string.Empty;
        await _client.StopScreenShareAsync();
        NotifyScreenShareAndCameraChanged();
    }

    public async Task ToggleCameraAsync()
    {
        if (!IsVoiceConnected && _selectedChannel is not null)
        {
            await StartOrJoinVoiceCallInCurrentChannelAsync();
        }
        if (!IsVoiceConnected) return;

        IsCameraEnabled = !IsCameraEnabled;
        await _client.ToggleVideoAsync(IsCameraEnabled);
        NotifyScreenShareAndCameraChanged();
    }

    private void NotifyScreenShareAndCameraChanged()
    {
        OnPropertyChanged(nameof(ScreenShareStageVisibility));
        OnPropertyChanged(nameof(ScreenShareOpacity));
        OnPropertyChanged(nameof(ScreenShareIconBrush));
        OnPropertyChanged(nameof(ScreenShareTooltipText));
        OnPropertyChanged(nameof(CameraOpacity));
        OnPropertyChanged(nameof(CameraIconBrush));
        OnPropertyChanged(nameof(CameraTooltipText));
    }

    public async Task CreatePollInCurrentChannelAsync(string question, List<string> answers, int durationHours, bool allowMultiselect)
    {
        if (_selectedChannel is null) return;
        await _client.Rest.CreatePollMessageAsync(_selectedChannel.Id, question, answers, durationHours, allowMultiselect);
    }

    private DateTimeOffset _lastTypingSent = DateTimeOffset.MinValue;
    public void NotifyTypingInCurrentChannel()
    {
        if (_selectedChannel is null) return;
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastTypingSent).TotalSeconds < 8) return;
        _lastTypingSent = now;
        _ = _client.Rest.TriggerTypingAsync(_selectedChannel.Id);
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        OnPropertyChanged(nameof(MuteOpacity));
        if (_selectedChannel is not null && IsVoiceConnected)
        {
            _ = _client.JoinVoiceChannelAsync(_selectedChannel.GuildId, _selectedChannel.Id, IsMuted, IsDeafened, IsCameraEnabled, IsScreenSharing);
        }
    }

    public void ToggleDeafen()
    {
        IsDeafened = !IsDeafened;
        if (IsDeafened) IsMuted = true;
        OnPropertyChanged(nameof(DeafenOpacity));
        OnPropertyChanged(nameof(MuteOpacity));
        if (_selectedChannel is not null && IsVoiceConnected)
        {
            _ = _client.JoinVoiceChannelAsync(_selectedChannel.GuildId, _selectedChannel.Id, IsMuted, IsDeafened, IsCameraEnabled, IsScreenSharing);
        }
    }

    public void ToggleKrisp()
    {
        SetKrispEnabled(!IsKrispEnabled);
    }

    public void SetKrispEnabled(bool enabled)
    {
        IsKrispEnabled = enabled;
        _client.IsKrispEnabled = enabled;
        App.Settings.Current.EnableKrispNoiseSuppression = enabled;
        App.Settings.Save();
        OnPropertyChanged(nameof(KrispOpacity));
        OnPropertyChanged(nameof(KrispIconBrush));
        OnPropertyChanged(nameof(KrispTooltipText));
    }

    public void SetStatus(string status)
    {
        CurrentStatus = status;
        OnPropertyChanged(nameof(StatusDotBrush));
        OnPropertyChanged(nameof(CurrentStatusDisplayName));
        OnPropertyChanged(nameof(CurrentUserSubStatusText));
        _ = _client.Rest.UpdateCustomStatusAsync(customStatusText: null, status: status);
        _ = _client.BroadcastCurrentPresenceAsync(status);
    }

    public async Task SetCustomRichPresenceAsync(DiscordActivity? customActivity)
    {
        await _client.SetCustomRichPresenceAsync(customActivity, CurrentStatus);
        NotifyCurrentUserRichPresenceChanged();
    }

    public void ApplySearchFilter(string query)
    {
        SearchFilterText = query;
        FilteredChannels.Clear();
        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var ch in Channels) FilteredChannels.Add(ch);
            return;
        }

        var lower = query.ToLowerInvariant();
        foreach (var ch in Channels.Where(c => c.DisplayName.ToLowerInvariant().Contains(lower)))
        {
            FilteredChannels.Add(ch);
        }
    }
}
