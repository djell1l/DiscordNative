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

    public async Task<DiscordUser?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var user = await _http.GetFromJsonAsync<DiscordUser>($"{ApiBase}/users/@me", ct);
        if (user is not null)
        {
            await EnrichUserProfileAsync(user, ct);
            if (user.ConnectedAccounts.Count == 0)
            {
                var visibleConns = await GetCurrentUserVisibleConnectionsAsync(ct);
                if (visibleConns.Count > 0)
                {
                    user.ConnectedAccounts = visibleConns;
                    Protocol.App.RunOnUiThread(() => user.NotifyProfileEnriched());
                }
            }
        }
        return user;
    }

    public async Task EnrichUserProfileAsync(DiscordUser user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) return;
        try
        {
            using var resp = await _http.GetAsync($"{ApiBase}/users/{user.Id}/profile?with_mutual_guilds=false", ct);
            if (!resp.IsSuccessStatusCode) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("user", out var uEl))
            {
                if (uEl.TryGetProperty("bio", out var bioEl) && bioEl.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(user.Bio))
                    user.Bio = bioEl.GetString();
                if (uEl.TryGetProperty("banner", out var banEl) && banEl.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(user.Banner))
                    user.Banner = banEl.GetString();
                if (uEl.TryGetProperty("accent_color", out var uAcEl) && uAcEl.ValueKind == JsonValueKind.Number && uAcEl.TryGetInt32(out int uAcVal))
                    user.AccentColor = uAcVal;
                if (uEl.TryGetProperty("clan", out var clanEl) && clanEl.ValueKind == JsonValueKind.Object)
                    user.Clan = JsonSerializer.Deserialize<DiscordClan>(clanEl.GetRawText());
                if (uEl.TryGetProperty("primary_guild", out var pgEl) && pgEl.ValueKind == JsonValueKind.Object)
                    user.PrimaryGuild = JsonSerializer.Deserialize<DiscordClan>(pgEl.GetRawText());
            }

            if (root.TryGetProperty("user_profile", out var upEl) && upEl.ValueKind == JsonValueKind.Object)
            {
                if (upEl.TryGetProperty("bio", out var upBio) && upBio.ValueKind == JsonValueKind.String)
                    user.Bio = upBio.GetString();
                if (upEl.TryGetProperty("pronouns", out var pronEl) && pronEl.ValueKind == JsonValueKind.String)
                    user.Pronouns = pronEl.GetString();
                if (upEl.TryGetProperty("banner", out var upBan) && upBan.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(user.Banner))
                    user.Banner = upBan.GetString();
                if (upEl.TryGetProperty("accent_color", out var acEl) && acEl.ValueKind == JsonValueKind.Number && acEl.TryGetInt32(out int acVal))
                    user.AccentColor = acVal;
            }

            if (root.TryGetProperty("badges", out var badgesEl) && badgesEl.ValueKind == JsonValueKind.Array)
            {
                var badges = JsonSerializer.Deserialize<List<DiscordBadge>>(badgesEl.GetRawText());
                if (badges is not null) user.Badges = badges;
            }

            if (root.TryGetProperty("connected_accounts", out var connEl) && connEl.ValueKind == JsonValueKind.Array)
            {
                var conns = JsonSerializer.Deserialize<List<DiscordConnectedAccount>>(connEl.GetRawText());
                if (conns is not null)
                {
                    user.ConnectedAccounts = conns.Where(c => c.Visibility != 0).ToList();
                }
            }

            // Widgets (played_games & favorite_games -> Game Collection)
            if (root.TryGetProperty("widgets", out var widgetsEl) && widgetsEl.ValueKind == JsonValueKind.Array)
            {
                var rawGames = new List<(string gameId, string? comment, bool isFav)>();
                foreach (var w in widgetsEl.EnumerateArray())
                {
                    if (!w.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object) continue;
                    var wType = dataEl.TryGetProperty("type", out var tEl) ? tEl.GetString() : "";
                    bool isFav = string.Equals(wType, "favorite_games", StringComparison.OrdinalIgnoreCase);

                    if (dataEl.TryGetProperty("games", out var gamesEl) && gamesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var g in gamesEl.EnumerateArray())
                        {
                            var gid = g.TryGetProperty("game_id", out var gidEl) ? gidEl.GetString() : null;
                            var comment = g.TryGetProperty("comment", out var cEl) && cEl.ValueKind == JsonValueKind.String ? cEl.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(gid) && rawGames.All(x => x.gameId != gid))
                            {
                                rawGames.Add((gid!, comment, isFav));
                            }
                        }
                    }
                }

                if (rawGames.Count > 0)
                {
                    var query = string.Join("&", rawGames.Select(g => $"application_ids={Uri.EscapeDataString(g.gameId)}"));
                    using var appResp = await _http.GetAsync($"{ApiBase}/applications/public?{query}", ct);
                    var appMap = new Dictionary<string, (string name, string icon)>();
                    if (appResp.IsSuccessStatusCode)
                    {
                        using var appDoc = await JsonDocument.ParseAsync(await appResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                        if (appDoc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var appEl in appDoc.RootElement.EnumerateArray())
                            {
                                var id = appEl.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                                var name = appEl.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                                var icon = appEl.TryGetProperty("icon", out var iEl) ? iEl.GetString() : null;
                                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                                {
                                    appMap[id!] = (name!, icon ?? "");
                                }
                            }
                        }
                    }

                    var gameList = new List<DiscordWidgetGame>();
                    foreach (var rg in rawGames)
                    {
                        if (appMap.TryGetValue(rg.gameId, out var info) && !string.IsNullOrWhiteSpace(info.icon))
                        {
                            gameList.Add(new DiscordWidgetGame
                            {
                                GameId = rg.gameId,
                                Name = info.name,
                                IconUrl = $"https://cdn.discordapp.com/app-icons/{rg.gameId}/{info.icon}.png?size=128",
                                Comment = rg.comment,
                                IsFavorite = rg.isFav
                            });
                        }
                    }
                    user.GameWidgets = gameList;
                }
            }

            user.HasProfileEnriched = true;
            Protocol.App.RunOnUiThread(() => user.NotifyProfileEnriched());
        }
        catch { }
    }

    public async Task<List<DiscordConnectedAccount>> GetCurrentUserVisibleConnectionsAsync(CancellationToken ct = default)
    {
        try
        {
            var conns = await _http.GetFromJsonAsync<List<DiscordConnectedAccount>>($"{ApiBase}/users/@me/connections", ct);
            if (conns is not null)
            {
                return conns.Where(c => c.Visibility == 1).ToList();
            }
        }
        catch { }
        return new();
    }

    public async Task<DiscordUser> ModifyCurrentUserAsync(
        string? globalName = null,
        string? username = null,
        string? currentPassword = null,
        string? newPassword = null,
        string? bio = null,
        string? pronouns = null,
        string? avatarDataUri = null,
        bool removeAvatar = false,
        string? bannerDataUri = null,
        bool removeBanner = false,
        int? accentColor = null,
        CancellationToken ct = default)
    {
        var userBody = new Dictionary<string, object?>();
        if (globalName is not null) userBody["global_name"] = globalName;
        if (!string.IsNullOrWhiteSpace(username)) userBody["username"] = username;
        if (!string.IsNullOrWhiteSpace(currentPassword)) userBody["password"] = currentPassword;
        if (!string.IsNullOrWhiteSpace(newPassword)) userBody["new_password"] = newPassword;
        if (removeAvatar) userBody["avatar"] = null;
        else if (!string.IsNullOrWhiteSpace(avatarDataUri)) userBody["avatar"] = avatarDataUri;
        if (removeBanner) userBody["banner"] = null;
        else if (!string.IsNullOrWhiteSpace(bannerDataUri)) userBody["banner"] = bannerDataUri;
        if (accentColor.HasValue) userBody["accent_color"] = accentColor.Value;

        if (bio is not null) userBody["bio"] = bio;
        if (pronouns is not null) userBody["pronouns"] = pronouns;

        // Mise à jour en parallèle de /users/@me/profile pour bio, pronouns et accent_color (utilisé par Discord v9)
        if (bio is not null || pronouns is not null || accentColor.HasValue)
        {
            try
            {
                var profileBody = new Dictionary<string, object?>();
                if (bio is not null) profileBody["bio"] = bio;
                if (pronouns is not null) profileBody["pronouns"] = pronouns;
                if (accentColor.HasValue) profileBody["accent_color"] = accentColor.Value;
                using var profResp = await _http.PatchAsJsonAsync($"{ApiBase}/users/@me/profile", profileBody, ct);
            }
            catch { }
        }

        var response = await _http.PatchAsJsonAsync($"{ApiBase}/users/@me", userBody, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errJson = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Erreur Discord ({(int)response.StatusCode}) : {errJson}");
        }

        var updated = await response.Content.ReadFromJsonAsync<DiscordUser>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Réponse de mise à jour du profil illisible.");
        if (bio is not null && string.IsNullOrEmpty(updated.Bio)) updated.Bio = bio;
        if (pronouns is not null && string.IsNullOrEmpty(updated.Pronouns)) updated.Pronouns = pronouns;
        await EnrichUserProfileAsync(updated, ct);
        return updated;
    }

    public async Task UpdateCustomStatusAsync(string? customStatusText, string? status = null, CancellationToken ct = default)
    {
        try
        {
            var settingsBody = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(status))
            {
                settingsBody["status"] = status;
            }
            if (customStatusText is not null)
            {
                settingsBody["custom_status"] = string.IsNullOrWhiteSpace(customStatusText)
                    ? null
                    : new { text = customStatusText };
            }
            if (settingsBody.Count > 0)
            {
                using var resp = await _http.PatchAsJsonAsync($"{ApiBase}/users/@me/settings", settingsBody, ct);
            }
        }
        catch { }
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

    public async Task<DiscordMessage> CreatePollMessageAsync(
        string channelId,
        string question,
        List<string> answers,
        int durationHours = 24,
        bool allowMultiselect = false,
        CancellationToken ct = default)
    {
        var pollAnswers = answers
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select((a, idx) => new
            {
                answer_id = idx + 1,
                poll_media = new { text = a.Trim() }
            })
            .ToArray();

        var payload = new
        {
            content = "",
            nonce = Guid.NewGuid().ToString("N"),
            poll = new
            {
                question = new { text = question.Trim() },
                answers = pollAnswers,
                duration = Math.Clamp(durationHours, 1, 768),
                allow_multiselect = allowMultiselect,
                layout_type = 1
            }
        };

        var response = await _http.PostAsJsonAsync($"{ApiBase}/channels/{channelId}/messages", payload, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiscordMessage>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Réponse de création de sondage illisible.");
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


