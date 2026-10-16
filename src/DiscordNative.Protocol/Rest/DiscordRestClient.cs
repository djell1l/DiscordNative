using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordNative.Protocol.Models;

namespace DiscordNative.Protocol.Rest;

/// <summary>
/// Client REST minimal pour les opérations courantes (lister guildes/salons/messages, envoyer un message).
/// Utilise le même en-tête Authorization brut (sans préfixe "Bot ") que le client officiel pour un token utilisateur.
/// </summary>
public sealed class DiscordRestClient : IDisposable
{
    private const string ApiBase = "https://discord.com/api/v9";
    private readonly HttpClient _http;

    public DiscordRestClient(string token)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");
        // Pas de préfixe "Bot" : c'est un token de compte utilisateur.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<DiscordUser?> GetCurrentUserAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<DiscordUser>($"{ApiBase}/users/@me", ct);

    public async Task<DiscordUser> ModifyCurrentUserAsync(string? globalName = null, string? username = null, string? bio = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (globalName is not null) body["global_name"] = globalName;
        if (username is not null) body["username"] = username;
        if (bio is not null) body["bio"] = bio;

        var response = await _http.PatchAsJsonAsync($"{ApiBase}/users/@me", body, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiscordUser>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Réponse de mise à jour du profil illisible.");
    }

    public async Task<List<DiscordGuild>> GetGuildsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<DiscordGuild>>($"{ApiBase}/users/@me/guilds", ct) ?? new();

    /// <summary>
    /// Récupère l'ordre officiel des serveurs et dossiers tel que défini dans les paramètres utilisateur Discord (guild_folders / guild_positions).
    /// </summary>
    public async Task<List<string>> GetOrderedGuildIdsAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{ApiBase}/users/@me/settings", ct);
            if (!resp.IsSuccessStatusCode) return new();

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return ExtractOrderedGuildIds(doc.RootElement);
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// Extrait la liste ordonnée des identifiants de serveurs depuis un payload JSON user_settings (REST ou Gateway).
    /// </summary>
    public static List<string> ExtractOrderedGuildIds(JsonElement userSettings)
    {
        var list = new List<string>();

        // 1. guild_folders : liste ordonnée de dossiers contenant chacun un tableau "guild_ids"
        if (userSettings.TryGetProperty("guild_folders", out var foldersEl) && foldersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var folder in foldersEl.EnumerateArray())
            {
                if (folder.TryGetProperty("guild_ids", out var gIdsEl) && gIdsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var idEl in gIdsEl.EnumerateArray())
                    {
                        var id = idEl.GetString();
                        if (!string.IsNullOrEmpty(id) && !list.Contains(id))
                        {
                            list.Add(id);
                        }
                    }
                }
            }
        }

        // 2. Repli sur guild_positions si guild_folders est absent ou vide
        if (list.Count == 0 && userSettings.TryGetProperty("guild_positions", out var positionsEl) && positionsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var idEl in positionsEl.EnumerateArray())
            {
                var id = idEl.GetString();
                if (!string.IsNullOrEmpty(id) && !list.Contains(id))
                {
                    list.Add(id);
                }
            }
        }

        return list;
    }

    public async Task<List<DiscordChannel>> GetGuildChannelsAsync(string guildId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<DiscordChannel>>($"{ApiBase}/guilds/{guildId}/channels", ct) ?? new();

    public async Task<List<DiscordChannel>> GetPrivateChannelsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<DiscordChannel>>($"{ApiBase}/users/@me/channels", ct) ?? new();

    public async Task<List<DiscordRelationship>> GetRelationshipsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<DiscordRelationship>>($"{ApiBase}/users/@me/relationships", ct) ?? new();

    public async Task<List<DiscordMessage>> GetMessagesAsync(string channelId, int limit = 50, string? beforeId = null, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/channels/{channelId}/messages?limit={limit}";
        if (beforeId is not null) url += $"&before={beforeId}";
        return await _http.GetFromJsonAsync<List<DiscordMessage>>(url, ct) ?? new();
    }

    public async Task<DiscordMessage> SendMessageAsync(string channelId, string content, string? replyToMessageId = null, string? nonce = null, CancellationToken ct = default)
    {
        var payload = new SendMessageRequest
        {
            Content = content,
            Nonce = nonce ?? Guid.NewGuid().ToString("N"),
            MessageReference = string.IsNullOrWhiteSpace(replyToMessageId)
                ? null
                : new MessageReferencePayload { ChannelId = channelId, MessageId = replyToMessageId }
        };
        var response = await _http.PostAsJsonAsync($"{ApiBase}/channels/{channelId}/messages", payload, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiscordMessage>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Réponse d'envoi de message illisible.");
    }

    public async Task AddReactionAsync(string channelId, string messageId, string emoji, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(emoji);
        var response = await _http.PutAsync($"{ApiBase}/channels/{channelId}/messages/{messageId}/reactions/{encoded}/@me", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveReactionAsync(string channelId, string messageId, string emoji, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(emoji);
        var response = await _http.DeleteAsync($"{ApiBase}/channels/{channelId}/messages/{messageId}/reactions/{encoded}/@me", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<DiscordMessage> EditMessageAsync(string channelId, string messageId, string content, CancellationToken ct = default)
    {
        var payload = new { content };
        var response = await _http.PatchAsJsonAsync($"{ApiBase}/channels/{channelId}/messages/{messageId}", payload, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiscordMessage>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Réponse d'édition illisible.");
    }

    public async Task<DiscordMessage> SendFileAsync(string channelId, string filePath, string? content = null, bool anonymizeFilename = false, CancellationToken ct = default)
    {
        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath, ct);
        var ext = System.IO.Path.GetExtension(filePath);
        var fileName = anonymizeFilename
            ? $"upload_{Guid.NewGuid().ToString("N")[..8]}{ext}"
            : System.IO.Path.GetFileName(filePath);
        return await SendBytesAsync(channelId, fileBytes, fileName, content, ct);
    }

    public async Task<DiscordMessage> SendBytesAsync(string channelId, byte[] fileBytes, string fileName, string? content = null, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        if (!string.IsNullOrEmpty(content))
        {
            form.Add(new StringContent(content), "content");
        }
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", fileName);

        var response = await _http.PostAsync($"{ApiBase}/channels/{channelId}/messages", form, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiscordMessage>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Réponse d'envoi de fichier illisible.");
    }

    public async Task DeleteMessageAsync(string channelId, string messageId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"{ApiBase}/channels/{channelId}/messages/{messageId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<DiscordMessage>> GetPinnedMessagesAsync(string channelId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<DiscordMessage>>($"{ApiBase}/channels/{channelId}/pins", ct) ?? new();

    public async Task PinMessageAsync(string channelId, string messageId, CancellationToken ct = default)
    {
        var response = await _http.PutAsync($"{ApiBase}/channels/{channelId}/pins/{messageId}", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UnpinMessageAsync(string channelId, string messageId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"{ApiBase}/channels/{channelId}/pins/{messageId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<DiscordMessage>> GetMentionsAsync(int limit = 25, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<DiscordMessage>>($"{ApiBase}/users/@me/mentions?limit={limit}&roles=true&everyone=true", ct) ?? new();

    public async Task<DiscordChannel?> CreateDmAsync(string recipientUserId, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"{ApiBase}/users/@me/channels", new { recipient_id = recipientUserId }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiscordChannel>(cancellationToken: ct);
    }

    public async Task SendFriendRequestAsync(string username, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"{ApiBase}/users/@me/relationships", new { username }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PutRelationshipAsync(string userId, int type = 1, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"{ApiBase}/users/@me/relationships/{userId}", new { type }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveRelationshipAsync(string userId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"{ApiBase}/users/@me/relationships/{userId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task JoinInviteAsync(string inviteCodeOrUrl, CancellationToken ct = default)
    {
        var code = ExtractInviteCode(inviteCodeOrUrl);
        var response = await _http.PostAsJsonAsync($"{ApiBase}/invites/{code}", new { }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> CreateChannelInviteAsync(string channelId, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"{ApiBase}/channels/{channelId}/invites", new { max_age = 86400, max_uses = 0 }, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        return string.IsNullOrEmpty(code) ? string.Empty : $"https://discord.gg/{code}";
    }

    public async Task LeaveGuildAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"{ApiBase}/users/@me/guilds/{guildId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task TriggerTypingAsync(string channelId, CancellationToken ct = default) =>
        await _http.PostAsync($"{ApiBase}/channels/{channelId}/typing", content: null, ct);

    public void Dispose() => _http.Dispose();

    private static string ExtractInviteCode(string raw)
    {
        var trimmed = raw.Trim().TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }

    private sealed class SendMessageRequest
    {
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
        [JsonPropertyName("nonce")] public string Nonce { get; set; } = string.Empty;
        [JsonPropertyName("tts")] public bool Tts { get; set; } = false;
        [JsonPropertyName("message_reference")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MessageReferencePayload? MessageReference { get; set; }
    }

    private sealed class MessageReferencePayload
    {
        [JsonPropertyName("channel_id")] public string ChannelId { get; set; } = string.Empty;
        [JsonPropertyName("message_id")] public string MessageId { get; set; } = string.Empty;
    }
}


