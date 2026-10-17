using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DiscordNative.Protocol.Gateway;

/// <summary>
/// Client WebSocket bas niveau pour le Gateway Discord (v9, JSON).
/// Gère : connexion, HELLO, IDENTIFY, heartbeat périodique, RESUME sur coupure,
/// et redistribue les évènements DISPATCH (READY, MESSAGE_CREATE, ...).
/// </summary>
public sealed class DiscordGatewayClient : IAsyncDisposable
{
    private const string GatewayUrl = "wss://gateway.discord.gg/?v=9&encoding=json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<DiscordGatewayClient> _logger;
    private readonly string _token;

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _receiveLoopTask;
    private Task? _heartbeatLoopTask;

    private int? _lastSequence;
    private string? _sessionId;
    private bool _isReady;

    public event Action<DiscordDispatchEvent>? EventReceived;
    public event Action<GatewayConnectionState>? StateChanged;

    public DiscordGatewayClient(string token, ILogger<DiscordGatewayClient>? logger = null)
    {
        _token = token;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DiscordGatewayClient>.Instance;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await ConnectSocketAsync(resume: false, _lifetimeCts.Token);
    }

    private async Task ConnectSocketAsync(bool resume, CancellationToken ct)
    {
        StateChanged?.Invoke(GatewayConnectionState.Connecting);
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(GatewayUrl), ct);
        _logger.LogInformation("Gateway connecté (resume={Resume})", resume);

        // Le premier message reçu doit être HELLO (opcode 10).
        var hello = await ReceiveOneAsync(ct);
        if (hello is null || hello.Op != (int)GatewayOpCode.Hello || hello.D is null)
        {
            throw new InvalidOperationException("HELLO attendu à la connexion, reçu autre chose.");
        }

        var helloData = hello.D.Value.Deserialize<HelloPayload>(JsonOptions)
                         ?? throw new InvalidOperationException("HELLO illisible.");

        _heartbeatLoopTask = Task.Run(() => HeartbeatLoopAsync(helloData.HeartbeatInterval, ct), ct);

        if (resume && _sessionId is not null && _lastSequence is not null)
        {
            await SendAsync((int)GatewayOpCode.Resume, new ResumePayload
            {
                Token = _token,
                SessionId = _sessionId,
                Seq = _lastSequence.Value,
            }, ct);
        }
        else
        {
            await SendAsync((int)GatewayOpCode.Identify, new IdentifyPayload { Token = _token }, ct);
        }

        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(ct), ct);
    }

    private async Task HeartbeatLoopAsync(int intervalMs, CancellationToken ct)
    {
        // Jitter initial recommandé par la doc Discord pour éviter les thundering herds.
        await Task.Delay((int)(intervalMs * Random.Shared.NextDouble()), ct);
        while (!ct.IsCancellationRequested && _socket?.State == WebSocketState.Open)
        {
            try
            {
                await SendRawOpAsync((int)GatewayOpCode.Heartbeat, _lastSequence, ct);
                await Task.Delay(intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var payload = await ReceiveOneAsync(ct);
                if (payload is null) break;
                await HandlePayloadAsync(payload, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Boucle de réception Gateway interrompue, tentative de reconnexion.");
            StateChanged?.Invoke(GatewayConnectionState.Reconnecting);
            await TryReconnectAsync(ct);
        }
    }

    private async Task TryReconnectAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 5 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 3)), ct);
                await ConnectSocketAsync(resume: true, ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Échec reconnexion tentative {Attempt}", attempt);
            }
        }
        StateChanged?.Invoke(GatewayConnectionState.Disconnected);
    }

    private async Task HandlePayloadAsync(GatewayPayload payload, CancellationToken ct)
    {
        if (payload.Sequence is not null) _lastSequence = payload.Sequence;

        switch ((GatewayOpCode)payload.Op)
        {
            case GatewayOpCode.Dispatch:
                if (payload.EventName == "READY" && payload.D is not null)
                {
                    var readyRoot = payload.D.Value;
                    if (readyRoot.TryGetProperty("session_id", out var sid))
                        _sessionId = sid.GetString();
                    _isReady = true;
                    StateChanged?.Invoke(GatewayConnectionState.Ready);
                }
                if (payload.EventName is not null)
                {
                    EventReceived?.Invoke(new DiscordDispatchEvent(payload.EventName, payload.D ?? default));
                }
                break;

            case GatewayOpCode.Heartbeat:
                await SendRawOpAsync((int)GatewayOpCode.Heartbeat, _lastSequence, ct);
                break;

            case GatewayOpCode.Reconnect:
                _logger.LogInformation("Le Gateway demande une reconnexion.");
                await TryReconnectAsync(ct);
                break;

            case GatewayOpCode.InvalidSession:
                _logger.LogWarning("Session invalide, ré-identification complète.");
                _sessionId = null;
                _lastSequence = null;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                await SendAsync((int)GatewayOpCode.Identify, new IdentifyPayload { Token = _token }, ct);
                break;

            case GatewayOpCode.HeartbeatAck:
                // Rien à faire ; utile pour détecter une connexion "zombie" si on veut aller plus loin.
                break;
        }
    }

    private async Task<GatewayPayload?> ReceiveOneAsync(CancellationToken ct)
    {
        if (_socket is null) return null;
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        ms.Position = 0;
        return await JsonSerializer.DeserializeAsync<GatewayPayload>(ms, JsonOptions, ct);
    }

    public Task SendVoiceStateUpdateAsync(string guildId, string? channelId, bool mute = false, bool deaf = false, bool selfVideo = false, bool selfStream = false, CancellationToken ct = default)
    {
        var payload = new
        {
            guild_id = guildId,
            channel_id = channelId,
            self_mute = mute,
            self_deaf = deaf,
            self_video = selfVideo,
            self_stream = selfStream
        };
        return SendAsync(4, payload, ct);
    }

    public Task SendStreamCreateAsync(string guildId, string channelId, CancellationToken ct = default)
    {
        var payload = new
        {
            type = "guild",
            guild_id = guildId,
            channel_id = channelId,
            preferred_region = (string?)null
        };
        return SendAsync(18, payload, ct);
    }

    public Task SendStreamDeleteAsync(string streamKey, CancellationToken ct = default)
    {
        var payload = new
        {
            stream_key = streamKey
        };
        return SendAsync(19, payload, ct);
    }

    public Task SendPresenceUpdateAsync(string status, IEnumerable<object>? activities = null, CancellationToken ct = default)
    {
        var payload = new
        {
            status = status,
            since = 0,
            activities = activities?.ToList() ?? new List<object>(),
            afk = false
        };
        return SendAsync((int)GatewayOpCode.PresenceUpdate, payload, ct);
    }

    private Task SendAsync<T>(int opCode, T data, CancellationToken ct)
    {
        var envelope = new { op = opCode, d = data };
        return SendJsonAsync(envelope, ct);
    }

    private Task SendRawOpAsync(int opCode, int? seq, CancellationToken ct)
    {
        var envelope = new { op = opCode, d = seq };
        return SendJsonAsync(envelope, ct);
    }

    private async Task SendJsonAsync<T>(T payload, CancellationToken ct)
    {
        if (_socket is null || _socket.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCts?.Cancel();
        if (_socket?.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { /* best effort */ }
        }
        _socket?.Dispose();
        _lifetimeCts?.Dispose();
    }
}

public readonly record struct DiscordDispatchEvent(string Name, JsonElement Data);

public enum GatewayConnectionState
{
    Connecting,
    Ready,
    Reconnecting,
    Disconnected,
}
