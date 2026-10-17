using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DiscordNative.Protocol.Models;

public sealed class DiscordUser : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void NotifyPresenceChanged()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(Activities));
        OnPropertyChanged(nameof(PrimaryActivity));
        OnPropertyChanged(nameof(CustomStatusActivity));
        OnPropertyChanged(nameof(HasCustomStatus));
        OnPropertyChanged(nameof(CustomStatusText));
        OnPropertyChanged(nameof(HasRichPresence));
        OnPropertyChanged(nameof(HasActivityOrCustomStatus));
        OnPropertyChanged(nameof(ActivitySummaryText));
    }

    public void NotifyProfileEnriched()
    {
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(AboutMe));
        OnPropertyChanged(nameof(Pronouns));
        OnPropertyChanged(nameof(HasPronouns));
        OnPropertyChanged(nameof(Banner));
        OnPropertyChanged(nameof(BannerUrl));
        OnPropertyChanged(nameof(HasBanner));
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(ThemeColors));
        OnPropertyChanged(nameof(HasCustomThemeColors));
        OnPropertyChanged(nameof(ProfilePrimaryColorHex));
        OnPropertyChanged(nameof(ProfileSecondaryColorHex));
        OnPropertyChanged(nameof(ProfileBorderColorHex));
        OnPropertyChanged(nameof(ProfilePillBackgroundColorHex));
        OnPropertyChanged(nameof(ProfileTextForegroundColorHex));
        OnPropertyChanged(nameof(MutualFriendsCount));
        OnPropertyChanged(nameof(MutualGuildsCount));
        OnPropertyChanged(nameof(HasMutualInfo));
        OnPropertyChanged(nameof(MutualSummaryText));
        OnPropertyChanged(nameof(Clan));
        OnPropertyChanged(nameof(PrimaryGuild));
        OnPropertyChanged(nameof(ActiveClan));
        OnPropertyChanged(nameof(HasClan));
        OnPropertyChanged(nameof(ClanTag));
        OnPropertyChanged(nameof(ClanBadgeUrl));
        OnPropertyChanged(nameof(Badges));
        OnPropertyChanged(nameof(HasBadges));
        OnPropertyChanged(nameof(ConnectedAccounts));
        OnPropertyChanged(nameof(HasConnectedAccounts));
        OnPropertyChanged(nameof(GameWidgets));
        OnPropertyChanged(nameof(HasGameWidgets));
    }

    public bool HasProfileEnriched { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("discriminator")]
    public string Discriminator { get; set; } = "0";

    [JsonPropertyName("global_name")]
    public string? GlobalName { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("bot")]
    public bool Bot { get; set; }

    [JsonPropertyName("banner")]
    public string? Banner { get; set; }

    [JsonPropertyName("accent_color")]
    public int? AccentColor { get; set; }

    public List<int> ThemeColors { get; set; } = new();
    public bool HasCustomThemeColors => ThemeColors.Count >= 2;

    public string ProfilePrimaryColorHex => HasCustomThemeColors
        ? $"#{ThemeColors[0]:X6}"
        : (AccentColor is int ac ? $"#{ac:X6}" : "#232428");

    public string ProfileSecondaryColorHex => HasCustomThemeColors
        ? $"#{ThemeColors[1]:X6}"
        : (AccentColor is int ac ? $"#{ac:X6}" : "#111214");

    public string ProfileBorderColorHex => HasCustomThemeColors
        ? $"#{ThemeColors[0]:X6}"
        : (AccentColor is int ac ? $"#{ac:X6}" : "#383A40");

    public string ProfilePillBackgroundColorHex
    {
        get
        {
            if (!HasCustomThemeColors) return "#33FFFFFF";
            return IsThemeLight ? "#55FFFFFF" : "#44000000";
        }
    }

    public string ProfileTextForegroundColorHex => (HasCustomThemeColors && IsThemeLight) ? "#1E1F22" : "#FFFFFF";

    public bool IsThemeLight
    {
        get
        {
            if (!HasCustomThemeColors) return false;
            int c0 = ThemeColors[0];
            int c1 = ThemeColors[1];
            double lum0 = (0.299 * ((c0 >> 16) & 0xFF)) + (0.587 * ((c0 >> 8) & 0xFF)) + (0.114 * (c0 & 0xFF));
            double lum1 = (0.299 * ((c1 >> 16) & 0xFF)) + (0.587 * ((c1 >> 8) & 0xFF)) + (0.114 * (c1 & 0xFF));
            return ((lum0 + lum1) / 2.0) > 145.0;
        }
    }

    public int MutualFriendsCount { get; set; }
    public int MutualGuildsCount { get; set; }
    public bool HasMutualInfo => MutualFriendsCount > 0 || MutualGuildsCount > 0;
    public string MutualSummaryText
    {
        get
        {
            var parts = new List<string>();
            if (MutualFriendsCount > 0) parts.Add($"{MutualFriendsCount} Mutual Friend{(MutualFriendsCount > 1 ? "s" : "")}");
            if (MutualGuildsCount > 0) parts.Add($"{MutualGuildsCount} Mutual Server{(MutualGuildsCount > 1 ? "s" : "")}");
            return string.Join(" • ", parts);
        }
    }

    [JsonPropertyName("bio")]
    public string? Bio { get; set; }

    [JsonPropertyName("pronouns")]
    public string? Pronouns { get; set; }

    public bool HasPronouns => !string.IsNullOrWhiteSpace(Pronouns);

    [JsonPropertyName("premium_type")]
    public int PremiumType { get; set; }

    /// <summary>
    /// Vrai si le compte possède Nitro complet (premium_type == 2) ou si un badge "premium" (Subscriber Nitro) est présent sur le profil.
    /// </summary>
    public bool HasFullNitro => PremiumType == 2 || Badges.Any(b => b.Id.StartsWith("premium", StringComparison.OrdinalIgnoreCase));

    public string MemberSinceText
    {
        get
        {
            if (ulong.TryParse(Id, out ulong snowflake))
            {
                long timestampMs = (long)(snowflake >> 22) + 1420070400000L;
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).LocalDateTime;
                return dt.ToString("MMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture);
            }
            return "Sep 21, 2021";
        }
    }

    [JsonPropertyName("clan")]
    public DiscordClan? Clan { get; set; }

    [JsonPropertyName("primary_guild")]
    public DiscordClan? PrimaryGuild { get; set; }

    public DiscordClan? ActiveClan => Clan ?? PrimaryGuild;
    public bool HasClan => ActiveClan is { IdentityEnabled: true } && !string.IsNullOrWhiteSpace(ActiveClan.Tag);
    public string ClanTag => ActiveClan?.Tag ?? string.Empty;
    public string? ClanBadgeUrl => ActiveClan?.BadgeUrl;

    public List<DiscordBadge> Badges { get; set; } = new();
    public bool HasBadges => Badges.Count > 0;

    public List<DiscordConnectedAccount> ConnectedAccounts { get; set; } = new();
    public bool HasConnectedAccounts => ConnectedAccounts.Count > 0;

    public List<DiscordWidgetGame> GameWidgets { get; set; } = new();
    public bool HasGameWidgets => GameWidgets.Count > 0;

    public List<DiscordActivity> Activities { get; set; } = new();

    public DiscordActivity? PrimaryActivity => Activities.FirstOrDefault(a => a.Type != 4 && a.Type != 6);
    public DiscordActivity? CustomStatusActivity => Activities.FirstOrDefault(a => a.Type == 4 && !string.IsNullOrWhiteSpace(a.State));
    public bool HasCustomStatus => CustomStatusActivity is not null && !string.IsNullOrWhiteSpace(CustomStatusActivity.State);
    public string CustomStatusText => CustomStatusActivity?.State ?? string.Empty;
    public bool HasRichPresence => PrimaryActivity is not null;
    public bool HasActivityOrCustomStatus => PrimaryActivity is not null || CustomStatusActivity is not null;
    public void NotifyActivityTimesChanged()
    {
        PrimaryActivity?.NotifyTimeChanged();
        OnPropertyChanged(nameof(PrimaryActivity));
    }

    public string ActivitySummaryText
    {
        get
        {
            var act = PrimaryActivity;
            if (act is not null)
            {
                return act.SummaryLine;
            }
            var cs = CustomStatusActivity;
            if (cs is not null && !string.IsNullOrWhiteSpace(cs.State))
            {
                return cs.State;
            }
            return string.Empty;
        }
    }

    public string DisplayName => GlobalName ?? Username;

    public bool IsAnimatedAvatar => Avatar is not null && Avatar.StartsWith("a_");
    public bool IsAnimatedBanner => Banner is not null && Banner.StartsWith("a_");

    public string StaticAvatarUrl => Avatar is null
        ? $"https://cdn.discordapp.com/embed/avatars/{(int.TryParse(Discriminator, out var d) ? d % 5 : 0)}.png"
        : $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.png?size=128";

    public string AnimatedAvatarUrl => Avatar is null
        ? StaticAvatarUrl
        : $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.{(Avatar.StartsWith("a_") ? "gif" : "png")}?size=128";

    public string AvatarUrl => AnimatedAvatarUrl;

    public string? StaticBannerUrl => Banner is null
        ? null
        : $"https://cdn.discordapp.com/banners/{Id}/{Banner}.png?size=512";

    public string? AnimatedBannerUrl => Banner is null
        ? null
        : $"https://cdn.discordapp.com/banners/{Id}/{Banner}.{(Banner.StartsWith("a_") ? "gif" : "png")}?size=512";

    public string? BannerUrl => Banner is null
        ? null
        : $"https://cdn.discordapp.com/banners/{Id}/{Banner}.{(Banner.StartsWith("a_") ? "gif" : "png")}?size=512";

    public bool HasBanner => Banner is not null;

    public string AboutMe => string.IsNullOrWhiteSpace(Bio) ? "Grand voyageur de salons Discord." : Bio;

    public string UserTag => Discriminator == "0" ? $"@{Username}" : $"{Username}#{Discriminator}";

    public string Status { get; set; } = "online";

    private bool _isSpeaking;
    public bool IsSpeaking
    {
        get => _isSpeaking;
        set
        {
            if (_isSpeaking != value)
            {
                _isSpeaking = value;
                OnPropertyChanged(nameof(IsSpeaking));
                OnPropertyChanged(nameof(SpeakingRingOpacity));
                OnPropertyChanged(nameof(SpeakingCardBorderBrush));
                OnPropertyChanged(nameof(SpeakingCardBorderThickness));
            }
        }
    }

    public double SpeakingRingOpacity => _isSpeaking ? 1.0 : 0.0;
    public string SpeakingCardBorderBrush => _isSpeaking ? "#23A55A" : "#33FFFFFF";
    public int SpeakingCardBorderThickness => _isSpeaking ? 3 : 1;

    private bool _isStreaming;
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming != value)
            {
                _isStreaming = value;
                OnPropertyChanged(nameof(IsStreaming));
                OnPropertyChanged(nameof(StreamingBadgeOpacity));
            }
        }
    }
    public double StreamingBadgeOpacity => _isStreaming ? 1.0 : 0.0;

    private bool _isVideoEnabled;
    public bool IsVideoEnabled
    {
        get => _isVideoEnabled;
        set
        {
            if (_isVideoEnabled != value)
            {
                _isVideoEnabled = value;
                OnPropertyChanged(nameof(IsVideoEnabled));
                OnPropertyChanged(nameof(VideoBadgeOpacity));
            }
        }
    }
    public double VideoBadgeOpacity => _isVideoEnabled ? 1.0 : 0.0;

    private bool _isVoiceMuted;
    public bool IsVoiceMuted
    {
        get => _isVoiceMuted;
        set
        {
            if (_isVoiceMuted != value)
            {
                _isVoiceMuted = value;
                OnPropertyChanged(nameof(IsVoiceMuted));
                OnPropertyChanged(nameof(VoiceMutedOpacity));
            }
        }
    }
    public double VoiceMutedOpacity => _isVoiceMuted ? 1.0 : 0.0;

    private bool _isVoiceDeafened;
    public bool IsVoiceDeafened
    {
        get => _isVoiceDeafened;
        set
        {
            if (_isVoiceDeafened != value)
            {
                _isVoiceDeafened = value;
                OnPropertyChanged(nameof(IsVoiceDeafened));
                OnPropertyChanged(nameof(VoiceDeafenedOpacity));
            }
        }
    }
    public double VoiceDeafenedOpacity => _isVoiceDeafened ? 1.0 : 0.0;

    public string StatusColor => Status switch
    {
        "online" => "#23A55A",
        "idle" => "#F0B232",
        "dnd" => "#F23F43",
        _ => "#80848E"
    };
}

public sealed class DiscordActivity : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void NotifyTimeChanged()
    {
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(HasElapsed));
    }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("application_id")]
    public string? ApplicationId { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("timestamps")]
    public DiscordActivityTimestamps? Timestamps { get; set; }

    [JsonPropertyName("assets")]
    public DiscordActivityAssets? Assets { get; set; }

    [JsonPropertyName("buttons")]
    public List<string>? Buttons { get; set; }

    public string HeaderLabel => Type switch
    {
        0 => "EN TRAIN DE JOUER",
        1 => "EN DIRECT SUR STREAM",
        2 => $"ÉCOUTE {Name.ToUpperInvariant()}",
        3 => $"REGARDE {Name.ToUpperInvariant()}",
        5 => $"PARTICIPE À {Name.ToUpperInvariant()}",
        _ => "ACTIVITÉ EN COURS"
    };

    public string SummaryLine => Type switch
    {
        0 => $"Joue à {Name}",
        1 => $"En stream : {Details ?? Name}",
        2 => $"Écoute {Details ?? Name}",
        3 => $"Regarde {Name}",
        5 => $"Participe à {Name}",
        _ => Name
    };

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public bool HasState => !string.IsNullOrWhiteSpace(State);
    public bool HasLargeImage => !string.IsNullOrWhiteSpace(LargeImageUrl);
    public bool HasSmallImage => !string.IsNullOrWhiteSpace(SmallImageUrl);
    public bool HasElapsed => !string.IsNullOrWhiteSpace(ElapsedText);

    public string? LargeImageUrl => ResolveAssetUrl(ApplicationId, Assets?.LargeImage);
    public string? SmallImageUrl => ResolveAssetUrl(ApplicationId, Assets?.SmallImage);

    public string ElapsedText
    {
        get
        {
            if (Timestamps?.Start is not long startMs || startMs <= 0) return string.Empty;
            var now = DateTimeOffset.UtcNow;
            var start = DateTimeOffset.FromUnixTimeMilliseconds(startMs);
            var elapsed = now - start;
            if (elapsed.TotalSeconds < 0) elapsed = TimeSpan.Zero;

            // Pour Spotify ou musiques avec durée de fin (Timestamps.End)
            if (Timestamps?.End is long endMs && endMs > startMs)
            {
                var total = DateTimeOffset.FromUnixTimeMilliseconds(endMs) - start;
                if (elapsed > total) elapsed = total;
                return $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00} / {(int)total.TotalMinutes:00}:{total.Seconds:00}";
            }

            return elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00} écoulées"
                : $"{elapsed.Minutes:00}:{elapsed.Seconds:00} écoulées";
        }
    }

    private static string? ResolveAssetUrl(string? appId, string? assetKey)
    {
        if (string.IsNullOrWhiteSpace(assetKey)) return null;
        if (assetKey.StartsWith("mp:", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://media.discordapp.net/{assetKey[3..]}";
        }
        if (assetKey.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://i.scdn.co/image/{assetKey[8..]}";
        }
        if (assetKey.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            assetKey.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return assetKey;
        }
        if (!string.IsNullOrWhiteSpace(appId))
        {
            return $"https://cdn.discordapp.com/app-assets/{appId}/{assetKey}.png?size=160";
        }
        return null;
    }
}

public sealed class DiscordActivityTimestamps
{
    [JsonPropertyName("start")]
    public long? Start { get; set; }

    [JsonPropertyName("end")]
    public long? End { get; set; }
}

public sealed class DiscordActivityAssets
{
    [JsonPropertyName("large_image")]
    public string? LargeImage { get; set; }

    [JsonPropertyName("large_text")]
    public string? LargeText { get; set; }

    [JsonPropertyName("small_image")]
    public string? SmallImage { get; set; }

    [JsonPropertyName("small_text")]
    public string? SmallText { get; set; }
}

public sealed class DiscordClan
{
    [JsonPropertyName("identity_guild_id")]
    public string? IdentityGuildId { get; set; }

    [JsonPropertyName("identity_enabled")]
    public bool IdentityEnabled { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("badge")]
    public string? Badge { get; set; }

    public string? BadgeUrl => (!string.IsNullOrWhiteSpace(IdentityGuildId) && !string.IsNullOrWhiteSpace(Badge))
        ? $"https://cdn.discordapp.com/clan-badges/{IdentityGuildId}/{Badge}.png?size=32"
        : null;
}

public sealed class DiscordBadge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    public string IconUrl => $"https://cdn.discordapp.com/badge-icons/{Icon}.png";
}

public sealed class DiscordConnectedAccount
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    /// <summary>
    /// Visibilité sur le profil : 1 = visible (par défaut sur /users/{id}/profile), 0 = masqué sur /users/@me/connections.
    /// </summary>
    [JsonPropertyName("visibility")]
    public int Visibility { get; set; } = 1;

    public string PlatformDisplayName => Type.ToLowerInvariant() switch
    {
        "github" => "GitHub",
        "steam" => "Steam",
        "spotify" => "Spotify",
        "twitch" => "Twitch",
        "youtube" => "YouTube",
        "twitter" or "x" => "X (Twitter)",
        "reddit" => "Reddit",
        "playstation" => "PlayStation Network",
        "xbox" => "Xbox",
        "battlenet" => "Battle.net",
        "riotgames" => "Riot Games",
        "epicgames" => "Epic Games",
        "roblox" => "Roblox",
        "tiktok" => "TikTok",
        "instagram" => "Instagram",
        "bluesky" => "Bluesky",
        "mastodon" => "Mastodon",
        "paypal" => "PayPal",
        "ebay" => "eBay",
        "crunchyroll" => "Crunchyroll",
        "domain" => "Site Web / Domaine",
        "bungie" => "Bungie.net",
        "leagueoflegends" => "League of Legends",
        _ => string.IsNullOrWhiteSpace(Type) ? "Compte lié" : char.ToUpperInvariant(Type[0]) + Type[1..]
    };

    public string PlatformBadgeText => Type.ToLowerInvariant() switch
    {
        "github" => "GH",
        "steam" => "ST",
        "spotify" => "🎵",
        "twitch" => "TW",
        "youtube" => "▶",
        "twitter" or "x" => "𝕏",
        "reddit" => "RD",
        "playstation" => "PS",
        "xbox" => "XB",
        "battlenet" => "BN",
        "riotgames" => "RG",
        "epicgames" => "EG",
        "roblox" => "RBX",
        "tiktok" => "TT",
        "instagram" => "IG",
        "bluesky" => "🦋",
        "domain" => "🌐",
        _ => "🔗"
    };

    public string? ProfileUrl => Type.ToLowerInvariant() switch
    {
        "github" when !string.IsNullOrWhiteSpace(Name) => $"https://github.com/{Name}",
        "steam" when !string.IsNullOrWhiteSpace(Id) => $"https://steamcommunity.com/profiles/{Id}",
        "spotify" when !string.IsNullOrWhiteSpace(Id) => $"https://open.spotify.com/user/{Id}",
        "twitch" when !string.IsNullOrWhiteSpace(Name) => $"https://www.twitch.tv/{Name}",
        "youtube" when !string.IsNullOrWhiteSpace(Id) => $"https://www.youtube.com/channel/{Id}",
        "twitter" or "x" when !string.IsNullOrWhiteSpace(Name) => $"https://x.com/{Name}",
        "reddit" when !string.IsNullOrWhiteSpace(Name) => $"https://www.reddit.com/user/{Name}",
        "tiktok" when !string.IsNullOrWhiteSpace(Name) => $"https://www.tiktok.com/@{Name.TrimStart('@')}",
        "instagram" when !string.IsNullOrWhiteSpace(Name) => $"https://www.instagram.com/{Name.TrimStart('@')}",
        "bluesky" when !string.IsNullOrWhiteSpace(Name) => $"https://bsky.app/profile/{Name.TrimStart('@')}",
        "roblox" when !string.IsNullOrWhiteSpace(Id) => $"https://www.roblox.com/users/{Id}/profile",
        "domain" when !string.IsNullOrWhiteSpace(Name) => Name.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Name : $"https://{Name}",
        _ when Name.Contains('.') && !Name.Contains(' ') => Name.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Name : $"https://{Name}",
        _ => null
    };

    public bool HasUrl => !string.IsNullOrWhiteSpace(ProfileUrl);
    public string TooltipText => HasUrl ? $"{PlatformDisplayName} : {Name}\nCliquez pour ouvrir {ProfileUrl}" : $"{PlatformDisplayName} : {Name} (cliquez pour copier)";
}

public sealed class DiscordWidgetGame
{
    public string GameId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public bool IsFavorite { get; set; }
    public string TooltipText => string.IsNullOrWhiteSpace(Comment) ? Name : $"{Name} — \"{Comment}\"";
}
