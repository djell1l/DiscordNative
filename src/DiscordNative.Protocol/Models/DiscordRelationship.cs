using System.Text.Json.Serialization;

namespace DiscordNative.Protocol.Models;

public enum DiscordRelationshipType
{
    None = 0,
    Friend = 1,
    Blocked = 2,
    PendingIncoming = 3,
    PendingOutgoing = 4,
}

public sealed class DiscordRelationship
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public DiscordRelationshipType Type { get; set; }

    [JsonPropertyName("user")]
    public DiscordUser User { get; set; } = new();

    public bool IsFriend => Type == DiscordRelationshipType.Friend;
    public bool IsPending => Type is DiscordRelationshipType.PendingIncoming or DiscordRelationshipType.PendingOutgoing;
    public bool IsPendingIncoming => Type == DiscordRelationshipType.PendingIncoming;
    public bool IsBlocked => Type == DiscordRelationshipType.Blocked;

    public string SubtitleText => Type switch
    {
        DiscordRelationshipType.PendingIncoming => "Demande d'ami reçue",
        DiscordRelationshipType.PendingOutgoing => "Demande d'ami envoyée",
        DiscordRelationshipType.Blocked => "Utilisateur bloqué",
        _ => User.UserTag
    };
}

