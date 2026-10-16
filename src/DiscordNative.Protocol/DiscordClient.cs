using System.Collections.ObjectModel;
using System.Text.Json;
using DiscordNative.Protocol.Gateway;
using DiscordNative.Protocol.Models;
using DiscordNative.Protocol.Rest;
using Microsoft.Extensions.Logging;

namespace DiscordNative.Protocol;

/// <summary>
/// Point d'entrée unique de la librairie : combine REST + Gateway et maintient
/// un état en mémoire (cache) des guildes/salons/messages, avec des évènements
/// pour que l'UI (WinUI) puisse réagir en temps réel.
/// </summary>
public sealed class DiscordClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _token;
    private readonly ILoggerFactory? _loggerFactory;
    private DiscordGatewayClient? _gateway;
    private List<string> _orderedGuildIds = new();

    public DiscordRestClient Rest { get; }
    public DiscordUser? CurrentUser { get; set; }
    public ObservableCollection<DiscordGuild> Guilds { get; } = new();
    public ObservableCollection<DiscordChannel> PrivateChannels { get; } = new();
    public ObservableCollection<DiscordRelationship> Friends { get; } = new();

    public event Action<DiscordMessage>? MessageReceived;
    public event Action<DiscordMessage>? MessageUpdated;
    public event Action<string, string>? MessageDeleted; // channelId, messageId
    public event Action<string, string, DiscordEmoji>? ReactionAdded; // channelId, messageId, emoji
    public event Action<GatewayConnectionState>? ConnectionStateChanged;

    public DiscordClient(string token, ILoggerFactory? loggerFactory = null)
    {
        _token = token;
        _loggerFactory = loggerFactory;
        Rest = new DiscordRestClient(token);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        CurrentUser = await Rest.GetCurrentUserAsync(ct);

        try
        {
            var privateChannels = await Rest.GetPrivateChannelsAsync(ct);
            App.RunOnUiThread(() =>
            {
                PrivateChannels.Clear();
                foreach (var c in privateChannels) PrivateChannels.Add(c);
            });
        }
        catch { /* pas critique */ }

        try
        {
            var relationships = await Rest.GetRelationshipsAsync(ct);
            var friends = relationships.Where(r => r.Type == DiscordRelationshipType.Friend).ToList();
            App.RunOnUiThread(() =>
            {
                Friends.Clear();
                foreach (var f in friends) Friends.Add(f);
            });
        }
        catch { /* pas critique si ça échoue */ }

        try
        {
            _orderedGuildIds = await Rest.GetOrderedGuildIdsAsync(ct);
            var guilds = await Rest.GetGuildsAsync(ct);
            if (guilds.Count > 0)
            {
                var sorted = SortGuilds(guilds, _orderedGuildIds);
                App.RunOnUiThread(() =>
                {
                    SyncGuildsCollection(sorted);
                });
            }
        }
        catch { /* pas critique */ }

        _gateway = new DiscordGatewayClient(_token, _loggerFactory?.CreateLogger<DiscordGatewayClient>());
        _gateway.EventReceived += OnDispatchEvent;
        _gateway.StateChanged += state => ConnectionStateChanged?.Invoke(state);
        await _gateway.ConnectAsync(ct);
    }

    public async Task RefreshGuildsAsync(CancellationToken ct = default)
    {
        try
        {
            _orderedGuildIds = await Rest.GetOrderedGuildIdsAsync(ct);
            var guilds = await Rest.GetGuildsAsync(ct);
            if (guilds.Count > 0)
            {
                var sorted = SortGuilds(guilds, _orderedGuildIds);
                App.RunOnUiThread(() =>
                {
                    SyncGuildsCollection(sorted);
                });
            }
        }
        catch { }
    }

    private void OnDispatchEvent(DiscordDispatchEvent evt)
    {
        switch (evt.Name)
        {
            case "READY":
                HandleReady(evt.Data);
                break;
            case "USER_SETTINGS_UPDATE":
                if (evt.Data.TryGetProperty("guild_folders", out _) || evt.Data.TryGetProperty("guild_positions", out _))
                {
                    var updatedOrder = DiscordRestClient.ExtractOrderedGuildIds(evt.Data);
                    if (updatedOrder.Count > 0)
                    {
                        _orderedGuildIds = updatedOrder;
                        App.RunOnUiThread(() =>
                        {
                            var sorted = SortGuilds(Guilds.ToList(), _orderedGuildIds);
                            SyncGuildsCollection(sorted);
                        });
                    }
                }
                break;
            case "MESSAGE_CREATE":
                var message = evt.Data.Deserialize<DiscordMessage>(JsonOptions);
                if (message is not null) MessageReceived?.Invoke(message);
                break;
            case "MESSAGE_UPDATE":
                var updatedMsg = evt.Data.Deserialize<DiscordMessage>(JsonOptions);
                if (updatedMsg is not null) MessageUpdated?.Invoke(updatedMsg);
                break;
            case "MESSAGE_DELETE":
                if (evt.Data.TryGetProperty("id", out var idProp) && evt.Data.TryGetProperty("channel_id", out var chProp))
                {
                    MessageDeleted?.Invoke(chProp.GetString() ?? "", idProp.GetString() ?? "");
                }
                break;
            case "MESSAGE_REACTION_ADD":
                if (evt.Data.TryGetProperty("channel_id", out var rCh) &&
                    evt.Data.TryGetProperty("message_id", out var rMsg) &&
                    evt.Data.TryGetProperty("emoji", out var emojiEl))
                {
                    var emoji = emojiEl.Deserialize<DiscordEmoji>(JsonOptions);
                    if (emoji is not null) ReactionAdded?.Invoke(rCh.GetString() ?? "", rMsg.GetString() ?? "", emoji);
                }
                break;
            case "GUILD_CREATE":
                var guild = evt.Data.Deserialize<DiscordGuild>(JsonOptions);
                if (guild is not null && !string.IsNullOrEmpty(guild.Name))
                {
                    App.RunOnUiThread(() =>
                    {
                        var existing = Guilds.FirstOrDefault(g => g.Id == guild.Id);
                        if (existing != null)
                        {
                            var idx = Guilds.IndexOf(existing);
                            Guilds[idx] = guild;
                        }
                        else
                        {
                            var currentList = Guilds.ToList();
                            currentList.Add(guild);
                            var sorted = SortGuilds(currentList, _orderedGuildIds);
                            SyncGuildsCollection(sorted);
                        }
                    });
                }
                break;
            case "GUILD_DELETE":
                if (evt.Data.TryGetProperty("id", out var delIdProp))
                {
                    var delId = delIdProp.GetString();
                    if (!string.IsNullOrEmpty(delId))
                    {
                        App.RunOnUiThread(() =>
                        {
                            var existing = Guilds.FirstOrDefault(g => g.Id == delId);
                            if (existing != null) Guilds.Remove(existing);
                        });
                    }
                }
                break;
        }
    }

    private void HandleReady(JsonElement data)
    {
        if (data.TryGetProperty("user_settings", out var userSettingsEl))
        {
            var readyOrder = DiscordRestClient.ExtractOrderedGuildIds(userSettingsEl);
            if (readyOrder.Count > 0)
            {
                _orderedGuildIds = readyOrder;
            }
        }

        if (data.TryGetProperty("guilds", out var guildsEl) && guildsEl.ValueKind == JsonValueKind.Array)
        {
            var validGuilds = new List<DiscordGuild>();
            foreach (var gEl in guildsEl.EnumerateArray())
            {
                var g = gEl.Deserialize<DiscordGuild>(JsonOptions);
                if (g is null) continue;
                if (gEl.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                {
                    if (string.IsNullOrEmpty(g.Name) && props.TryGetProperty("name", out var n)) g.Name = n.GetString() ?? "";
                    if (g.Icon is null && props.TryGetProperty("icon", out var i) && i.ValueKind == JsonValueKind.String) g.Icon = i.GetString();
                }
                if (!string.IsNullOrEmpty(g.Name))
                {
                    validGuilds.Add(g);
                }
            }

            if (validGuilds.Count > 0)
            {
                var sorted = SortGuilds(validGuilds, _orderedGuildIds);
                App.RunOnUiThread(() =>
                {
                    SyncGuildsCollection(sorted);
                });
            }
        }

        if (data.TryGetProperty("private_channels", out var dmEl))
        {
            var dms = dmEl.Deserialize<List<DiscordChannel>>(JsonOptions) ?? new();
            // Si les destinataires ne sont pas inclus, on garde la liste REST (plus complète).
            if (dms.Count > 0 && dms.All(c => c.Recipients is { Count: > 0 } || c.Type == DiscordChannelType.GroupDm))
            {
                var sorted = dms.OrderByDescending(c => c.LastMessageId?.Length ?? 0).ThenByDescending(c => c.LastMessageId).ToList();
                App.RunOnUiThread(() =>
                {
                    PrivateChannels.Clear();
                    foreach (var c in sorted) PrivateChannels.Add(c);
                });
            }
        }
    }

    private static List<DiscordGuild> SortGuilds(IEnumerable<DiscordGuild> guilds, List<string> orderedIds)
    {
        var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < orderedIds.Count; i++)
        {
            idToIndex[orderedIds[i]] = i;
        }

        return guilds
            .OrderBy(g => idToIndex.TryGetValue(g.Id, out var idx) ? idx : int.MaxValue)
            .ThenBy(g => g.Name)
            .ToList();
    }

    private void SyncGuildsCollection(List<DiscordGuild> sorted)
    {
        for (int targetIdx = 0; targetIdx < sorted.Count; targetIdx++)
        {
            var targetGuild = sorted[targetIdx];
            var existingIdx = -1;
            for (int i = 0; i < Guilds.Count; i++)
            {
                if (Guilds[i].Id == targetGuild.Id)
                {
                    existingIdx = i;
                    break;
                }
            }

            if (existingIdx >= 0)
            {
                Guilds[existingIdx] = targetGuild;
                if (existingIdx != targetIdx)
                {
                    Guilds.Move(existingIdx, targetIdx);
                }
            }
            else
            {
                Guilds.Insert(targetIdx, targetGuild);
            }
        }

        for (int i = Guilds.Count - 1; i >= sorted.Count; i--)
        {
            Guilds.RemoveAt(i);
        }
    }

    public Task JoinVoiceChannelAsync(string guildId, string? channelId, bool mute = false, bool deaf = false)
    {
        if (_gateway is not null) return _gateway.SendVoiceStateUpdateAsync(guildId, channelId, mute, deaf);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Rest.Dispose();
        if (_gateway is not null) await _gateway.DisposeAsync();
    }
}

/// <summary>
/// Petit hook statique pour marshaller les mises à jour de collections vers le thread UI
/// sans coupler la librairie protocole à WinUI. L'application définit ce délégué au démarrage.
/// </summary>
public static class App
{
    public static Action<Action> RunOnUiThread { get; set; } = action => action();
}


