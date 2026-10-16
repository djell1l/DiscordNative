using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordNative.App.Services;

public sealed class UpdateAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}

public sealed class UpdateInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<UpdateAsset> Assets { get; set; } = new();
}

public static class UpdateService
{
    private const string RepoOwner = "djell1l";
    private const string RepoName = "Diswin";
    private static readonly HttpClient Http = new();

    public static async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Diswin", "1.0"));

            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize<List<UpdateInfo>>(json);
            if (releases is null || releases.Count == 0) return null;

            var includeExp = string.Equals(App.Settings.Current.UpdateChannel, "Experimental", StringComparison.OrdinalIgnoreCase);
            var latest = releases.FirstOrDefault(r => includeExp || !r.Prerelease) ?? releases[0];
            var currentVersion = "v0.3.1";

            if (latest.TagName != currentVersion && !latest.TagName.Equals(currentVersion + "-exp", StringComparison.OrdinalIgnoreCase))
            {
                return latest;
            }
        }
        catch { }

        return null;
    }

    public static async Task ApplyAutomaticUpdateAsync(UpdateInfo update, Action<string> onProgress)
    {
        var asset = update.Assets.FirstOrDefault(a => a.Name.EndsWith("-portable.zip", StringComparison.OrdinalIgnoreCase))
                 ?? update.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            Process.Start(new ProcessStartInfo(update.HtmlUrl) { UseShellExecute = true });
            return;
        }

        onProgress("Téléchargement de la mise à jour...");
        var tempZip = Path.Combine(Path.GetTempPath(), $"DiscordNative_Update_{Guid.NewGuid():N}.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), $"DiscordNative_Update_{Guid.NewGuid():N}");

        try
        {
            using (var s = await Http.GetStreamAsync(asset.BrowserDownloadUrl))
            using (var fs = new FileStream(tempZip, FileMode.Create))
            {
                await s.CopyToAsync(fs);
            }

            onProgress("Extraction des fichiers...");
            ZipFile.ExtractToDirectory(tempZip, tempExtract, overwriteFiles: true);

            var currentAppDir = AppDomain.CurrentDomain.BaseDirectory;
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(currentAppDir, "DiscordNative.App.exe");

            onProgress("Application de la mise à jour et redémarrage...");

            // Script de remplacement autonome et relance automatique façon Discord
            var scriptPath = Path.Combine(Path.GetTempPath(), "discordnative_updater.ps1");
            var scriptContent = $@"
Start-Sleep -Seconds 1
Get-Process -Name 'DiscordNative.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Copy-Item -Path '{tempExtract}\*' -Destination '{currentAppDir}' -Recurse -Force
Start-Process -FilePath '{currentExe}'
Remove-Item '{tempZip}' -Force -ErrorAction SilentlyContinue
Remove-Item '{tempExtract}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item '{scriptPath}' -Force -ErrorAction SilentlyContinue
";
            File.WriteAllText(scriptPath, scriptContent);

            var psi = new ProcessStartInfo("powershell", $"-WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"")
            {
                UseShellExecute = true,
                CreateNoWindow = true,
            };
            Process.Start(psi);
            Environment.Exit(0);
        }
        catch (Exception)
        {
            // En cas d'échec du téléchargement direct, ouvrir le lien de release
            Process.Start(new ProcessStartInfo(update.HtmlUrl) { UseShellExecute = true });
        }
    }
}
