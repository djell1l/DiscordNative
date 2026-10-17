using DiscordNative.Protocol.Models;

namespace DiscordNative.Protocol.Utils;

public static class DiscordPermissionEvaluator
{
    public const ulong Administrator = 1UL << 3;   // 8
    public const ulong ViewChannel = 1UL << 10;    // 1024 (0x400)
    public const ulong Connect = 1UL << 20;        // 1048576 (0x100000)

    public static (bool CanView, bool CanConnect) EvaluateChannelPermissions(
        DiscordChannel channel,
        DiscordGuild? guild,
        string? currentUserId,
        IEnumerable<string>? userRoles,
        IReadOnlyDictionary<string, DiscordChannel>? allGuildChannels = null)
    {
        // 1. DMs / Group DMs sont toujours visibles par leurs participants
        if (channel.Type is DiscordChannelType.Dm or DiscordChannelType.GroupDm)
        {
            return (true, true);
        }

        // 2. Propriétaire du serveur a toutes les permissions
        if (guild != null && !string.IsNullOrEmpty(guild.OwnerId) && guild.OwnerId == currentUserId)
        {
            return (true, true);
        }

        // 3. Si l'API Discord fournit directement le champ "permissions" calculé pour l'utilisateur
        if (!string.IsNullOrWhiteSpace(channel.Permissions) && ulong.TryParse(channel.Permissions, out ulong computedPerms))
        {
            if ((computedPerms & Administrator) != 0)
            {
                return (true, true);
            }
            bool canView = (computedPerms & ViewChannel) != 0;
            bool canConnect = (computedPerms & Connect) != 0;
            return (canView, canConnect);
        }

        // 4. Calcul manuel via les rôles et permission_overwrites
        var roleIdSet = new HashSet<string>(userRoles ?? Enumerable.Empty<string>());
        var roleDict = guild?.Roles?.ToDictionary(r => r.Id) ?? new Dictionary<string, DiscordRole>();

        // Vérifier si l'utilisateur possède le rôle Administrateur
        foreach (var rId in roleIdSet)
        {
            if (roleDict.TryGetValue(rId, out var role) && (role.PermissionBits & Administrator) != 0)
            {
                return (true, true);
            }
        }

        // Permission de base : rôle @everyone
        string guildId = guild?.Id ?? channel.GuildId ?? string.Empty;
        ulong basePerms = 0;
        if (!string.IsNullOrEmpty(guildId) && roleDict.TryGetValue(guildId, out var everyoneRole))
        {
            basePerms = everyoneRole.PermissionBits;
        }
        else
        {
            // Par défaut dans Discord, @everyone a VIEW_CHANNEL et CONNECT
            basePerms = ViewChannel | Connect;
        }

        // Ajouter les permissions de base des autres rôles du membre
        foreach (var rId in roleIdSet)
        {
            if (rId != guildId && roleDict.TryGetValue(rId, out var role))
            {
                basePerms |= role.PermissionBits;
            }
        }

        if ((basePerms & Administrator) != 0)
        {
            return (true, true);
        }

        // Si le salon a une catégorie parente, appliquer d'abord les overwrites de la catégorie
        if (!string.IsNullOrEmpty(channel.ParentId) && allGuildChannels != null && allGuildChannels.TryGetValue(channel.ParentId, out var parentCat))
        {
            basePerms = ApplyOverwrites(basePerms, parentCat.PermissionOverwrites, guildId, roleIdSet, currentUserId);
        }

        // Appliquer les overwrites spécifiques au salon
        basePerms = ApplyOverwrites(basePerms, channel.PermissionOverwrites, guildId, roleIdSet, currentUserId);

        bool finalCanView = (basePerms & ViewChannel) != 0;
        bool finalCanConnect = (basePerms & Connect) != 0;
        return (finalCanView, finalCanConnect);
    }

    private static ulong ApplyOverwrites(
        ulong perms,
        List<DiscordPermissionOverwrite>? overwrites,
        string guildId,
        HashSet<string> userRoles,
        string? currentUserId)
    {
        if (overwrites == null || overwrites.Count == 0) return perms;

        // a. Overwrite pour @everyone (id == guildId)
        var everyoneOw = overwrites.FirstOrDefault(o => o.Id == guildId);
        if (everyoneOw != null)
        {
            perms = (perms & ~everyoneOw.DenyBits) | everyoneOw.AllowBits;
        }

        // b. Overwrites pour les rôles du membre
        ulong rolesDeny = 0;
        ulong rolesAllow = 0;
        foreach (var ow in overwrites)
        {
            if (ow.Type == 0 && ow.Id != guildId && userRoles.Contains(ow.Id))
            {
                rolesDeny |= ow.DenyBits;
                rolesAllow |= ow.AllowBits;
            }
        }
        perms = (perms & ~rolesDeny) | rolesAllow;

        // c. Overwrite spécifique au membre (type == 1 && id == currentUserId)
        if (!string.IsNullOrEmpty(currentUserId))
        {
            var memberOw = overwrites.FirstOrDefault(o => o.Type == 1 && o.Id == currentUserId);
            if (memberOw != null)
            {
                perms = (perms & ~memberOw.DenyBits) | memberOw.AllowBits;
            }
        }

        return perms;
    }
}
