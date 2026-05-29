using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Saucy.IPC;

internal static class DalamudRepoHelper
{
    private static HashSet<string>? _cachedUrls;
    private static DateTime _cachedUtc;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    public static bool IsRepositoryAdded(string repositoryUrl)
    {
        RefreshCacheIfNeeded();
        return _cachedUrls!.Contains(NormalizeUrl(repositoryUrl));
    }

    private static void RefreshCacheIfNeeded()
    {
        if (_cachedUrls != null && DateTime.UtcNow - _cachedUtc < CacheDuration)
            return;

        _cachedUrls = LoadThirdPartyRepoUrls();
        _cachedUtc = DateTime.UtcNow;
    }

    private static HashSet<string> LoadThirdPartyRepoUrls()
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher",
                "dalamudConfig.json");

            if (!File.Exists(configPath))
                return [];

            var config = JsonConvert.DeserializeObject<DalamudConfigFile>(File.ReadAllText(configPath));
            if (config?.ThirdRepoList == null)
                return [];

            return config.ThirdRepoList
                .Where(repo => repo.IsEnabled && !string.IsNullOrWhiteSpace(repo.Url))
                .Select(repo => NormalizeUrl(repo.Url!))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.ToLowerInvariant();

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Path = uri.AbsolutePath.TrimEnd('/'),
        };

        return builder.Uri.ToString().TrimEnd('/').ToLowerInvariant();
    }

    private sealed class DalamudConfigFile
    {
        public List<ThirdPartyRepoEntry>? ThirdRepoList { get; set; }
    }

    private sealed class ThirdPartyRepoEntry
    {
        public string? Url { get; set; }

        public bool IsEnabled { get; set; } = true;
    }
}
