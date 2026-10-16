using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DiscordNative.Protocol.Auth;

/// <summary>
/// Reproduit le flow de login du site web Discord (POST /auth/login puis, si demandé,
/// POST /auth/mfa/totp, /auth/mfa/sms ou /auth/mfa/backup) pour récupérer un token de compte utilisateur.
/// Permet également la validation directe d'un jeton de compte (User Token).
/// </summary>
public sealed class DiscordAuthClient : IDisposable
{
    private const string ApiBase = "https://discord.com/api/v9";
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DiscordAuthClient()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://discord.com");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://discord.com/login");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Discord-Locale", "en-US");

        string tz = "UTC";
        try
        {
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var ianaId) && !string.IsNullOrWhiteSpace(ianaId))
            {
                tz = ianaId;
            }
        }
        catch { }

        if (!tz.All(c => c >= 32 && c <= 126))
        {
            tz = "UTC";
        }
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Discord-Timezone", tz);

        var superProps = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            "{\"os\":\"Windows\",\"browser\":\"Chrome\",\"device\":\"\",\"system_locale\":\"en-US\",\"browser_user_agent\":\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36\",\"browser_version\":\"128.0.0.0\",\"os_version\":\"10\",\"referrer\":\"\",\"referring_domain\":\"\",\"referrer_current\":\"\",\"referring_domain_current\":\"\",\"release_channel\":\"stable\",\"client_build_number\":330000,\"client_event_source\":null}"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Super-Properties", superProps);
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var requestObj = new LoginRequest { Login = email, Password = password };
        var content = JsonContent.Create(requestObj, options: JsonOptions);
        var response = await _http.PostAsync($"{ApiBase}/auth/login", content, ct);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct)
                   ?? throw new DiscordAuthException("Réponse de login illisible.");

        if (!response.IsSuccessStatusCode)
        {
            if (body.CaptchaKey is not null && body.CaptchaKey.Count > 0)
            {
                throw new DiscordAuthException("Discord requiert une vérification captcha (hCaptcha). Utilisez l'onglet « Code QR » ou « Jeton (Token) ».");
            }
            throw new DiscordAuthException(body.Message ?? "Échec de connexion (identifiants invalides ?).");
        }

        if (body.Mfa == true)
        {
            return LoginResult.RequiresMfa(
                body.Ticket ?? throw new DiscordAuthException("Ticket MFA manquant."),
                body.LoginInstanceId,
                totp: body.Totp ?? true,
                sms: body.Sms ?? false,
                backup: body.Backup ?? true,
                webauthn: body.Webauthn is not null);
        }

        return LoginResult.Success(body.Token ?? throw new DiscordAuthException("Token manquant dans la réponse."));
    }

    public Task<LoginResult> SubmitTotpAsync(string ticket, string code, string? loginInstanceId = null, CancellationToken ct = default)
    {
        code = code.Replace(" ", "").Replace("-", "").Trim();
        string endpoint = code.Length == 8 ? "backup" : "totp";
        return SubmitMfaAsync(ticket, code, endpoint, loginInstanceId, ct);
    }

    public async Task<LoginResult> SubmitMfaAsync(
        string ticket,
        string code,
        string mfaType = "totp",
        string? loginInstanceId = null,
        CancellationToken ct = default)
    {
        code = code.Replace(" ", "").Replace("-", "").Trim();

        var payload = new MfaVerifyRequest
        {
            Ticket = ticket,
            Code = code,
            LoginInstanceId = string.IsNullOrWhiteSpace(loginInstanceId) ? null : loginInstanceId
        };

        var content = JsonContent.Create(payload, options: JsonOptions);
        var response = await _http.PostAsync($"{ApiBase}/auth/mfa/{mfaType}", content, ct);

        LoginResponse? body = null;
        try
        {
            body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
        }
        catch { }

        if (!response.IsSuccessStatusCode)
        {
            if (body?.CaptchaKey is not null && body.CaptchaKey.Count > 0)
            {
                throw new DiscordAuthException("Discord requiert un captcha sur la validation MFA. Utilisez l'onglet « Code QR » ou « Jeton (Token) ».");
            }

            if (body?.Code == 60006)
            {
                throw new DiscordAuthException("Le ticket MFA a expiré (60006). Veuillez recommencer la connexion.");
            }

            if (body?.Code == 60008)
            {
                if (mfaType == "totp" && code.Length == 8)
                {
                    return await SubmitMfaAsync(ticket, code, "backup", loginInstanceId, ct);
                }
                if (mfaType == "backup" && code.Length == 6)
                {
                    return await SubmitMfaAsync(ticket, code, "totp", loginInstanceId, ct);
                }

                throw new DiscordAuthException("Code 2FA refusé par Discord (erreur 60008). Utilisez le QR Code ou votre Jeton Discord.");
            }

            throw new DiscordAuthException(body?.Message ?? $"Échec de validation MFA (HTTP {(int)response.StatusCode}).");
        }

        return LoginResult.Success(body?.Token ?? throw new DiscordAuthException("Token manquant après MFA."));
    }

    public async Task SendSmsAsync(string ticket, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, string> { ["ticket"] = ticket };
        var response = await _http.PostAsJsonAsync($"{ApiBase}/auth/mfa/sms/send", payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            LoginResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
            }
            catch { }
            throw new DiscordAuthException(body?.Message ?? "Impossible d'envoyer le code SMS par Discord.");
        }
    }

    public async Task<string> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        token = new string(token.Trim().Trim('"').Trim('\'').Where(c => c >= 32 && c <= 126).ToArray());
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new DiscordAuthException("Le jeton ne peut pas être vide.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/users/@me");
        request.Headers.TryAddWithoutValidation("Authorization", token);

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new DiscordAuthException("Jeton invalide ou non autorisé.");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        var username = json?["username"]?.ToString() ?? "Utilisateur";
        var discriminator = json?["discriminator"]?.ToString();
        return string.IsNullOrEmpty(discriminator) || discriminator == "0"
            ? $"@{username}"
            : $"{username}#{discriminator}";
    }

    public DiscordRemoteAuthClient CreateRemoteAuthClient() => new(_http);

    public void Dispose() => _http.Dispose();

    private sealed class LoginRequest
    {
        [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
        [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
        [JsonPropertyName("undelete")] public bool Undelete { get; set; } = false;
        [JsonPropertyName("login_source")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LoginSource { get; set; } = null;
        [JsonPropertyName("gift_code_sku_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GiftCodeSkuId { get; set; } = null;
    }

    private sealed class MfaVerifyRequest
    {
        [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
        [JsonPropertyName("ticket")] public string Ticket { get; set; } = string.Empty;
        [JsonPropertyName("login_instance_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LoginInstanceId { get; set; }
        [JsonPropertyName("gift_code_sku_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GiftCodeSkuId { get; set; } = null;
        [JsonPropertyName("login_source")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LoginSource { get; set; } = null;
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("mfa")] public bool? Mfa { get; set; }
        [JsonPropertyName("ticket")] public string? Ticket { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("sms")] public bool? Sms { get; set; }
        [JsonPropertyName("totp")] public bool? Totp { get; set; }
        [JsonPropertyName("backup")] public bool? Backup { get; set; }
        [JsonPropertyName("webauthn")] public object? Webauthn { get; set; }
        [JsonPropertyName("login_instance_id")] public string? LoginInstanceId { get; set; }
        [JsonPropertyName("captcha_key")] public List<string>? CaptchaKey { get; set; }
    }
}

public sealed class LoginResult
{
    public bool NeedsMfa { get; private init; }
    public string? MfaTicket { get; private init; }
    public string? LoginInstanceId { get; private init; }
    public bool HasTotp { get; private init; }
    public bool HasSms { get; private init; }
    public bool HasBackup { get; private init; }
    public bool HasWebAuthn { get; private init; }
    public string? Token { get; private init; }

    public static LoginResult RequiresMfa(
        string ticket,
        string? loginInstanceId = null,
        bool totp = true,
        bool sms = false,
        bool backup = true,
        bool webauthn = false) =>
        new()
        {
            NeedsMfa = true,
            MfaTicket = ticket,
            LoginInstanceId = loginInstanceId,
            HasTotp = totp,
            HasSms = sms,
            HasBackup = backup,
            HasWebAuthn = webauthn
        };

    public static LoginResult Success(string token) =>
        new() { NeedsMfa = false, Token = token };
}

public sealed class DiscordAuthException : Exception
{
    public DiscordAuthException(string message) : base(message) { }
}
