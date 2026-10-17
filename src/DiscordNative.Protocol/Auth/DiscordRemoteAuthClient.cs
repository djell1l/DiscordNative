using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiscordNative.Protocol.Auth;

public sealed class RemoteAuthUser
{
    public string UserId { get; }
    public string Username { get; }
    public string Discriminator { get; }
    public string? AvatarHash { get; }

    public RemoteAuthUser(string userId, string username, string discriminator, string? avatarHash)
    {
        UserId = userId;
        Username = username;
        Discriminator = discriminator;
        AvatarHash = avatarHash;
    }

    public string DisplayTag => string.IsNullOrEmpty(Discriminator) || Discriminator == "0"
        ? $"@{Username}"
        : $"{Username}#{Discriminator}";
}

/// <summary>
/// Implémente le protocole officiel Discord Remote Auth v2 (QR Code Login via WebSocket + RSA-2048 OAEP-SHA256).
/// Permet la connexion instantanée en scannant le QR code avec l'application mobile Discord.
/// </summary>
public sealed class DiscordRemoteAuthClient : IDisposable
{
    private const string GatewayUrl = "wss://remote-auth-gateway.discord.gg/?v=2";
    private const string ApiBase = "https://discord.com/api/v9";

    private readonly HttpClient _http;
    private RSA? _rsa;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoopTask;
    private Task? _heartbeatLoopTask;
    private int _heartbeatIntervalMs = 41250;

    public event EventHandler<string>? QrCodeReady;
    public event EventHandler<RemoteAuthUser>? UserScanned;
    public event EventHandler<string>? Authenticated;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? Cancelled;

    public DiscordRemoteAuthClient(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _http = httpClient;
        }
        else
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://discord.com");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://discord.com/login");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Discord-Locale", "fr");

            var superProps = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "{\"os\":\"Windows\",\"browser\":\"Chrome\",\"device\":\"\",\"system_locale\":\"fr-FR\",\"browser_user_agent\":\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36\",\"browser_version\":\"128.0.0.0\",\"os_version\":\"10\",\"referrer\":\"\",\"referring_domain\":\"\",\"referrer_current\":\"\",\"referring_domain_current\":\"\",\"release_channel\":\"stable\",\"client_build_number\":330000,\"client_event_source\":null}"));
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Super-Properties", superProps);
        }
    }

    public async Task StartAsync(CancellationToken externalCt = default)
    {
        Stop();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;

        _rsa = RSA.Create(2048);
        var spki = _rsa.ExportSubjectPublicKeyInfo();
        var encodedPublicKey = Convert.ToBase64String(spki);

        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        _ws.Options.SetRequestHeader("Origin", "https://discord.com");
        _ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");

        try
        {
            await _ws.ConnectAsync(new Uri(GatewayUrl), ct);
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(encodedPublicKey, ct), ct);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Connexion au serveur QR échouée : {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(string encodedPublicKey, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Cancelled?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var jsonStr = Encoding.UTF8.GetString(ms.ToArray());
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                if (!root.TryGetProperty("op", out var opProp)) continue;
                var op = opProp.GetString();

                switch (op)
                {
                    case "hello":
                        if (root.TryGetProperty("heartbeat_interval", out var hbProp))
                        {
                            _heartbeatIntervalMs = hbProp.GetInt32();
                        }
                        StartHeartbeat(ct);

                        var initPayload = JsonSerializer.Serialize(new
                        {
                            op = "init",
                            encoded_public_key = encodedPublicKey
                        });
                        await SendJsonAsync(initPayload, ct);
                        break;

                    case "nonce_proof":
                        if (_rsa is not null && root.TryGetProperty("encrypted_nonce", out var nonceProp))
                        {
                            var encNonce = Convert.FromBase64String(nonceProp.GetString()!);
                            var decNonce = _rsa.Decrypt(encNonce, RSAEncryptionPadding.OaepSHA256);
                            var hash = SHA256.HashData(decNonce);
                            var proof = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');

                            var proofPayload = JsonSerializer.Serialize(new
                            {
                                op = "nonce_proof",
                                proof
                            });
                            await SendJsonAsync(proofPayload, ct);
                        }
                        break;

                    case "pending_remote_init":
                        if (root.TryGetProperty("fingerprint", out var fpProp))
                        {
                            var fingerprint = fpProp.GetString()!;
                            var qrUrl = $"https://discord.com/ra/{fingerprint}";
                            QrCodeReady?.Invoke(this, qrUrl);
                        }
                        break;

                    case "pending_ticket":
                        if (_rsa is not null && root.TryGetProperty("encrypted_user_payload", out var userProp))
                        {
                            try
                            {
                                var encPayload = Convert.FromBase64String(userProp.GetString()!);
                                var decPayload = _rsa.Decrypt(encPayload, RSAEncryptionPadding.OaepSHA256);
                                var userStr = Encoding.UTF8.GetString(decPayload);
                                var parts = userStr.Split(':');
                                if (parts.Length >= 4)
                                {
                                    var user = new RemoteAuthUser(parts[0], parts[3], parts[1], parts[2]);
                                    UserScanned?.Invoke(this, user);
                                }
                            }
                            catch { }
                        }
                        break;

                    case "pending_login":
                        if (root.TryGetProperty("ticket", out var ticketProp))
                        {
                            var ticket = ticketProp.GetString()!;
                            await ExchangeTicketAsync(ticket, ct);
                        }
                        break;

                    case "cancel":
                        Cancelled?.Invoke(this, EventArgs.Empty);
                        return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Erreur session QR : {ex.Message}");
        }
    }

    private void StartHeartbeat(CancellationToken ct)
    {
        _heartbeatLoopTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
                {
                    await Task.Delay(_heartbeatIntervalMs, ct);
                    if (_ws?.State == WebSocketState.Open)
                    {
                        var hb = JsonSerializer.Serialize(new { op = "heartbeat" });
                        await SendJsonAsync(hb, ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private async Task ExchangeTicketAsync(string ticket, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/users/@me/remote-auth/login");
            req.Content = JsonContent.Create(new { ticket });

            var response = await _http.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errJson = await response.Content.ReadAsStringAsync(ct);
                ErrorOccurred?.Invoke(this, $"Échec d'échange de jeton (HTTP {(int)response.StatusCode}) : {errJson}");
                return;
            }

            var resObj = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
            var encTokenStr = resObj?["encrypted_token"]?.ToString();
            if (string.IsNullOrWhiteSpace(encTokenStr) || _rsa is null)
            {
                ErrorOccurred?.Invoke(this, "Réponse de validation QR invalide.");
                return;
            }

            var encTokenBytes = Convert.FromBase64String(encTokenStr);
            var decTokenBytes = _rsa.Decrypt(encTokenBytes, RSAEncryptionPadding.OaepSHA256);
            var token = Encoding.UTF8.GetString(decTokenBytes);

            Authenticated?.Invoke(this, token);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Erreur lors de l'échange du ticket QR : {ex.Message}");
        }
    }

    private async Task SendJsonAsync(string json, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_ws != null)
        {
            try { _ws.Dispose(); } catch { }
            _ws = null;
        }

        if (_rsa != null)
        {
            try { _rsa.Dispose(); } catch { }
            _rsa = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
