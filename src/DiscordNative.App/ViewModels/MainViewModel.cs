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

    public double MuteOpacity => IsMuted ? 0.4 : 1.0;
    public double DeafenOpacity => IsDeafened ? 0.4 : 1.0;

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

    public Microsoft.UI.Xaml.Visibility ShowingGuildChannels => _showingFriends ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility ShowingFriendsView => _showingFriends ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public MainViewModel(DiscordClient client)
    {
        _client = client;
        _client.MessageReceived += OnMessageReceived;
        _client.MessageUpdated += OnMessageUpdated;
        _client.MessageDeleted += OnMessageDeleted;
        _client.ReactionAdded += OnReactionAdded;
        _client.Friends.CollectionChanged += (_, _) => RefreshFilteredFriends();
    }

    public void OnUserLoaded()
    {
        CurrentUserName = _client.CurrentUser?.DisplayName ?? "Utilisateur inconnu";
        OnPropertyChanged(nameof(CurrentUser));
        OnPropertyChanged(nameof(CurrentUserAvatarUrl));
        OnPropertyChanged(nameof(CurrentUserBannerUrl));
        OnPropertyChanged(nameof(CurrentUserHasBanner));
        OnPropertyChanged(nameof(CurrentUserTag));
        OnPropertyChanged(nameof(CurrentUserBio));
        RefreshFilteredFriends();
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

    public async Task UpdateProfileAsync(string newDisplayName, string newBio)
    {
        try
        {
            var updated = await _client.Rest.ModifyCurrentUserAsync(globalName: newDisplayName, bio: newBio);
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
                _ = _client.JoinVoiceChannelAsync(channel.GuildId, channel.Id);
                IsVoiceConnected = true;
                ConnectedVoiceChannelName = channel.DisplayName;
            }

            VoiceParticipants.Clear();
            if (_client.CurrentUser != null)
            {
                VoiceParticipants.Add(_client.CurrentUser);
            }
            if (_client.Friends.Count > 0)
            {
                foreach (var friend in _client.Friends.Take(4))
                {
                    VoiceParticipants.Add(friend.User);
                }
            }
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
                        if (seenUsers.Add(r.Id)) ChannelMembers.Add(r);
                    }
                }

                foreach (var message in history.OrderBy(m => m.Timestamp))
                {
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
                await _client.Rest.SendBytesAsync(_selectedChannel.Id, bytes, "message.txt", "📄 Message long envoyé sous forme de fichier :");
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
        var guilds = await _client.Rest.GetGuildsAsync();
        foreach (var g in guilds)
        {
            if (Guilds.All(existing => existing.Id != g.Id))
            {
                Guilds.Add(g);
            }
        }
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

    public void DisconnectVoice()
    {
        if (_selectedChannel?.GuildId != null)
        {
            _ = _client.JoinVoiceChannelAsync(_selectedChannel.GuildId, null);
        }
        IsVoiceConnected = false;
        ConnectedVoiceChannelName = string.Empty;
        if (IsVoiceChannelSelected)
        {
            IsVoiceChannelSelected = false;
            OnPropertyChanged(nameof(VoiceViewVisibility));
            OnPropertyChanged(nameof(TextViewVisibility));
            OnPropertyChanged(nameof(MemberListVisibility));
        }
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        OnPropertyChanged(nameof(MuteOpacity));
        if (_selectedChannel?.GuildId != null && IsVoiceConnected)
        {
            _ = _client.JoinVoiceChannelAsync(_selectedChannel.GuildId, _selectedChannel.Id, IsMuted, IsDeafened);
        }
    }

    public void ToggleDeafen()
    {
        IsDeafened = !IsDeafened;
        if (IsDeafened) IsMuted = true;
        OnPropertyChanged(nameof(DeafenOpacity));
        OnPropertyChanged(nameof(MuteOpacity));
        if (_selectedChannel?.GuildId != null && IsVoiceConnected)
        {
            _ = _client.JoinVoiceChannelAsync(_selectedChannel.GuildId, _selectedChannel.Id, IsMuted, IsDeafened);
        }
    }

    public void SetStatus(string status)
    {
        CurrentStatus = status;
        OnPropertyChanged(nameof(StatusDotBrush));
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
