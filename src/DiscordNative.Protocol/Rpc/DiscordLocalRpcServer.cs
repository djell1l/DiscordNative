using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DiscordNative.Protocol.Models;

namespace DiscordNative.Protocol.Rpc;

/// <summary>
/// Serveur Named Pipe IPC local (`discord-ipc-0` à `discord-ipc-9`) compatible avec le SDK Discord Rich Presence officiel.
/// Permet aux jeux et logiciels locaux (VS Code, Spotify, jeux Steam, etc.) d'envoyer leur Rich Presence (`SET_ACTIVITY`) directement à Diswin.
/// </summary>
public sealed class DiscordLocalRpcServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenLoopTask;
    private string? _pipeName;

    public event Action<DiscordActivity?>? LocalActivityChanged;

    public void Start()
    {
        _listenLoopTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        // Trouver le premier pipe disponible entre discord-ipc-0 et discord-ipc-9
        for (int i = 0; i < 10; i++)
        {
            string candidate = $"discord-ipc-{i}";
            if (!File.Exists($@"\\.\pipe\{candidate}"))
            {
                _pipeName = candidate;
                break;
            }
        }
        _pipeName ??= "discord-ipc-0";

        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);
                _ = Task.Run(() => HandleClientConnectionAsync(server, ct), ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch
            {
                server?.Dispose();
                await Task.Delay(2000, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        string? connectedAppId = null;
        string connectedAppName = "Application externe";
        try
        {
            using (pipe)
            {
                byte[] header = new byte[8];
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    int readHeader = await ReadExactAsync(pipe, header, 8, ct);
                    if (readHeader < 8) break;

                    int op = BitConverter.ToInt32(header, 0);
                    int len = BitConverter.ToInt32(header, 4);
                    if (len <= 0 || len > 1024 * 1024) break;

                    byte[] body = new byte[len];
                    int readBody = await ReadExactAsync(pipe, body, len, ct);
                    if (readBody < len) break;

                    string json = Encoding.UTF8.GetString(body);

                    if (op == 0) // HANDSHAKE
                    {
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("client_id", out var cidEl))
                        {
                            connectedAppId = cidEl.GetString();
                            if (!string.IsNullOrWhiteSpace(connectedAppId))
                            {
                                connectedAppName = await FetchAppNameAsync(connectedAppId!) ?? connectedAppName;
                            }
                        }

                        var readyReply = JsonSerializer.Serialize(new
                        {
                            cmd = "DISPATCH",
                            evt = "READY",
                            data = new
                            {
                                v = 1,
                                config = new
                                {
                                    cdn_host = "cdn.discordapp.com",
                                    api_endpoint = "//discord.com/api",
                                    environment = "production"
                                },
                                user = new
                                {
                                    id = "0",
                                    username = "DiswinUser",
                                    discriminator = "0",
                                    avatar = (string?)null
                                }
                            },
                            nonce = (string?)null
                        });
                        await WriteFrameAsync(pipe, 1, readyReply, ct);
                    }
                    else if (op == 1) // FRAME
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        string? cmd = root.TryGetProperty("cmd", out var cmdEl) ? cmdEl.GetString() : null;
                        string? nonce = root.TryGetProperty("nonce", out var nEl) && nEl.ValueKind == JsonValueKind.String ? nEl.GetString() : null;

                        if (string.Equals(cmd, "SET_ACTIVITY", StringComparison.OrdinalIgnoreCase))
                        {
                            DiscordActivity? parsedActivity = null;
                            if (root.TryGetProperty("args", out var argsEl) &&
                                argsEl.ValueKind == JsonValueKind.Object &&
                                argsEl.TryGetProperty("activity", out var actEl) &&
                                actEl.ValueKind == JsonValueKind.Object)
                            {
                                parsedActivity = ParseIpcActivity(actEl, connectedAppId, connectedAppName);
                            }

                            LocalActivityChanged?.Invoke(parsedActivity);

                            var ack = JsonSerializer.Serialize(new
                            {
                                cmd = "SET_ACTIVITY",
                                data = new { },
                                evt = (string?)null,
                                nonce
                            });
                            await WriteFrameAsync(pipe, 1, ack, ct);
                        }
                        else if (op == 2) // CLOSE
                        {
                            break;
                        }
                    }
                    else if (op == 3) // PING
                    {
                        await WriteFrameAsync(pipe, 4, json, ct);
                    }
                }
            }
        }
        catch { }
        finally
        {
            if (connectedAppId != null)
            {
                LocalActivityChanged?.Invoke(null);
            }
        }
    }

    private static DiscordActivity ParseIpcActivity(JsonElement actEl, string? appId, string appName)
    {
        var act = new DiscordActivity
        {
            ApplicationId = appId,
            Name = actEl.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nEl.GetString())
                ? nEl.GetString()!
                : appName,
            Type = actEl.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.Number ? tEl.GetInt32() : 0,
            Details = actEl.TryGetProperty("details", out var dEl) && dEl.ValueKind == JsonValueKind.String ? dEl.GetString() : null,
            State = actEl.TryGetProperty("state", out var sEl) && sEl.ValueKind == JsonValueKind.String ? sEl.GetString() : null,
        };

        if (actEl.TryGetProperty("timestamps", out var tsEl) && tsEl.ValueKind == JsonValueKind.Object)
        {
            long? start = tsEl.TryGetProperty("start", out var stEl) && stEl.ValueKind == JsonValueKind.Number ? stEl.GetInt64() : null;
            if (start is > 0 and < 10_000_000_000L) start *= 1000; // Convertir secondes en millisecondes si nécessaire
            act.Timestamps = new DiscordActivityTimestamps { Start = start };
        }

        if (actEl.TryGetProperty("assets", out var asEl) && asEl.ValueKind == JsonValueKind.Object)
        {
            act.Assets = new DiscordActivityAssets
            {
                LargeImage = asEl.TryGetProperty("large_image", out var li) && li.ValueKind == JsonValueKind.String ? li.GetString() : null,
                LargeText = asEl.TryGetProperty("large_text", out var lt) && lt.ValueKind == JsonValueKind.String ? lt.GetString() : null,
                SmallImage = asEl.TryGetProperty("small_image", out var si) && si.ValueKind == JsonValueKind.String ? si.GetString() : null,
                SmallText = asEl.TryGetProperty("small_text", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null,
            };
        }

        return act;
    }

    private static async Task<string?> FetchAppNameAsync(string appId)
    {
        try
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync($"https://discord.com/api/v9/oauth2/applications/{appId}/rpc");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
            {
                return nEl.GetString();
            }
        }
        catch { }
        return null;
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0) return offset;
            offset += read;
        }
        return offset;
    }

    private static async Task WriteFrameAsync(Stream stream, int op, string json, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[8 + payload.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0);
        BitConverter.GetBytes(payload.Length).CopyTo(frame, 4);
        Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
