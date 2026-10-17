using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Concentus.Enums;
using Concentus.Structs;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NSec.Cryptography;

namespace DiscordNative.Protocol.Voice;

/// <summary>
/// Client Vocal Discord 100% C# natif (Voice Gateway v4 + UDP RTP + AEAD XChaCha20-Poly1305 / AES256-GCM + Opus 48kHz stéréo + WASAPI).
/// </summary>
public sealed class DiscordVoiceClient : IAsyncDisposable
{
    private readonly string _guildId;
    private readonly string _userId;
    private readonly string _sessionId;
    private readonly string _token;
    private readonly string _endpoint;

    private ClientWebSocket? _ws;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    private uint _ssrc;
    private string _udpIp = string.Empty;
    private int _udpPort;
    private string _selectedMode = "aead_xchacha20_poly1305_rtpsize";
    private byte[]? _secretKey;

    private ushort _rtpSeq;
    private uint _rtpTimestamp;
    private uint _nonceCounter;
    private bool _isSpeaking;
    private DateTime _lastLocalVoiceActivityUtc = DateTime.MinValue;

    private WasapiCapture? _micCapture;
    private WasapiLoopbackCapture? _loopbackCapture;
    private readonly ConcurrentQueue<short> _loopbackPcmQueue = new();
    private BufferedWaveProvider? _playbackBuffer;
    private WasapiOut? _speakerOut;
    private readonly ConcurrentQueue<short> _micPcmQueue = new();
    private readonly object _encodeLock = new();
    private OpusEncoder? _opusEncoder;
    private readonly KrispAudioProcessor _krispProcessor = new();
    private readonly short[] _krispMonoBuf = new short[960];
    private readonly ConcurrentDictionary<uint, OpusDecoder> _decoders = new();
    private readonly ConcurrentDictionary<uint, string> _ssrcToUserId = new();
    private readonly ConcurrentDictionary<string, DateTime> _remoteLastPacketUtc = new();

    public event Action<string, bool>? UserSpeakingChanged;

    public bool IsKrispEnabled { get; set; } = true;

    private bool _isSystemAudioSharingEnabled;
    public bool IsSystemAudioSharingEnabled
    {
        get => _isSystemAudioSharingEnabled;
        set
        {
            if (_isSystemAudioSharingEnabled != value)
            {
                _isSystemAudioSharingEnabled = value;
                UpdateSystemAudioLoopback(value);
            }
        }
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (value && _isSpeaking)
            {
                _isSpeaking = false;
                UserSpeakingChanged?.Invoke(_userId, false);
                _ = SendSpeakingAsync(false);
            }
        }
    }

    public bool IsDeafened { get; set; }

    public DiscordVoiceClient(string guildId, string userId, string sessionId, string token, string endpoint, bool enableKrisp = true)
    {
        _guildId = guildId;
        _userId = userId;
        _sessionId = sessionId;
        _token = token;
        _endpoint = endpoint.Replace(":80", "").Replace(":443", "");
        IsKrispEnabled = enableKrisp;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ws = new ClientWebSocket();
            _ws.Options.SetRequestHeader("Origin", "https://discord.com");

            var uri = new Uri($"wss://{_endpoint}/?v=8");
            await _ws.ConnectAsync(uri, _cts.Token);

            _ = Task.Run(() => GatewayReceiveLoopAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                $"[{DateTime.Now}] Voice ConnectAsync failed on {_endpoint}: {ex}\n\n");
            throw;
        }
    }

    private async Task GatewayReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = await _ws.ReceiveAsync(buffer, ct);
                    if (res.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, res.Count);
                } while (!res.EndOfMessage);

                ms.Position = 0;
                using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
                var root = doc.RootElement;
                if (!root.TryGetProperty("op", out var opEl)) continue;
                int op = opEl.GetInt32();

                switch (op)
                {
                    case 8: // Hello (heartbeat interval)
                        if (root.TryGetProperty("d", out var helloD) && helloD.TryGetProperty("heartbeat_interval", out var hbEl))
                        {
                            int intervalMs = (int)hbEl.GetDouble();
                            _ = Task.Run(() => HeartbeatLoopAsync(intervalMs, ct), ct);

                            // Voice Gateway v8 : Envoyer OP 0 (Identify) dès réception du Hello
                            var identify = new
                            {
                                op = 0,
                                d = new
                                {
                                    server_id = _guildId,
                                    user_id = _userId,
                                    session_id = _sessionId,
                                    token = _token
                                }
                            };
                            await SendJsonAsync(identify, ct);
                        }
                        break;

                    case 2: // Ready (SSRC, IP, Port, Modes)
                        if (root.TryGetProperty("d", out var readyD))
                        {
                            _ssrc = readyD.GetProperty("ssrc").GetUInt32();
                            _udpIp = readyD.GetProperty("ip").GetString() ?? "";
                            _udpPort = readyD.GetProperty("port").GetInt32();

                            if (readyD.TryGetProperty("modes", out var modesEl) && modesEl.ValueKind == JsonValueKind.Array)
                            {
                                var modes = modesEl.EnumerateArray().Select(m => m.GetString() ?? "").ToList();
                                if (modes.Contains("aead_xchacha20_poly1305_rtpsize"))
                                    _selectedMode = "aead_xchacha20_poly1305_rtpsize";
                                else if (modes.Contains("aead_aes256_gcm_rtpsize"))
                                    _selectedMode = "aead_aes256_gcm_rtpsize";
                            }

                            await SetupUdpAndSelectProtocolAsync(ct);
                        }
                        break;

                    case 4: // Session Description (secret_key)
                        if (root.TryGetProperty("d", out var sdD) && sdD.TryGetProperty("secret_key", out var keyEl) && keyEl.ValueKind == JsonValueKind.Array)
                        {
                            _secretKey = keyEl.EnumerateArray().Select(b => (byte)b.GetInt32()).ToArray();
                            if (sdD.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String)
                            {
                                _selectedMode = modeEl.GetString() ?? _selectedMode;
                            }
                            await SendSpeakingAsync(false);
                            StartAudioPipeline(ct);
                        }
                        break;

                    case 5: // Speaking (autre participant parle ou arrête de parler)
                        if (root.TryGetProperty("d", out var spkD))
                        {
                            var spkUserId = spkD.TryGetProperty("user_id", out var uidEl) && uidEl.ValueKind == JsonValueKind.String ? uidEl.GetString() : null;
                            uint spkSsrc = spkD.TryGetProperty("ssrc", out var ssrcEl) && ssrcEl.ValueKind == JsonValueKind.Number ? ssrcEl.GetUInt32() : 0;
                            bool isSpk = false;
                            if (spkD.TryGetProperty("speaking", out var spkVal))
                            {
                                if (spkVal.ValueKind == JsonValueKind.Number) isSpk = spkVal.GetInt32() != 0;
                                else if (spkVal.ValueKind == JsonValueKind.True) isSpk = true;
                            }
                            if (!string.IsNullOrWhiteSpace(spkUserId))
                            {
                                if (spkSsrc != 0) _ssrcToUserId[spkSsrc] = spkUserId!;
                                UserSpeakingChanged?.Invoke(spkUserId!, isSpk);
                            }
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                $"[{DateTime.Now}] Voice GatewayReceiveLoopAsync error: {ex}\n\n");
        }
    }

    private async Task SetupUdpAndSelectProtocolAsync(CancellationToken ct)
    {
        _udp = new UdpClient();
        _udp.Connect(_udpIp, _udpPort);

        // IP Discovery packet (74 bytes)
        byte[] disc = new byte[74];
        BinaryPrimitives.WriteUInt16BigEndian(disc.AsSpan(0, 2), 1); // Type 1 = Request
        BinaryPrimitives.WriteUInt16BigEndian(disc.AsSpan(2, 2), 70); // Length = 70
        BinaryPrimitives.WriteUInt32BigEndian(disc.AsSpan(4, 4), _ssrc);
        await _udp.SendAsync(disc, disc.Length);

        var recvTask = _udp.ReceiveAsync();
        var completed = await Task.WhenAny(recvTask, Task.Delay(3000, ct));
        string extIp = _udpIp;
        int extPort = _udpPort;

        if (completed == recvTask)
        {
            var resp = recvTask.Result.Buffer;
            if (resp.Length >= 74)
            {
                int nullIdx = Array.IndexOf(resp, (byte)0, 8);
                int len = nullIdx > 8 ? nullIdx - 8 : 64;
                extIp = Encoding.ASCII.GetString(resp, 8, Math.Min(len, 64)).TrimEnd('\0');
                extPort = BinaryPrimitives.ReadUInt16BigEndian(resp.AsSpan(72, 2));
            }
        }

        // OP 1 : Select Protocol
        var selectProto = new
        {
            op = 1,
            d = new
            {
                protocol = "udp",
                data = new
                {
                    address = extIp,
                    port = extPort,
                    mode = _selectedMode
                }
            }
        };
        await SendJsonAsync(selectProto, ct);
    }

    private void StartAudioPipeline(CancellationToken ct)
    {
        try
        {
            _opusEncoder = new OpusEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_VOIP)
            {
                Bitrate = 64000
            };

            // 1. Démarrer la sortie audio (Haut-parleurs / Casque)
            try
            {
                _playbackBuffer = new BufferedWaveProvider(new WaveFormat(48000, 16, 2))
                {
                    BufferDuration = TimeSpan.FromSeconds(2),
                    DiscardOnBufferOverflow = true
                };
                _speakerOut = new WasapiOut(AudioClientShareMode.Shared, 40);
                _speakerOut.Init(_playbackBuffer);
                _speakerOut.Play();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                    $"[{DateTime.Now}] WasapiOut failed: {ex}\n\n");
            }

            // 2. Boucle de réception UDP -> Déchiffrement -> Décodage Opus -> Haut-parleurs
            _ = Task.Run(() => UdpReceiveAudioLoopAsync(ct), ct);

            // 3. Démarrer la capture Microphone WASAPI
            try
            {
                _micCapture = new WasapiCapture();
                var capFormat = _micCapture.WaveFormat;
                _micCapture.DataAvailable += (_, e) => OnMicDataAvailable(e.Buffer, e.BytesRecorded, capFormat);
                _micCapture.StartRecording();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                    $"[{DateTime.Now}] WasapiCapture failed: {ex}\n\n");
            }

            if (_isSystemAudioSharingEnabled)
            {
                UpdateSystemAudioLoopback(true);
            }
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscordNative_crash.log"),
                $"[{DateTime.Now}] StartAudioPipeline error: {ex}\n\n");
        }
    }

    private void UpdateSystemAudioLoopback(bool enable)
    {
        try
        {
            if (enable)
            {
                if (_loopbackCapture != null) return;
                _loopbackCapture = new WasapiLoopbackCapture();
                var fmt = _loopbackCapture.WaveFormat;
                _loopbackCapture.DataAvailable += (_, e) => OnLoopbackDataAvailable(e.Buffer, e.BytesRecorded, fmt);
                _loopbackCapture.StartRecording();
            }
            else
            {
                var cap = _loopbackCapture;
                _loopbackCapture = null;
                if (cap != null)
                {
                    try { cap.StopRecording(); } catch { }
                    try { cap.Dispose(); } catch { }
                }
                while (_loopbackPcmQueue.TryDequeue(out _)) { }
            }
        }
        catch { }
    }

    private void OnLoopbackDataAvailable(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (!_isSystemAudioSharingEnabled || bytesRecorded <= 0) return;

        try
        {
            int channels = format.Channels;
            int sampleRate = format.SampleRate;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
            int bytesPerFrame = (isFloat ? 4 : 2) * channels;
            int inFrames = bytesRecorded / Math.Max(1, bytesPerFrame);
            if (inFrames <= 0) return;

            double ratio = 48000.0 / sampleRate;
            int outFrames = (int)(inFrames * ratio);

            // Prevent unbounded queue growth
            while (_loopbackPcmQueue.Count > 1920 * 6)
            {
                _loopbackPcmQueue.TryDequeue(out _);
            }

            for (int i = 0; i < outFrames; i++)
            {
                int srcFrame = Math.Min(inFrames - 1, (int)(i / ratio));
                int offset = srcFrame * bytesPerFrame;

                short left, right;
                if (isFloat)
                {
                    float fl = BitConverter.ToSingle(buffer, offset) * 0.45f;
                    float fr = (channels > 1 ? BitConverter.ToSingle(buffer, offset + 4) : fl) * 0.45f;
                    left = (short)Math.Clamp(fl * 32767f, short.MinValue, short.MaxValue);
                    right = (short)Math.Clamp(fr * 32767f, short.MinValue, short.MaxValue);
                }
                else
                {
                    left = (short)(BitConverter.ToInt16(buffer, offset) * 0.45);
                    right = (short)((channels > 1 ? BitConverter.ToInt16(buffer, offset + 2) : left) * 0.45);
                }

                _loopbackPcmQueue.Enqueue(left);
                _loopbackPcmQueue.Enqueue(right);
            }
        }
        catch { }
    }

    private void OnMicDataAvailable(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if ((IsMuted && !_isSystemAudioSharingEnabled) || IsDeafened || _secretKey == null || _udp == null || bytesRecorded <= 0) return;

        try
        {
            // Convertir le flux micro en PCM 16-bit 48kHz Stéréo
            int channels = format.Channels;
            int sampleRate = format.SampleRate;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
            int bytesPerFrame = (isFloat ? 4 : 2) * channels;
            int inFrames = bytesRecorded / Math.Max(1, bytesPerFrame);
            if (inFrames <= 0) return;

            double ratio = 48000.0 / sampleRate;
            int outFrames = (int)(inFrames * ratio);

            for (int i = 0; i < outFrames; i++)
            {
                int srcFrame = Math.Min(inFrames - 1, (int)(i / ratio));
                int offset = srcFrame * bytesPerFrame;

                short left = 0, right = 0;
                if (!IsMuted)
                {
                    if (isFloat)
                    {
                        float fl = BitConverter.ToSingle(buffer, offset);
                        float fr = channels > 1 ? BitConverter.ToSingle(buffer, offset + 4) : fl;
                        left = (short)Math.Clamp(fl * 32767f, short.MinValue, short.MaxValue);
                        right = (short)Math.Clamp(fr * 32767f, short.MinValue, short.MaxValue);
                    }
                    else
                    {
                        left = BitConverter.ToInt16(buffer, offset);
                        right = channels > 1 ? BitConverter.ToInt16(buffer, offset + 2) : left;
                    }
                }

                _micPcmQueue.Enqueue(left);
                _micPcmQueue.Enqueue(right);
            }

            // Une trame Opus 20ms @ 48kHz stéréo = 960 échantillons par canal = 1920 shorts
            while (_micPcmQueue.Count >= 1920)
            {
                short[] frame = new short[1920];
                for (int i = 0; i < 1920; i++)
                {
                    _micPcmQueue.TryDequeue(out frame[i]);
                }
                SendOpusFrame(frame);
            }
        }
        catch { }
    }

    private void SendOpusFrame(short[] pcmFrame)
    {
        if (_opusEncoder == null || _secretKey == null || _udp == null) return;

        // 1. Suppression de bruit Krisp sur la voix du microphone (avant mixage de l'audio du stream)
        if (IsKrispEnabled && !IsMuted)
        {
            for (int i = 0; i < 960; i++)
            {
                int l = pcmFrame[i * 2];
                int r = pcmFrame[i * 2 + 1];
                _krispMonoBuf[i] = (short)((l + r) / 2);
            }

            _krispProcessor.ProcessFrame48kMono(_krispMonoBuf);

            for (int i = 0; i < 960; i++)
            {
                short clean = _krispMonoBuf[i];
                pcmFrame[i * 2] = clean;
                pcmFrame[i * 2 + 1] = clean;
            }
        }

        // 1b. Mixage de l'audio système si le partage d'écran avec son est actif
        if (_isSystemAudioSharingEnabled && _loopbackPcmQueue.Count >= 1920)
        {
            for (int i = 0; i < 1920; i++)
            {
                if (_loopbackPcmQueue.TryDequeue(out short sysSample))
                {
                    pcmFrame[i] = (short)Math.Clamp((int)pcmFrame[i] + sysSample, short.MinValue, short.MaxValue);
                }
            }
        }

        // 2. Détection d'activité vocale (VAD) avec maintien de 280ms pour éviter tout clignotement
        long sumSq = 0;
        for (int i = 0; i < pcmFrame.Length; i += 4)
        {
            long s = pcmFrame[i];
            sumSq += s * s;
        }
        double rms = Math.Sqrt((double)sumSq / (pcmFrame.Length / 4));
        var now = DateTime.UtcNow;
        double vadThreshold = IsKrispEnabled ? 130.0 : 190.0;
        if (rms > vadThreshold)
        {
            _lastLocalVoiceActivityUtc = now;
        }
        bool active = (now - _lastLocalVoiceActivityUtc).TotalMilliseconds < 280;

        if (active && !_isSpeaking)
        {
            _isSpeaking = true;
            UserSpeakingChanged?.Invoke(_userId, true);
            _ = SendSpeakingAsync(true);
        }
        else if (!active && _isSpeaking)
        {
            _isSpeaking = false;
            UserSpeakingChanged?.Invoke(_userId, false);
            _ = SendSpeakingAsync(false);
            return;
        }
        else if (!active)
        {
            return;
        }

        byte[] opusOut = new byte[1275];
        int opusLen;
        lock (_encodeLock)
        {
            opusLen = _opusEncoder.Encode(pcmFrame, 0, 960, opusOut, 0, opusOut.Length);
        }

        // En-tête RTP 12 octets
        byte[] rtpHeader = new byte[12];
        rtpHeader[0] = 0x80;
        rtpHeader[1] = 0x78;
        BinaryPrimitives.WriteUInt16BigEndian(rtpHeader.AsSpan(2, 2), _rtpSeq++);
        BinaryPrimitives.WriteUInt32BigEndian(rtpHeader.AsSpan(4, 4), _rtpTimestamp);
        BinaryPrimitives.WriteUInt32BigEndian(rtpHeader.AsSpan(8, 4), _ssrc);
        _rtpTimestamp += 960;

        byte[]? packet = EncryptRtpPacket(rtpHeader, opusOut.AsSpan(0, opusLen));
        if (packet != null)
        {
            _ = _udp.SendAsync(packet, packet.Length);
        }
    }

    private byte[]? EncryptRtpPacket(byte[] rtpHeader, ReadOnlySpan<byte> payload)
    {
        if (_secretKey == null) return null;
        uint counter = ++_nonceCounter;
        byte[] nonceSuffix = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(nonceSuffix, counter);

        if (_selectedMode == "aead_aes256_gcm_rtpsize")
        {
            byte[] nonce = new byte[12];
            Buffer.BlockCopy(nonceSuffix, 0, nonce, 0, 4);

            byte[] cipher = new byte[payload.Length];
            byte[] tag = new byte[16];
            using var aes = new AesGcm(_secretKey, 16);
            aes.Encrypt(nonce, payload, cipher, tag, rtpHeader);

            byte[] finalPacket = new byte[12 + cipher.Length + 16 + 4];
            Buffer.BlockCopy(rtpHeader, 0, finalPacket, 0, 12);
            Buffer.BlockCopy(cipher, 0, finalPacket, 12, cipher.Length);
            Buffer.BlockCopy(tag, 0, finalPacket, 12 + cipher.Length, 16);
            Buffer.BlockCopy(nonceSuffix, 0, finalPacket, 12 + cipher.Length + 16, 4);
            return finalPacket;
        }
        else
        {
            // aead_xchacha20_poly1305_rtpsize
            byte[] nonce = new byte[24];
            Buffer.BlockCopy(nonceSuffix, 0, nonce, 0, 4);

            var algo = AeadAlgorithm.XChaCha20Poly1305;
            using var key = Key.Import(algo, _secretKey, KeyBlobFormat.RawSymmetricKey);
            byte[] encrypted = algo.Encrypt(key, nonce, rtpHeader, payload);

            byte[] finalPacket = new byte[12 + encrypted.Length + 4];
            Buffer.BlockCopy(rtpHeader, 0, finalPacket, 0, 12);
            Buffer.BlockCopy(encrypted, 0, finalPacket, 12, encrypted.Length);
            Buffer.BlockCopy(nonceSuffix, 0, finalPacket, 12 + encrypted.Length, 4);
            return finalPacket;
        }
    }

    private async Task UdpReceiveAudioLoopAsync(CancellationToken ct)
    {
        while (_udp != null && !ct.IsCancellationRequested)
        {
            try
            {
                var res = await _udp.ReceiveAsync(ct);
                if (IsDeafened || _secretKey == null || _playbackBuffer == null) continue;
                ProcessIncomingRtpPacket(res.Buffer);
            }
            catch { }
        }
    }

    private void ProcessIncomingRtpPacket(byte[] buf)
    {
        if (buf.Length < 32 || _playbackBuffer == null) return;

        byte b0 = buf[0];
        byte b1 = buf[1];
        // Filtrer uniquement les paquets audio RTP (PayloadType 0x78)
        if ((b1 & 0x7F) != 0x78) return;

        uint ssrc = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(8, 4));
        if (ssrc == _ssrc) return;

        int csrcCount = b0 & 0x0F;
        bool hasExt = (b0 & 0x10) != 0;
        int headerLen = 12 + (csrcCount * 4);
        if (hasExt)
        {
            if (buf.Length < headerLen + 4) return;
            headerLen += 4;
        }

        if (buf.Length <= headerLen + 20) return;

        var aad = buf.AsSpan(0, headerLen).ToArray();
        var nonceSuffix = buf.AsSpan(buf.Length - 4, 4);
        var cipherSpan = buf.AsSpan(headerLen, buf.Length - headerLen - 4);

        byte[]? decrypted = DecryptRtpPacket(aad, cipherSpan, nonceSuffix);
        if (decrypted == null || decrypted.Length == 0) return;

        ReadOnlySpan<byte> opusPayload = decrypted;
        if (hasExt)
        {
            ushort extWords = BinaryPrimitives.ReadUInt16BigEndian(aad.AsSpan(headerLen - 2, 2));
            int extBytes = extWords * 4;
            if (opusPayload.Length <= extBytes) return;
            opusPayload = opusPayload.Slice(extBytes);
        }

#pragma warning disable CS0618
        var decoder = _decoders.GetOrAdd(ssrc, _ => new OpusDecoder(48000, 2));
        short[] pcmOut = new short[960 * 2];
        byte[] opusArr = opusPayload.ToArray();
        int samples = decoder.Decode(opusArr, 0, opusArr.Length, pcmOut, 0, 960, false);
#pragma warning restore CS0618
        if (samples > 0)
        {
            byte[] pcmBytes = new byte[samples * 2 * 2];
            Buffer.BlockCopy(pcmOut, 0, pcmBytes, 0, pcmBytes.Length);
            _playbackBuffer.AddSamples(pcmBytes, 0, pcmBytes.Length);
        }
    }

    private byte[]? DecryptRtpPacket(byte[] aad, ReadOnlySpan<byte> cipherWithTag, ReadOnlySpan<byte> nonceSuffix)
    {
        if (_secretKey == null) return null;
        try
        {
            if (_selectedMode == "aead_aes256_gcm_rtpsize")
            {
                if (cipherWithTag.Length < 16) return null;
                byte[] nonce = new byte[12];
                nonceSuffix.CopyTo(nonce.AsSpan(0, 4));
                var cipher = cipherWithTag.Slice(0, cipherWithTag.Length - 16);
                var tag = cipherWithTag.Slice(cipherWithTag.Length - 16, 16);
                byte[] plain = new byte[cipher.Length];
                using var aes = new AesGcm(_secretKey, 16);
                aes.Decrypt(nonce, cipher, tag, plain, aad);
                return plain;
            }
            else
            {
                byte[] nonce = new byte[24];
                nonceSuffix.CopyTo(nonce.AsSpan(0, 4));
                var algo = AeadAlgorithm.XChaCha20Poly1305;
                using var key = Key.Import(algo, _secretKey, KeyBlobFormat.RawSymmetricKey);
                return algo.Decrypt(key, nonce, aad, cipherWithTag);
            }
        }
        catch
        {
            return null;
        }
    }

    private Task SendSpeakingAsync(bool speaking)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return Task.CompletedTask;
        var payload = new
        {
            op = 5,
            d = new
            {
                speaking = speaking ? 1 : 0,
                delay = 0,
                ssrc = _ssrc
            }
        };
        return SendJsonAsync(payload, CancellationToken.None);
    }

    private async Task HeartbeatLoopAsync(int intervalMs, CancellationToken ct)
    {
        while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(intervalMs, ct);
                var hb = new { op = 3, d = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                await SendJsonAsync(hb, ct);
            }
            catch { break; }
        }
    }

    private async Task SendJsonAsync<T>(T payload, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        try { _micCapture?.StopRecording(); } catch { }
        try { _micCapture?.Dispose(); } catch { }
        try { _loopbackCapture?.StopRecording(); } catch { }
        try { _loopbackCapture?.Dispose(); } catch { }
        try { _krispProcessor.Dispose(); } catch { }
        try { _speakerOut?.Stop(); } catch { }
        try { _speakerOut?.Dispose(); } catch { }
        try { _udp?.Close(); } catch { }
        try { _udp?.Dispose(); } catch { }
        if (_ws is { State: WebSocketState.Open })
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
