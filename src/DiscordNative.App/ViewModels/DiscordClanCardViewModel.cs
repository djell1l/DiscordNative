using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using DiscordNative.Protocol.Models;

namespace DiscordNative.App.ViewModels;

public sealed class DiscordClanCardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _guildId = string.Empty;
    public string GuildId
    {
        get => _guildId;
        set { _guildId = value; OnPropertyChanged(nameof(GuildId)); }
    }

    private string _guildName = string.Empty;
    public string GuildName
    {
        get => _guildName;
        set { _guildName = value; OnPropertyChanged(nameof(GuildName)); }
    }

    private string _tag = string.Empty;
    public string Tag
    {
        get => _tag;
        set { _tag = value; OnPropertyChanged(nameof(Tag)); }
    }

    private string? _badgeUrl;
    public string? BadgeUrl
    {
        get => _badgeUrl;
        set { _badgeUrl = value; OnPropertyChanged(nameof(BadgeUrl)); }
    }

    private string? _iconUrl;
    public string? IconUrl
    {
        get => _iconUrl;
        set
        {
            _iconUrl = value;
            OnPropertyChanged(nameof(IconUrl));
            OnPropertyChanged(nameof(HasIcon));
        }
    }
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

    private string? _bannerUrl;
    public string? BannerUrl
    {
        get => _bannerUrl;
        set
        {
            _bannerUrl = value;
            OnPropertyChanged(nameof(BannerUrl));
            OnPropertyChanged(nameof(HasBanner));
        }
    }
    public bool HasBanner => !string.IsNullOrWhiteSpace(BannerUrl);

    private string? _description;
    public string? Description
    {
        get => _description;
        set
        {
            _description = value;
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(HasDescription));
        }
    }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    private string _onlineCountText = string.Empty;
    public string OnlineCountText
    {
        get => _onlineCountText;
        set
        {
            _onlineCountText = value;
            OnPropertyChanged(nameof(OnlineCountText));
            OnPropertyChanged(nameof(HasOnlineCount));
        }
    }
    public bool HasOnlineCount => !string.IsNullOrWhiteSpace(OnlineCountText);

    private string _memberCountText = string.Empty;
    public string MemberCountText
    {
        get => _memberCountText;
        set
        {
            _memberCountText = value;
            OnPropertyChanged(nameof(MemberCountText));
            OnPropertyChanged(nameof(HasMemberCount));
        }
    }
    public bool HasMemberCount => !string.IsNullOrWhiteSpace(MemberCountText);

    private string _establishedText = string.Empty;
    public string EstablishedText
    {
        get => _establishedText;
        set
        {
            _establishedText = value;
            OnPropertyChanged(nameof(EstablishedText));
            OnPropertyChanged(nameof(HasEstablished));
        }
    }
    public bool HasEstablished => !string.IsNullOrWhiteSpace(EstablishedText);

    private string? _associatedGameName;
    public string? AssociatedGameName
    {
        get => _associatedGameName;
        set
        {
            _associatedGameName = value;
            OnPropertyChanged(nameof(AssociatedGameName));
            OnPropertyChanged(nameof(HasAssociatedGame));
        }
    }
    public bool HasAssociatedGame => !string.IsNullOrWhiteSpace(AssociatedGameName);

    private string? _associatedGameIconUrl;
    public string? AssociatedGameIconUrl
    {
        get => _associatedGameIconUrl;
        set { _associatedGameIconUrl = value; OnPropertyChanged(nameof(AssociatedGameIconUrl)); }
    }

    private bool _isVerifiedOrPartner;
    public bool IsVerifiedOrPartner
    {
        get => _isVerifiedOrPartner;
        set { _isVerifiedOrPartner = value; OnPropertyChanged(nameof(IsVerifiedOrPartner)); }
    }

    private bool _isAlreadyJoined;
    public bool IsAlreadyJoined
    {
        get => _isAlreadyJoined;
        set
        {
            _isAlreadyJoined = value;
            OnPropertyChanged(nameof(IsAlreadyJoined));
            OnPropertyChanged(nameof(ActionButtonText));
        }
    }
    public string ActionButtonText => IsAlreadyJoined ? "Accéder au serveur" : "Rejoindre";

    private string? _vanityUrlCode;
    public string? VanityUrlCode
    {
        get => _vanityUrlCode;
        set { _vanityUrlCode = value; OnPropertyChanged(nameof(VanityUrlCode)); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
    }

    public static string ComputeEstablishedText(string guildId)
    {
        if (long.TryParse(guildId, out var snowflake))
        {
            long ms = (snowflake >> 22) + 1420070400000L;
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            return $"Est. {dt.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
        }
        return string.Empty;
    }

    public void UpdateFromGuild(DiscordGuild guild, string? tag = null, string? badgeUrl = null)
    {
        GuildId = guild.Id;
        GuildName = guild.Name;
        Tag = tag ?? Tag;
        BadgeUrl = badgeUrl ?? BadgeUrl;
        IconUrl = guild.IconUrl ?? BadgeUrl;
        BannerUrl = guild.BannerUrl;
        Description = guild.Description;
        EstablishedText = ComputeEstablishedText(guild.Id);
        IsAlreadyJoined = true;

        if (guild.ApproximatePresenceCount is int p)
        {
            OnlineCountText = $"{p:N0} En ligne";
        }
        if (guild.ApproximateMemberCount is int m)
        {
            MemberCountText = $"{m:N0} Membres";
        }
        else if (guild.Channels.Count > 0)
        {
            MemberCountText = $"{guild.Channels.Count} Salons";
        }

        IsVerifiedOrPartner = guild.Features.Any(f => f.Equals("VERIFIED", StringComparison.OrdinalIgnoreCase) ||
                                                      f.Equals("PARTNERED", StringComparison.OrdinalIgnoreCase) ||
                                                      f.Equals("COMMUNITY", StringComparison.OrdinalIgnoreCase));
        VanityUrlCode = guild.VanityUrlCode;
    }

    public void UpdateFromPreview(JsonElement root, string? fallbackTag = null, string? fallbackBadgeUrl = null)
    {
        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            GuildId = idEl.GetString() ?? GuildId;
        }

        if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
        {
            GuildName = nameEl.GetString() ?? GuildName;
        }

        if (root.TryGetProperty("icon", out var iconEl) && iconEl.ValueKind == JsonValueKind.String)
        {
            var hash = iconEl.GetString();
            if (!string.IsNullOrEmpty(hash))
            {
                IconUrl = $"https://cdn.discordapp.com/icons/{GuildId}/{hash}.png?size=128";
            }
        }
        if (string.IsNullOrEmpty(IconUrl))
        {
            IconUrl = fallbackBadgeUrl ?? BadgeUrl;
        }

        if (root.TryGetProperty("banner", out var banEl) && banEl.ValueKind == JsonValueKind.String)
        {
            var banHash = banEl.GetString();
            if (!string.IsNullOrEmpty(banHash))
            {
                BannerUrl = $"https://cdn.discordapp.com/banners/{GuildId}/{banHash}.png?size=512";
            }
        }
        else if (root.TryGetProperty("discovery_splash", out var dsEl) && dsEl.ValueKind == JsonValueKind.String)
        {
            var dsHash = dsEl.GetString();
            if (!string.IsNullOrEmpty(dsHash))
            {
                BannerUrl = $"https://cdn.discordapp.com/discovery-splashes/{GuildId}/{dsHash}.png?size=512";
            }
        }
        else if (root.TryGetProperty("splash", out var spEl) && spEl.ValueKind == JsonValueKind.String)
        {
            var spHash = spEl.GetString();
            if (!string.IsNullOrEmpty(spHash))
            {
                BannerUrl = $"https://cdn.discordapp.com/splashes/{GuildId}/{spHash}.png?size=512";
            }
        }

        if (root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
        {
            Description = descEl.GetString();
        }

        if (root.TryGetProperty("approximate_presence_count", out var apcEl) && apcEl.ValueKind == JsonValueKind.Number && apcEl.TryGetInt32(out int apcVal))
        {
            OnlineCountText = $"{apcVal:N0} En ligne";
        }

        if (root.TryGetProperty("approximate_member_count", out var amcEl) && amcEl.ValueKind == JsonValueKind.Number && amcEl.TryGetInt32(out int amcVal))
        {
            MemberCountText = $"{amcVal:N0} Membres";
        }

        if (root.TryGetProperty("features", out var featEl) && featEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in featEl.EnumerateArray())
            {
                var s = f.GetString();
                if (s is "VERIFIED" or "PARTNERED" or "COMMUNITY")
                {
                    IsVerifiedOrPartner = true;
                    break;
                }
            }
        }

        if (root.TryGetProperty("vanity_url_code", out var vanEl) && vanEl.ValueKind == JsonValueKind.String)
        {
            VanityUrlCode = vanEl.GetString();
        }

        // Si le profil de clan renvoie un jeu ou une activité associée (ex: FiveM, GTA V, etc.)
        if (root.TryGetProperty("game_activity", out var gaEl) && gaEl.ValueKind == JsonValueKind.Object)
        {
            if (gaEl.TryGetProperty("name", out var gNameEl) && gNameEl.ValueKind == JsonValueKind.String)
            {
                AssociatedGameName = gNameEl.GetString();
            }
            if (gaEl.TryGetProperty("icon", out var gIconEl) && gIconEl.ValueKind == JsonValueKind.String)
            {
                AssociatedGameIconUrl = gIconEl.GetString();
            }
        }

        Tag = fallbackTag ?? Tag;
        BadgeUrl = fallbackBadgeUrl ?? BadgeUrl;
        EstablishedText = ComputeEstablishedText(GuildId);
    }
}
