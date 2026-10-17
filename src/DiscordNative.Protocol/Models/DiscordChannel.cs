using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DiscordNative.Protocol.Models;

public enum DiscordChannelType
{
    GuildText = 0,
    Dm = 1,
    GuildVoice = 2,
    GroupDm = 3,
    GuildCategory = 4,
    GuildAnnouncement = 5,
    AnnouncementThread = 10,
    PublicThread = 11,
    PrivateThread = 12,
    GuildStageVoice = 13,
    GuildForum = 15,
}

public sealed class DiscordChannel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public DiscordChannelType Type { get; set; }

    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("recipients")]
    public List<DiscordUser>? Recipients { get; set; }

    [JsonPropertyName("last_message_id")]
    public string? LastMessageId { get; set; }

    [JsonPropertyName("user_limit")]
    public int? UserLimit { get; set; }

    [JsonPropertyName("permissions")]
    public string? Permissions { get; set; }

    [JsonPropertyName("permission_overwrites")]
    public List<DiscordPermissionOverwrite>? PermissionOverwrites { get; set; }

    private bool _canView = true;
    [JsonIgnore]
    public bool CanView
    {
        get => _canView;
        set
        {
            if (_canView != value)
            {
                _canView = value;
                OnPropertyChanged(nameof(CanView));
                OnPropertyChanged(nameof(IsLocked));
                OnPropertyChanged(nameof(ShowVoiceSpeakerIcon));
            }
        }
    }

    private bool _canConnect = true;
    [JsonIgnore]
    public bool CanConnect
    {
        get => _canConnect;
        set
        {
            if (_canConnect != value)
            {
                _canConnect = value;
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(IsLocked));
                OnPropertyChanged(nameof(ShowVoiceSpeakerIcon));
            }
        }
    }

    [JsonIgnore]
    public bool IsLocked => IsVoiceChannel && (!CanConnect || !CanView);

    [JsonIgnore]
    public bool ShowVoiceSpeakerIcon => IsVoiceChannel && !IsLocked;

    public bool IsTextLike => Type is DiscordChannelType.GuildText
        or DiscordChannelType.Dm
        or DiscordChannelType.GroupDm
        or DiscordChannelType.GuildAnnouncement
        or DiscordChannelType.PublicThread
        or DiscordChannelType.PrivateThread
        or DiscordChannelType.AnnouncementThread;

    public bool IsVoiceChannel => Type is DiscordChannelType.GuildVoice or DiscordChannelType.GuildStageVoice;

    [JsonIgnore]
    public ObservableCollection<DiscordUser> ConnectedVoiceMembers { get; } = new();

    [JsonIgnore]
    public bool HasConnectedVoiceMembers => ConnectedVoiceMembers.Count > 0;

    [JsonIgnore]
    public string VoiceLimitText => UserLimit.HasValue && UserLimit.Value > 0
        ? $"{ConnectedVoiceMembers.Count:D2} / {UserLimit.Value:D2}"
        : $"{ConnectedVoiceMembers.Count}";

    [JsonIgnore]
    public string VoiceSpeakerColorBrush => HasConnectedVoiceMembers ? "#23A55A" : "#949BA4";

    private int _mentionCount;
    [JsonIgnore]
    public int MentionCount
    {
        get => _mentionCount;
        set
        {
            if (_mentionCount != value)
            {
                _mentionCount = Math.Max(0, value);
                OnPropertyChanged(nameof(MentionCount));
                OnPropertyChanged(nameof(HasMentions));
                OnPropertyChanged(nameof(MentionCountText));
            }
        }
    }

    [JsonIgnore]
    public bool HasMentions => _mentionCount > 0;

    [JsonIgnore]
    public string MentionCountText => _mentionCount > 99 ? "99+" : _mentionCount.ToString();

    private bool _hasUnread;
    [JsonIgnore]
    public bool HasUnread
    {
        get => _hasUnread;
        set
        {
            if (_hasUnread != value)
            {
                _hasUnread = value;
                OnPropertyChanged(nameof(HasUnread));
            }
        }
    }

    public void NotifyVoiceMembersChanged()
    {
        OnPropertyChanged(nameof(ConnectedVoiceMembers));
        OnPropertyChanged(nameof(HasConnectedVoiceMembers));
        OnPropertyChanged(nameof(VoiceLimitText));
        OnPropertyChanged(nameof(VoiceSpeakerColorBrush));
    }

    /// <summary>Nom d'affichage : pour un DM, on utilise le nom du destinataire.</summary>
    public string DisplayName => Type switch
    {
        DiscordChannelType.Dm => Recipients?.FirstOrDefault()?.DisplayName ?? "DM inconnu",
        DiscordChannelType.GroupDm => Name ?? string.Join(", ", Recipients?.Select(r => r.DisplayName) ?? Array.Empty<string>()),
        _ => Name ?? "salon-inconnu",
    };

    /// <summary>URL de l'icône : photo de profil pour un DM.</summary>
    public string? IconUrl => Type switch
    {
        DiscordChannelType.Dm => Recipients?.FirstOrDefault()?.AvatarUrl,
        DiscordChannelType.GroupDm => null, // Discord handles group DM icons separately
        _ => null
    };
}

public sealed class DiscordPermissionOverwrite
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; } // 0 = role, 1 = member

    [JsonPropertyName("allow")]
    public string? Allow { get; set; }

    [JsonPropertyName("deny")]
    public string? Deny { get; set; }

    [JsonIgnore]
    public ulong AllowBits => ulong.TryParse(Allow, out var b) ? b : 0UL;

    [JsonIgnore]
    public ulong DenyBits => ulong.TryParse(Deny, out var b) ? b : 0UL;
}
