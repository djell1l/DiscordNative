using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using DiscordNative.Protocol.Gateway;
using DiscordNative.Protocol.Models;
using DiscordNative.Protocol.Rest;
using DiscordNative.Protocol.Rpc;
using DiscordNative.Protocol.Voice;
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
    private DiscordVoiceClient? _voiceClient;
    private DiscordLocalRpcServer? _localRpcServer;
    private DiscordActivity? _localIpcActivity;
    private DiscordActivity? _customRpcActivity;
    private string _currentStatus = "online";
    private string? _voiceSessionId;
    private string? _pendingVoiceGuildId;
    private string? _pendingVoiceChannelId;
    private bool _voiceMuted;
    private bool _voiceDeafened;
    private bool _voiceVideo;
    private bool _voiceStream;
    private bool _voiceKrispEnabled = true;
    private bool _voiceSystemAudioSharing;
    private List<string> _orderedGuildIds = new();

    public bool IsKrispEnabled
    {
        get => _voiceKrispEnabled;
        set
        {
            _voiceKrispEnabled = value;
            if (_voiceClient is not null)
            {
                _voiceClient.IsKrispEnabled = value;
            }
        }
    }

    public bool IsSystemAudioSharingEnabled
    {
        get => _voiceSystemAudioSharing;
        set
        {
            _voiceSystemAudioSharing = value;
            if (_voiceClient is not null)
            {
                _voiceClient.IsSystemAudioSharingEnabled = value;
            }
        }
    }

    // Suivi en temps réel des membres présents dans chaque salon vocal (channelId -> liste des utilisateurs)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DiscordUser>> _voiceChannelMembers = new();

    // Cache des membres par serveur (guildId -> (userId -> DiscordUser))
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DiscordUser>> _guildMembers = new();

    // Cache global des utilisateurs par userId
    private readonly ConcurrentDictionary<string, DiscordUser> _userCache = new();

    // Cache temps réel de la présence (statut + Rich Presence / Activités) par userId
    private readonly ConcurrentDictionary<string, (string Status, List<DiscordActivity> Activities)> _userPresences = new();

    public DiscordRestClient Rest { get; }
    public DiscordUser? CurrentUser { get; set; }
    public ObservableCollection<DiscordGuild> Guilds { get; } = new();
    public ObservableCollection<DiscordChannel> PrivateChannels { get; } = new();
    public ObservableCollection<DiscordRelationship> Friends { get; } = new();

    public event Action<DiscordMessage>? MessageReceived;
    public event Action<DiscordMessage>? MessageUpdated;
    public event Action<string, string>? MessageDeleted; // channelId, messageId
    public event Action<string, string, DiscordEmoji>? ReactionAdded; // channelId, messageId, emoji
    public event Action<string>? VoiceParticipantsChanged; // channelId
    public event Action<string, bool>? VoiceSpeakingChanged; // userId, isSpeaking
    public event Action<string>? PresenceUpdated; // userId
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

        _localRpcServer = new DiscordLocalRpcServer();
        _localRpcServer.LocalActivityChanged += OnLocalIpcActivityChanged;
        _localRpcServer.Start();
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
                var guildIdStr = evt.Data.TryGetProperty("id", out var gIdEl) ? gIdEl.GetString() : null;

                // 1. Cacher d'abord tous les membres du serveur
                if (evt.Data.TryGetProperty("members", out var gMembersEl) && gMembersEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mEl in gMembersEl.EnumerateArray())
                    {
                        CacheGuildMember(guildIdStr, mEl);
                    }
                }

                if (evt.Data.TryGetProperty("presences", out var gPresEl) && gPresEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pEl in gPresEl.EnumerateArray())
                    {
                        HandlePresenceUpdate(pEl);
                    }
                }
                if (evt.Data.TryGetProperty("voice_states", out var gVsArray) && gVsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var vs in gVsArray.EnumerateArray())
                    {
                        HandleVoiceStateUpdate(vs, guildIdStr);
                    }
                }
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
            case "GUILD_MEMBERS_CHUNK":
                if (evt.Data.TryGetProperty("guild_id", out var chunkGidEl) && evt.Data.TryGetProperty("members", out var chunkMemArr) && chunkMemArr.ValueKind == JsonValueKind.Array)
                {
                    var chunkGid = chunkGidEl.GetString();
                    foreach (var mEl in chunkMemArr.EnumerateArray())
                    {
                        CacheGuildMember(chunkGid, mEl);
                    }
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
            case "PRESENCE_UPDATE":
                HandlePresenceUpdate(evt.Data);
                break;
            case "SESSIONS_REPLACE":
                HandleSessionsReplace(evt.Data);
                break;
            case "VOICE_STATE_UPDATE":
                HandleVoiceStateUpdate(evt.Data);
                break;
            case "VOICE_SERVER_UPDATE":
                HandleVoiceServerUpdate(evt.Data);
                break;
        }
    }

    public void CacheGuildMember(string? guildId, JsonElement memberEl)
    {
        try
        {
            if (memberEl.ValueKind != JsonValueKind.Object) return;
            if (!memberEl.TryGetProperty("user", out var uEl) || uEl.ValueKind != JsonValueKind.Object) return;

            var user = uEl.Deserialize<DiscordUser>(JsonOptions);
            if (user == null || string.IsNullOrEmpty(user.Id)) return;

            if (memberEl.TryGetProperty("nick", out var nickEl) && nickEl.ValueKind == JsonValueKind.String)
            {
                user.ServerNick = nickEl.GetString();
            }

            if (memberEl.TryGetProperty("avatar", out var avEl) && avEl.ValueKind == JsonValueKind.String)
            {
                user.ServerAvatar = avEl.GetString();
                user.GuildIdForAvatar = guildId;
            }

            _userCache[user.Id] = user;

            if (!string.IsNullOrEmpty(guildId))
            {
                var dict = _guildMembers.GetOrAdd(guildId, _ => new ConcurrentDictionary<string, DiscordUser>());
                dict[user.Id] = user;
            }
        }
        catch { }
    }

    public Task SubscribeGuildAsync(string guildId, CancellationToken ct = default)
    {
        if (_gateway is not null && !string.IsNullOrEmpty(guildId))
        {
            return _gateway.SendGuildSubscriptionsAsync(guildId, ct);
        }
        return Task.CompletedTask;
    }

    private void HandleVoiceStateUpdate(JsonElement data, string? fallbackGuildId = null)
    {
        try
        {
            var userId = data.TryGetProperty("user_id", out var uidEl) ? uidEl.GetString() : null;
            var channelId = data.TryGetProperty("channel_id", out var cidEl) && cidEl.ValueKind == JsonValueKind.String ? cidEl.GetString() : null;
            var sessionId = data.TryGetProperty("session_id", out var sidEl) ? sidEl.GetString() : null;
            var guildId = data.TryGetProperty("guild_id", out var gidEl) && gidEl.ValueKind == JsonValueKind.String ? gidEl.GetString() : fallbackGuildId;

            if (string.IsNullOrWhiteSpace(userId)) return;

            if (userId == CurrentUser?.Id && !string.IsNullOrWhiteSpace(sessionId))
            {
                _voiceSessionId = sessionId;
            }

            DiscordUser? userObj = null;

            // 1. Si le member est fourni directement dans le voice state
            if (data.TryGetProperty("member", out var memEl) && memEl.ValueKind == JsonValueKind.Object)
            {
                CacheGuildMember(guildId, memEl);
                if (!string.IsNullOrEmpty(guildId) && _guildMembers.TryGetValue(guildId, out var gDict) && gDict.TryGetValue(userId, out var mUser))
                {
                    userObj = mUser;
                }
                else if (_userCache.TryGetValue(userId, out var cUser))
                {
                    userObj = cUser;
                }
            }

            // 2. Si non trouvé, vérifier dans le cache du serveur
            if (userObj == null && !string.IsNullOrEmpty(guildId) && _guildMembers.TryGetValue(guildId, out var gmDict) && gmDict.TryGetValue(userId, out var gUser))
            {
                userObj = gUser;
            }

            // 3. Cache global
            if (userObj == null && _userCache.TryGetValue(userId, out var cachedUser))
            {
                userObj = cachedUser;
            }

            // 4. CurrentUser
            if (userObj == null && userId == CurrentUser?.Id)
            {
                userObj = CurrentUser;
            }

            // 5. Friends
            if (userObj == null)
            {
                userObj = Friends.Select(f => f.User).FirstOrDefault(u => u.Id == userId);
            }

            bool needsResolution = false;
            // 6. Fallback si inconnu
            if (userObj == null)
            {
                userObj = new DiscordUser { Id = userId!, Username = $"Utilisateur {userId![..Math.Min(4, userId.Length)]}" };
                needsResolution = true;
            }
            else if (userObj.Username.StartsWith("Utilisateur ") && userObj.Username.Length <= 16 && string.IsNullOrEmpty(userObj.GlobalName) && string.IsNullOrEmpty(userObj.ServerNick))
            {
                needsResolution = true;
            }

            bool selfStream = data.TryGetProperty("self_stream", out var ssEl) && ssEl.ValueKind == JsonValueKind.True;
            bool selfVideo = data.TryGetProperty("self_video", out var svEl) && svEl.ValueKind == JsonValueKind.True;
            bool selfMute = (data.TryGetProperty("self_mute", out var smEl) && smEl.ValueKind == JsonValueKind.True) ||
                            (data.TryGetProperty("mute", out var mEl) && mEl.ValueKind == JsonValueKind.True);
            bool selfDeaf = (data.TryGetProperty("self_deaf", out var sdEl) && sdEl.ValueKind == JsonValueKind.True) ||
                            (data.TryGetProperty("deaf", out var dEl) && dEl.ValueKind == JsonValueKind.True);

            App.RunOnUiThread(() =>
            {
                userObj.IsStreaming = selfStream;
                userObj.IsVideoEnabled = selfVideo;
                userObj.IsVoiceMuted = selfMute;
                userObj.IsVoiceDeafened = selfDeaf;
            });

            // Retirer l'utilisateur de TOUS les autres salons vocaux que celui rejoint (ou de tous s'il a quitté)
            foreach (var kvp in _voiceChannelMembers)
            {
                if (kvp.Key != channelId)
                {
                    if (kvp.Value.TryRemove(userId!, out _))
                    {
                        VoiceParticipantsChanged?.Invoke(kvp.Key);
                    }
                }
            }

            // L'ajouter au nouveau salon vocal s'il a rejoint
            if (!string.IsNullOrWhiteSpace(channelId))
            {
                var dict = _voiceChannelMembers.GetOrAdd(channelId!, _ => new ConcurrentDictionary<string, DiscordUser>());
                dict[userId!] = userObj;
                VoiceParticipantsChanged?.Invoke(channelId!);

                if (needsResolution)
                {
                    _ = ResolveVoiceParticipantProfileAsync(guildId, channelId!, userId!, userObj);
                }
            }
        }
        catch { }
    }

    private async Task ResolveVoiceParticipantProfileAsync(string? guildId, string channelId, string userId, DiscordUser placeholderUser)
    {
        try
        {
            DiscordUser? resolved = null;
            if (!string.IsNullOrEmpty(guildId))
            {
                resolved = await Rest.GetGuildMemberAsync(guildId, userId);
            }
            if (resolved == null)
            {
                resolved = await Rest.GetUserAsync(userId);
            }

            if (resolved != null && !string.IsNullOrEmpty(resolved.Username))
            {
                App.RunOnUiThread(() =>
                {
                    placeholderUser.Username = resolved.Username;
                    placeholderUser.GlobalName = resolved.GlobalName;
                    placeholderUser.Discriminator = resolved.Discriminator;
                    placeholderUser.Avatar = resolved.Avatar;
                    placeholderUser.ServerNick = resolved.ServerNick;
                    placeholderUser.ServerAvatar = resolved.ServerAvatar;
                    placeholderUser.GuildIdForAvatar = resolved.GuildIdForAvatar ?? guildId;
                    placeholderUser.NotifyProfileChanged();
                });

                _userCache[userId] = placeholderUser;
                if (!string.IsNullOrEmpty(guildId))
                {
                    var gm = _guildMembers.GetOrAdd(guildId, _ => new ConcurrentDictionary<string, DiscordUser>());
                    gm[userId] = placeholderUser;
                }

                VoiceParticipantsChanged?.Invoke(channelId);
            }
        }
        catch { }
    }

    private void HandleVoiceServerUpdate(JsonElement data)
    {
        try
        {
            var token = data.TryGetProperty("token", out var tokEl) ? tokEl.GetString() : null;
            var guildId = data.TryGetProperty("guild_id", out var gidEl) && gidEl.ValueKind == JsonValueKind.String
                ? gidEl.GetString()
                : _pendingVoiceGuildId;
            var endpoint = data.TryGetProperty("endpoint", out var epEl) && epEl.ValueKind == JsonValueKind.String
                ? epEl.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(guildId))
                return;

            var userId = CurrentUser?.Id;
            var sessionId = _voiceSessionId;
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_voiceClient is not null)
                    {
                        await _voiceClient.DisposeAsync();
                        _voiceClient = null;
                    }

                    var vc = new DiscordVoiceClient(guildId!, userId!, sessionId!, token!, endpoint!, _voiceKrispEnabled)
                    {
                        IsMuted = _voiceMuted,
                        IsDeafened = _voiceDeafened,
                        IsSystemAudioSharingEnabled = _voiceSystemAudioSharing
                    };
                    vc.UserSpeakingChanged += (spkUserId, isSpk) =>
                    {
                        App.RunOnUiThread(() =>
                        {
                            if (CurrentUser?.Id == spkUserId)
                            {
                                CurrentUser.IsSpeaking = isSpk;
                            }
                            foreach (var chDict in _voiceChannelMembers.Values)
                            {
                                if (chDict.TryGetValue(spkUserId, out var u))
                                {
                                    u.IsSpeaking = isSpk;
                                }
                            }
                            VoiceSpeakingChanged?.Invoke(spkUserId, isSpk);
                        });
                    };
                    _voiceClient = vc;
                    await vc.ConnectAsync();
                }
                catch { }
            });
        }
        catch { }
    }

    public List<DiscordUser> GetVoiceChannelParticipants(string channelId)
    {
        if (_voiceChannelMembers.TryGetValue(channelId, out var dict))
        {
            return dict.Values.ToList();
        }
        return new List<DiscordUser>();
    }

    private void HandleReady(JsonElement data)
    {
        if (data.TryGetProperty("session_id", out var sidProp) && sidProp.ValueKind == JsonValueKind.String)
        {
            _voiceSessionId = sidProp.GetString();
        }

        if (data.TryGetProperty("user_settings", out var userSettingsEl))
        {
            var readyOrder = DiscordRestClient.ExtractOrderedGuildIds(userSettingsEl);
            if (readyOrder.Count > 0)
            {
                _orderedGuildIds = readyOrder;
            }
        }

        if (data.TryGetProperty("presences", out var readyPresEl) && readyPresEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var pEl in readyPresEl.EnumerateArray())
            {
                HandlePresenceUpdate(pEl);
            }
        }

        if (data.TryGetProperty("sessions", out var sessionsEl) && sessionsEl.ValueKind == JsonValueKind.Array)
        {
            HandleSessionsReplace(sessionsEl);
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
                if (gEl.TryGetProperty("members", out var readyMembArray) && readyMembArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mEl in readyMembArray.EnumerateArray())
                    {
                        CacheGuildMember(g.Id, mEl);
                    }
                }
                if (gEl.TryGetProperty("presences", out var gpArray) && gpArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pEl in gpArray.EnumerateArray())
                    {
                        HandlePresenceUpdate(pEl);
                    }
                }
                if (gEl.TryGetProperty("voice_states", out var vsArray) && vsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var vs in vsArray.EnumerateArray())
                    {
                        HandleVoiceStateUpdate(vs, g.Id);
                    }
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

    private void HandlePresenceUpdate(JsonElement data)
    {
        try
        {
            string? userId = null;
            if (data.TryGetProperty("user", out var uEl) && uEl.ValueKind == JsonValueKind.Object &&
                uEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            {
                userId = idEl.GetString();
            }
            if (string.IsNullOrWhiteSpace(userId)) return;

            string status = data.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.String
                ? stEl.GetString() ?? "offline"
                : "offline";

            var activities = new List<DiscordActivity>();
            if (data.TryGetProperty("activities", out var actArr) && actArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var aEl in actArr.EnumerateArray())
                {
                    var act = aEl.Deserialize<DiscordActivity>(JsonOptions);
                    if (act is not null && !string.IsNullOrWhiteSpace(act.Name))
                    {
                        activities.Add(act);
                    }
                }
            }

            UpdateUserPresenceCache(userId!, status, activities);
        }
        catch { }
    }

    private void HandleSessionsReplace(JsonElement data)
    {
        try
        {
            if (CurrentUser is null || data.ValueKind != JsonValueKind.Array) return;

            var mergedActivities = new List<DiscordActivity>();
            string status = _currentStatus;

            foreach (var session in data.EnumerateArray())
            {
                if (session.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.String)
                {
                    var s = stEl.GetString();
                    if (!string.IsNullOrWhiteSpace(s) && s != "offline") status = s!;
                }
                if (session.TryGetProperty("activities", out var actArr) && actArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var aEl in actArr.EnumerateArray())
                    {
                        var act = aEl.Deserialize<DiscordActivity>(JsonOptions);
                        if (act is not null && !string.IsNullOrWhiteSpace(act.Name) && mergedActivities.All(x => x.Name != act.Name))
                        {
                            mergedActivities.Add(act);
                        }
                    }
                }
            }

            if (_localIpcActivity is not null && mergedActivities.All(x => x.Name != _localIpcActivity.Name))
            {
                mergedActivities.Insert(0, _localIpcActivity);
            }
            if (_customRpcActivity is not null && mergedActivities.All(x => x.Name != _customRpcActivity.Name))
            {
                mergedActivities.Insert(0, _customRpcActivity);
            }

            UpdateUserPresenceCache(CurrentUser.Id, status, mergedActivities);
        }
        catch { }
    }

    private void UpdateUserPresenceCache(string userId, string status, List<DiscordActivity> activities)
    {
        _userPresences[userId] = (status, activities);

        App.RunOnUiThread(() =>
        {
            if (CurrentUser?.Id == userId)
            {
                CurrentUser.Status = status;
                CurrentUser.Activities = activities;
                CurrentUser.NotifyPresenceChanged();
            }

            foreach (var rel in Friends.Where(f => f.User.Id == userId))
            {
                rel.User.Status = status;
                rel.User.Activities = activities;
                rel.User.NotifyPresenceChanged();
                rel.NotifyPresenceChanged();
            }

            foreach (var dm in PrivateChannels)
            {
                if (dm.Recipients is null) continue;
                foreach (var r in dm.Recipients.Where(u => u.Id == userId))
                {
                    r.Status = status;
                    r.Activities = activities;
                    r.NotifyPresenceChanged();
                }
            }

            foreach (var chDict in _voiceChannelMembers.Values)
            {
                if (chDict.TryGetValue(userId, out var vu))
                {
                    vu.Status = status;
                    vu.Activities = activities;
                    vu.NotifyPresenceChanged();
                }
            }

            PresenceUpdated?.Invoke(userId);
        });
    }

    public void ApplyCachedPresenceToUser(DiscordUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) return;
        if (_userPresences.TryGetValue(user.Id, out var p))
        {
            user.Status = p.Status;
            user.Activities = p.Activities;
            user.NotifyPresenceChanged();
        }
    }

    private void OnLocalIpcActivityChanged(DiscordActivity? activity)
    {
        _localIpcActivity = activity;
        _ = BroadcastCurrentPresenceAsync();
    }

    public async Task SetCustomRichPresenceAsync(DiscordActivity? customActivity, string? status = null)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            _currentStatus = status!;
        }
        _customRpcActivity = customActivity;
        await BroadcastCurrentPresenceAsync();
    }

    public async Task BroadcastCurrentPresenceAsync(string? statusOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(statusOverride))
        {
            _currentStatus = statusOverride!;
        }

        var activeList = new List<DiscordActivity>();
        if (_customRpcActivity is not null) activeList.Add(_customRpcActivity);
        if (_localIpcActivity is not null) activeList.Add(_localIpcActivity);

        if (CurrentUser is not null)
        {
            UpdateUserPresenceCache(CurrentUser.Id, _currentStatus, activeList);
        }

        if (_gateway is not null)
        {
            var wireActivities = activeList.Select(a => (object)new
            {
                name = a.Name,
                type = a.Type,
                application_id = a.ApplicationId,
                details = a.Details,
                state = a.State,
                timestamps = a.Timestamps is not null ? new { start = a.Timestamps.Start, end = a.Timestamps.End } : null,
                assets = a.Assets is not null ? new
                {
                    large_image = a.Assets.LargeImage,
                    large_text = a.Assets.LargeText,
                    small_image = a.Assets.SmallImage,
                    small_text = a.Assets.SmallText
                } : null
            }).ToList();

            await _gateway.SendPresenceUpdateAsync(_currentStatus, wireActivities);
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

    public async Task JoinVoiceChannelAsync(string guildId, string? channelId, bool mute = false, bool deaf = false, bool selfVideo = false, bool selfStream = false)
    {
        bool wasStreaming = _voiceStream;
        _voiceMuted = mute;
        _voiceDeafened = deaf;
        _voiceVideo = selfVideo;
        _voiceStream = selfStream;

        if (_voiceClient is not null)
        {
            _voiceClient.IsMuted = mute;
            _voiceClient.IsDeafened = deaf;
        }

        if (CurrentUser is not null)
        {
            App.RunOnUiThread(() =>
            {
                CurrentUser.IsVoiceMuted = mute;
                CurrentUser.IsVoiceDeafened = deaf;
                CurrentUser.IsVideoEnabled = selfVideo;
                CurrentUser.IsStreaming = selfStream;
            });
        }

        if (channelId is null && _voiceClient is not null)
        {
            await _voiceClient.DisposeAsync();
            _voiceClient = null;
        }
        else if (channelId is not null)
        {
            _pendingVoiceGuildId = guildId;
            _pendingVoiceChannelId = channelId;
        }

        if (_gateway is not null)
        {
            await _gateway.SendVoiceStateUpdateAsync(guildId, channelId, mute, deaf, selfVideo, selfStream);

            if (channelId is not null && CurrentUser is not null)
            {
                if (selfStream && !wasStreaming)
                {
                    await _gateway.SendStreamCreateAsync(guildId, channelId);
                }
                else if (!selfStream && wasStreaming)
                {
                    await _gateway.SendStreamDeleteAsync($"guild:{guildId}:{channelId}:{CurrentUser.Id}");
                }
            }
        }
    }

    public async Task LeaveVoiceChannelAsync()
    {
        var guildId = _pendingVoiceGuildId;
        var channelId = _pendingVoiceChannelId;
        bool wasStreaming = _voiceStream;
        _pendingVoiceGuildId = null;
        _pendingVoiceChannelId = null;
        _voiceVideo = false;
        _voiceStream = false;

        if (CurrentUser is not null)
        {
            App.RunOnUiThread(() =>
            {
                CurrentUser.IsSpeaking = false;
                CurrentUser.IsStreaming = false;
                CurrentUser.IsVideoEnabled = false;
                CurrentUser.IsVoiceMuted = false;
                CurrentUser.IsVoiceDeafened = false;
            });
        }

        if (_voiceClient is not null)
        {
            await _voiceClient.DisposeAsync();
            _voiceClient = null;
        }

        if (_gateway is not null && !string.IsNullOrEmpty(guildId))
        {
            if (wasStreaming && !string.IsNullOrEmpty(channelId) && CurrentUser is not null)
            {
                try { await _gateway.SendStreamDeleteAsync($"guild:{guildId}:{channelId}:{CurrentUser.Id}"); } catch { }
            }
            await _gateway.SendVoiceStateUpdateAsync(guildId, null, _voiceMuted, _voiceDeafened, false, false);
        }
    }

    public async Task StartScreenShareAsync(bool shareSystemAudio = true)
    {
        IsSystemAudioSharingEnabled = shareSystemAudio;
        if (_pendingVoiceGuildId is not null && _pendingVoiceChannelId is not null)
        {
            await JoinVoiceChannelAsync(_pendingVoiceGuildId, _pendingVoiceChannelId, _voiceMuted, _voiceDeafened, _voiceVideo, selfStream: true);
        }
    }

    public async Task StopScreenShareAsync()
    {
        IsSystemAudioSharingEnabled = false;
        if (_pendingVoiceGuildId is not null && _pendingVoiceChannelId is not null)
        {
            await JoinVoiceChannelAsync(_pendingVoiceGuildId, _pendingVoiceChannelId, _voiceMuted, _voiceDeafened, _voiceVideo, selfStream: false);
        }
    }

    public async Task ToggleVideoAsync(bool enabled)
    {
        if (_pendingVoiceGuildId is not null && _pendingVoiceChannelId is not null)
        {
            await JoinVoiceChannelAsync(_pendingVoiceGuildId, _pendingVoiceChannelId, _voiceMuted, _voiceDeafened, selfVideo: enabled, _voiceStream);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_localRpcServer is not null) await _localRpcServer.DisposeAsync();
        if (_voiceClient is not null) await _voiceClient.DisposeAsync();
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


