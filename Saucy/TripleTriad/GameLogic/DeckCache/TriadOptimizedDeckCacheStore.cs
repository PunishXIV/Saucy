using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Saucy.TripleTriad.GameLogic;

internal static class TriadOptimizedDeckCacheStore
{
    public const int SchemaVersion = 2;
    public const int RebuildAfterNewCardCount = 5;

    private const string CacheFileName = "OptimizedDeckCache.json";
    private const string LegacyCacheFolderName = "OptimizedDeckCache";

    private static readonly object FileLock = new();

    private static ulong activeContentId;
    private static TriadOptimizedDeckCacheFile? activeFile;
    private static bool loadedForCharacter;

    public static void TickCharacter()
    {
        if (!Svc.ClientState.IsLoggedIn)
        {
            ResetActive();
            return;
        }

        var contentId = GetLocalContentId();
        if (contentId == 0)
        {
            ResetActive();
            return;
        }

        if (!loadedForCharacter || contentId != activeContentId)
        {
            LoadForCharacter(contentId);
        }
    }

    public static bool TryGetEntry(string sessionKey, out TriadOptimizedDeckCacheEntry? entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(sessionKey))
        {
            return false;
        }

        EnsureLoaded();
        return activeFile != null &&
               activeFile.Entries.TryGetValue(sessionKey, out entry);
    }

    public static bool TryGetRegionalMods(int npcId, out List<TriadGameModifier> regionMods)
    {
        regionMods = [];
        if (npcId < 0)
        {
            return false;
        }

        EnsureLoaded();
        if (activeFile?.RegionalRuleSignaturesByNpcId == null ||
            !activeFile.RegionalRuleSignaturesByNpcId.TryGetValue(npcId, out var signatures) ||
            signatures is not { Length: > 0 })
        {
            return false;
        }

        regionMods = TriadOptimizerSessionKey.RegionModsFromSignatures(signatures);
        return regionMods.Count > 0;
    }

    public static void UpsertRegionalMods(int npcId, IReadOnlyList<TriadGameModifier> regionMods)
    {
        if (npcId < 0)
        {
            return;
        }

        EnsureLoaded();
        activeFile ??= new();
        activeFile.Version = SchemaVersion;
        activeFile.RegionalRuleSignaturesByNpcId ??= new();

        if (regionMods == null || regionMods.Count == 0)
        {
            if (activeFile.RegionalRuleSignaturesByNpcId.Remove(npcId))
            {
                SaveActive();
            }

            return;
        }

        var signatures = TriadOptimizerSessionKey.GetModSignatures(regionMods);
        if (signatures.Length == 0)
        {
            return;
        }

        if (activeFile.RegionalRuleSignaturesByNpcId.TryGetValue(npcId, out var existing) &&
            existing.SequenceEqual(signatures, StringComparer.Ordinal))
        {
            return;
        }

        activeFile.RegionalRuleSignaturesByNpcId[npcId] = signatures;
        SaveActive();
    }

    public static bool HasAnyEntryForNpc(int npcId)
    {
        if (npcId < 0)
        {
            return false;
        }

        EnsureLoaded();
        if (activeFile == null)
        {
            return false;
        }

        foreach (var entry in activeFile.Entries.Values)
        {
            if (entry.NpcId == npcId)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetOwnedSnapshotForNpc(int npcId, string sessionKey, out int[] ownedAtBuild)
    {
        ownedAtBuild = [];
        EnsureLoaded();
        if (activeFile == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(sessionKey) &&
            activeFile.Entries.TryGetValue(sessionKey, out var sessionEntry) &&
            HasOwnedSnapshot(sessionEntry))
        {
            ownedAtBuild = sessionEntry.OwnedCardIdsAtBuild;
            return true;
        }

        TriadOptimizedDeckCacheEntry? latest = null;
        foreach (var entry in activeFile.Entries.Values)
        {
            if (entry.NpcId != npcId || !HasOwnedSnapshot(entry))
            {
                continue;
            }

            if (latest == null || entry.BuiltUtcTicks > latest.BuiltUtcTicks)
            {
                latest = entry;
            }
        }

        if (latest == null)
        {
            return false;
        }

        ownedAtBuild = latest.OwnedCardIdsAtBuild;
        return true;
    }

    private static bool HasOwnedSnapshot(TriadOptimizedDeckCacheEntry entry) =>
        entry?.OwnedCardIdsAtBuild is { Length: > 0 };

    public static IReadOnlyList<TriadOptimizedDeckCacheCharacterView> GetCharacterCacheViews()
    {
        lock (FileLock)
        {
            EnsureLoaded();
            var currentContentId = GetLocalContentId();
            var views = new List<TriadOptimizedDeckCacheCharacterView>();
            var configsRoot = GetPluginConfigsRoot();

            if (Directory.Exists(configsRoot))
            {
                foreach (var charDir in Directory.EnumerateDirectories(configsRoot, "CHAR_*"))
                {
                    var folderName = Path.GetFileName(charDir);
                    if (!TryParseContentIdFromFolder(folderName, out var contentId))
                    {
                        continue;
                    }

                    var cachePath = Path.Combine(charDir, Svc.PluginInterface.InternalName, CacheFileName);
                    if (!TryLoadCacheFile(cachePath, out var file))
                    {
                        continue;
                    }

                    var cacheFile = contentId == currentContentId && activeFile != null && loadedForCharacter
                        ? activeFile
                        : file;
                    if (cacheFile == null)
                    {
                        continue;
                    }

                    views.Add(BuildCharacterView(contentId, cacheFile, contentId == currentContentId));
                }
            }

            if (currentContentId != 0 &&
                loadedForCharacter &&
                activeFile != null &&
                views.All(v => v.ContentId != currentContentId))
            {
                views.Add(BuildCharacterView(currentContentId, activeFile, true));
            }

            return
            [
                .. views
                    .OrderByDescending(v => v.IsCurrentCharacter)
                    .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            ];
        }
    }

    public static void Upsert(TriadOptimizedDeckCacheEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.SessionKey))
        {
            return;
        }

        EnsureLoaded();
        activeFile ??= new();
        activeFile.Version = SchemaVersion;
        PruneOtherEntriesForNpc(entry.NpcId, entry.SessionKey);
        activeFile.Entries[entry.SessionKey] = entry;
        SaveActive();
    }

    private static void PruneOtherEntriesForNpc(int npcId, string keepSessionKey)
    {
        if (activeFile == null || activeFile.Entries.Count == 0)
        {
            return;
        }

        var staleKeys = activeFile.Entries
            .Where(kvp => kvp.Value.NpcId == npcId &&
                          !string.Equals(kvp.Key, keepSessionKey, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            activeFile.Entries.Remove(key);
        }
    }

    public static bool TryUpdateEstWinChance(string sessionKey, float estWinChance)
    {
        if (string.IsNullOrEmpty(sessionKey) || estWinChance <= 0f)
        {
            return false;
        }

        EnsureLoaded();
        if (activeFile == null || !activeFile.Entries.TryGetValue(sessionKey, out var entry))
        {
            return false;
        }

        entry.EstWinChance = estWinChance;
        SaveActive();
        return true;
    }

    public static void Remove(string sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey))
        {
            return;
        }

        EnsureLoaded();
        if (activeFile?.Entries.Remove(sessionKey) == true)
        {
            SaveActive();
        }
    }

    public static void RemoveAllForNpc(int npcId)
    {
        EnsureLoaded();
        if (activeFile == null || activeFile.Entries.Count == 0)
        {
            return;
        }

        var staleKeys = activeFile.Entries
            .Where(kvp => kvp.Value.NpcId == npcId)
            .Select(kvp => kvp.Key)
            .ToList();

        if (staleKeys.Count == 0)
        {
            return;
        }

        foreach (var key in staleKeys)
        {
            activeFile.Entries.Remove(key);
        }

        SaveActive();
    }

    public static void ClearActiveCharacter()
    {
        lock (FileLock)
        {
            if (activeContentId == 0)
            {
                return;
            }

            activeFile = new();
            loadedForCharacter = true;

            try
            {
                var path = GetCachePath(activeContentId);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "[Saucy] Failed to delete optimized deck cache file.");
            }
        }
    }

    private static void EnsureLoaded()
    {
        if (!Svc.ClientState.IsLoggedIn)
        {
            return;
        }

        var contentId = GetLocalContentId();
        if (contentId == 0)
        {
            return;
        }

        if (!loadedForCharacter || contentId != activeContentId)
        {
            LoadForCharacter(contentId);
        }
    }

    private static ulong GetLocalContentId()
    {
        if (!Svc.ClientState.IsLoggedIn || !Svc.PlayerState.IsLoaded)
        {
            return 0;
        }

        return Svc.PlayerState.ContentId;
    }

    private static void LoadForCharacter(ulong contentId)
    {
        lock (FileLock)
        {
            activeContentId = contentId;
            loadedForCharacter = true;

            var path = GetCachePath(contentId);
            if (!File.Exists(path))
            {
                TryMigrateLegacyCache(contentId, path);
            }

            if (!File.Exists(path))
            {
                activeFile = new();
                activeFile.RegionalRuleSignaturesByNpcId = new();
                ImportLegacyBuildTimestampsLocked();
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                activeFile = JsonConvert.DeserializeObject<TriadOptimizedDeckCacheFile>(json) ??
                             new TriadOptimizedDeckCacheFile();
                if (activeFile.Version != SchemaVersion)
                {
                    if (activeFile.Version == 1)
                    {
                        activeFile.Version = SchemaVersion;
                        activeFile.RegionalRuleSignaturesByNpcId ??= new();
                    }
                    else
                    {
                        activeFile = new();
                    }
                }

                activeFile.RegionalRuleSignaturesByNpcId ??= new();
                ImportLegacyBuildTimestampsLocked();
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "[Saucy] Failed to load optimized deck cache; starting empty.");
                activeFile = new();
            }
        }
    }

    private static void ImportLegacyBuildTimestampsLocked()
    {
        if (activeFile == null || C.TriadOptimizedDeckBuiltUtcTicksByNpcId.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var entry in activeFile.Entries.Values)
        {
            if (entry.BuiltUtcTicks > 0)
            {
                continue;
            }

            if (!C.TriadOptimizedDeckBuiltUtcTicksByNpcId.TryGetValue(entry.NpcId, out var ticks))
            {
                continue;
            }

            entry.BuiltUtcTicks = ticks;
            changed = true;
        }

        if (changed)
        {
            SaveActive();
        }
    }

    private static void SaveActive()
    {
        if (!loadedForCharacter || activeFile == null)
        {
            return;
        }

        lock (FileLock)
        {
            try
            {
                StampCharacterMetadata(activeFile);
                var path = GetCachePath(activeContentId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonConvert.SerializeObject(activeFile, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "[Saucy] Failed to save optimized deck cache.");
            }
        }
    }

    private static void ResetActive()
    {
        loadedForCharacter = false;
        activeFile = null;
        activeContentId = 0;
    }

    private static string GetCachePath(ulong contentId)
    {
        var charDir = GetCharacterConfigDirectory(contentId);
        return Path.Combine(charDir, CacheFileName);
    }

    private static string GetPluginConfigsRoot()
    {
        var pluginConfigDir = Svc.PluginInterface.GetPluginConfigDirectory();
        return Directory.GetParent(pluginConfigDir)?.FullName ?? pluginConfigDir;
    }

    private static string GetCharacterConfigDirectory(ulong contentId) =>
        Path.Combine(GetPluginConfigsRoot(), $"CHAR_{contentId}", Svc.PluginInterface.InternalName);

    private static bool TryParseContentIdFromFolder(string folderName, out ulong contentId)
    {
        contentId = 0;
        const string prefix = "CHAR_";
        if (!folderName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return ulong.TryParse(folderName[prefix.Length..], out contentId);
    }

    private static bool TryLoadCacheFile(string path, out TriadOptimizedDeckCacheFile? file)
    {
        file = null;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            file = JsonConvert.DeserializeObject<TriadOptimizedDeckCacheFile>(json);
            if (file == null || file.Version is not (1 or 2))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[Saucy] Failed to read optimized deck cache at {Path}.", path);
            return false;
        }
    }

    private static TriadOptimizedDeckCacheCharacterView BuildCharacterView(
        ulong contentId,
        TriadOptimizedDeckCacheFile file,
        bool isCurrentCharacter)
        => new()
        {
            ContentId = contentId,
            DisplayName = ResolveCharacterDisplayName(contentId, file, isCurrentCharacter),
            IsCurrentCharacter = isCurrentCharacter,
            Entries =
            [
                .. file.Entries.Values
                    .OrderByDescending(e => e.BuiltUtcTicks)
                    .ThenBy(e => e.NpcName, StringComparer.OrdinalIgnoreCase)
            ]
        };

    private static void StampCharacterMetadata(TriadOptimizedDeckCacheFile file)
    {
        if (!Svc.PlayerState.IsLoaded || activeContentId == 0 || Svc.PlayerState.ContentId != activeContentId)
        {
            return;
        }

        file.ContentId = activeContentId;
        file.CharacterName = Svc.PlayerState.CharacterName;
        file.HomeWorldRowId = Svc.PlayerState.HomeWorld.RowId;
    }

    private static string ResolveCharacterDisplayName(
        ulong contentId,
        TriadOptimizedDeckCacheFile file,
        bool isCurrentCharacter)
    {
        if (!string.IsNullOrWhiteSpace(file.CharacterName))
        {
            var worldName = ResolveWorldName(file.HomeWorldRowId);
            return string.IsNullOrEmpty(worldName)
                ? file.CharacterName
                : $"{file.CharacterName} @ {worldName}";
        }

        if (isCurrentCharacter && Svc.PlayerState.IsLoaded && Svc.PlayerState.ContentId == contentId)
        {
            var worldName = Svc.PlayerState.HomeWorld.ValueNullable?.Name.ToString();
            return string.IsNullOrEmpty(worldName)
                ? Svc.PlayerState.CharacterName
                : $"{Svc.PlayerState.CharacterName} @ {worldName}";
        }

        return $"Character {contentId}";
    }

    private static string ResolveWorldName(uint homeWorldRowId)
    {
        if (homeWorldRowId == 0)
        {
            return string.Empty;
        }

        var world = Svc.Data.GetExcelSheet<World>()?.GetRow(homeWorldRowId);
        return world?.Name.ToString() ?? string.Empty;
    }

    private static void TryMigrateLegacyCache(ulong contentId, string newPath)
    {
        var pluginDir = Svc.PluginInterface.GetPluginConfigDirectory();
        var legacyPath = Path.Combine(pluginDir, LegacyCacheFolderName, $"{contentId}.json");
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            File.Move(legacyPath, newPath);
            Svc.Log.Info($"[Saucy] Migrated optimized deck cache to CHAR_{contentId} layout.");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[Saucy] Failed to migrate legacy optimized deck cache.");
        }
    }
}
