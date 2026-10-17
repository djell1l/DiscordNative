using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DiscordNative.Protocol.Models;

public sealed class DiscordGuild : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("owner_id")]
    public string? OwnerId { get; set; }

    [JsonPropertyName("channels")]
    public List<DiscordChannel> Channels { get; set; } = new();

    [JsonPropertyName("roles")]
    public List<DiscordRole> Roles { get; set; } = new();

    public bool IsAnimated => Icon is not null && Icon.StartsWith("a_");

    public string? StaticIconUrl => Icon is null
        ? null
        : $"https://cdn.discordapp.com/icons/{Id}/{Icon}.png?size=128";

    public string? AnimatedIconUrl => Icon is null
        ? null
        : $"https://cdn.discordapp.com/icons/{Id}/{Icon}.{(Icon.StartsWith("a_") ? "gif" : "png")}?size=128";

    public string? IconUrl => AnimatedIconUrl;

    public bool HasIcon => Icon is not null;
    public bool ShowInitials => Icon is null;

    /// <summary>Initiales affichées quand il n'y a pas d'icône (comme le client officiel).</summary>
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length switch
            {
                0 => "?",
                1 => words[0].Length >= 2 ? words[0][..2].ToUpperInvariant() : words[0].ToUpperInvariant(),
                _ => string.Concat(words.Take(3).Select(w => w[0])).ToUpperInvariant(),
            };
        }
    }

    // ---------- Propriétés de notification / mention / activité ----------

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
                OnPropertyChanged(nameof(MentionTooltip));
                OnPropertyChanged(nameof(IndicatorHeight));
                OnPropertyChanged(nameof(HasUnreadOrSelected));
            }
        }
    }

    [JsonIgnore]
    public bool HasMentions => _mentionCount > 0;

    [JsonIgnore]
    public string MentionCountText => _mentionCount > 99 ? "99+" : _mentionCount.ToString();

    [JsonIgnore]
    public string MentionTooltip => _mentionCount switch
    {
        <= 0 => string.Empty,
        1 => "1 mention non lue",
        _ => $"{_mentionCount} mentions non lues"
    };

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
                OnPropertyChanged(nameof(IndicatorHeight));
                OnPropertyChanged(nameof(HasUnreadOrSelected));
            }
        }
    }

    private bool _isSelected;
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(IndicatorHeight));
                OnPropertyChanged(nameof(HasUnreadOrSelected));
            }
        }
    }

    private bool _isHovered;
    [JsonIgnore]
    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered != value)
            {
                _isHovered = value;
                OnPropertyChanged(nameof(IsHovered));
                OnPropertyChanged(nameof(IndicatorHeight));
                OnPropertyChanged(nameof(HasUnreadOrSelected));
            }
        }
    }

    [JsonIgnore]
    public bool HasUnreadOrSelected => IsSelected || IsHovered || HasMentions || HasUnread;

    [JsonIgnore]
    public double IndicatorHeight
    {
        get
        {
            if (IsSelected) return 40;
            if (IsHovered) return 20;
            if (HasMentions || HasUnread) return 8;
            return 0;
        }
    }

    private bool _hasVoiceActivity;
    [JsonIgnore]
    public bool HasVoiceActivity
    {
        get => _hasVoiceActivity;
        set
        {
            if (_hasVoiceActivity != value)
            {
                _hasVoiceActivity = value;
                OnPropertyChanged(nameof(HasVoiceActivity));
                OnPropertyChanged(nameof(HasVoiceBadge));
                OnPropertyChanged(nameof(HasStreamActivityAndNoVoice));
            }
        }
    }

    private bool _hasStreamActivity;
    [JsonIgnore]
    public bool HasStreamActivity
    {
        get => _hasStreamActivity;
        set
        {
            if (_hasStreamActivity != value)
            {
                _hasStreamActivity = value;
                OnPropertyChanged(nameof(HasStreamActivity));
                OnPropertyChanged(nameof(HasStreamBadge));
                OnPropertyChanged(nameof(HasVoiceBadge));
                OnPropertyChanged(nameof(HasStreamActivityAndNoVoice));
            }
        }
    }

    [JsonIgnore]
    public bool HasStreamBadge => HasStreamActivity;

    [JsonIgnore]
    public bool HasVoiceBadge => HasVoiceActivity && !HasStreamActivity;

    [JsonIgnore]
    public bool HasStreamActivityAndNoVoice => HasStreamActivity && !HasVoiceActivity;
}

public sealed class DiscordRole
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public int Color { get; set; }

    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("permissions")]
    public string? Permissions { get; set; }

    [JsonIgnore]
    public ulong PermissionBits => ulong.TryParse(Permissions, out var p) ? p : 0UL;
}
