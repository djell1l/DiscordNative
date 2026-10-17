using System.Text.Json;
using Windows.Security.Credentials;
using Windows.Storage;

namespace DiscordNative.App.Services;

public sealed class AppSettings
{
    /// <summary>Non sérialisé sur disque : rempli à la volée depuis PasswordVault par SettingsService.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SavedToken { get; set; }

    public string ThemeMode { get; set; } = "Default"; // Default | Light | Dark
    public string Language { get; set; } = "Default"; // Default (Système) | fr-FR | en-US | de-DE | es-ES | it-IT | ja-JP | pt-BR | ru-RU
    public string AccentColor { get; set; } = "#5865F2"; // Blurple par défaut
    public string? BackgroundImagePath { get; set; }
    public double BackgroundOpacity { get; set; } = 0.35;
    public double BackgroundBlurRadius { get; set; } = 12.0;
    public bool CompactMode { get; set; } = false;
    public bool BubbleMessageStyle { get; set; } = false;
    public double InterfaceScale { get; set; } = 1.0;
    public bool AnonymizeUploads { get; set; } = false;
    public bool ConvertLongMessagesToFile { get; set; } = true;
    public string UpdateChannel { get; set; } = "Stable"; // Stable | Experimental
    public bool ShowNotifications { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool EnableKrispNoiseSuppression { get; set; } = true;
    public Dictionary<string, string> UserNotes { get; set; } = new();
}

/// <summary>
/// Persistance locale des préférences utilisateur (thème, fond d'écran custom façon Files,
/// token sauvegardé). Stocké en JSON dans ApplicationData.LocalFolder — pas de LocalSettings
/// binaire pour rester simple à inspecter/déboguer.
/// </summary>
public sealed class SettingsService
{
    private const string FileName = "settings.json";
    private const string VaultResource = "DiscordNative";
    private const string VaultUserName = "DiscordToken";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly PasswordVault Vault = new();

    public AppSettings Current { get; private set; }

    public event Action? SettingsChanged;

    public SettingsService()
    {
        Current = Load();
        Current.SavedToken = TryReadTokenFromVault();
    }

    /// <summary>
    /// Le token est stocké séparément via Windows Credential Manager (PasswordVault),
    /// jamais en clair dans settings.json, pour limiter l'exposition en cas de fuite du fichier.
    /// </summary>
    private static string? TryReadTokenFromVault()
    {
        try
        {
            var credential = Vault.Retrieve(VaultResource, VaultUserName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception)
        {
            return null; // Rien de stocké encore, ou vault indisponible.
        }
    }

    public void SaveToken(string token)
    {
        Current.SavedToken = token;
        try
        {
            // On retire l'éventuelle entrée précédente avant d'écrire, PasswordVault n'autorisant
            // pas la mise à jour en place pour un couple (resource, username) déjà existant.
            var existing = Vault.FindAllByResource(VaultResource).FirstOrDefault(c => c.UserName == VaultUserName);
            if (existing is not null) Vault.Remove(existing);
        }
        catch { /* rien à supprimer */ }

        Vault.Add(new PasswordCredential(VaultResource, VaultUserName, token));
    }

    public static string GetSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = System.IO.Path.Combine(localAppData, "DiscordNative");
        System.IO.Directory.CreateDirectory(appFolder);
        return System.IO.Path.Combine(appFolder, FileName);
    }

    private static AppSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch { }
        
        return new AppSettings { ThemeMode = "Default", Language = "Default" };
    }

    public void Save()
    {
        try
        {
            var path = GetSettingsPath();
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            System.IO.File.WriteAllText(path, json);
            SettingsChanged?.Invoke();
        }
        catch { }
    }

    public void ExportSettings(string targetFilePath)
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        System.IO.File.WriteAllText(targetFilePath, json);
    }

    public void ImportSettings(string sourceFilePath)
    {
        var json = System.IO.File.ReadAllText(sourceFilePath);
        var imported = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        if (imported is not null)
        {
            imported.SavedToken = Current.SavedToken;
            Current = imported;
            Save();
        }
    }

    public async Task<string> ImportBackgroundImageAsync(StorageFile sourceFile)
    {
        var destFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordNative", "Backgrounds");
        System.IO.Directory.CreateDirectory(destFolder);
        var destPath = System.IO.Path.Combine(destFolder, sourceFile.Name);
        System.IO.File.Copy(sourceFile.Path, destPath, true);
        Current.BackgroundImagePath = destPath;
        Save();
        return destPath;
    }

    public void ClearBackgroundImage()
    {
        Current.BackgroundImagePath = null;
        Save();
    }

    public void ClearToken()
    {
        Current.SavedToken = null;
        try
        {
            var existing = Vault.FindAllByResource(VaultResource).FirstOrDefault(c => c.UserName == VaultUserName);
            if (existing is not null) Vault.Remove(existing);
        }
        catch { /* déjà absent */ }
    }
}

