namespace RimBob.Host;

internal static class HostLogPaths
{
    public static string ResolveLogsDirectory(string contentRootPath, string? configuredPath)
    {
        return ResolveStablePath(contentRootPath, configuredPath, "logs");
    }

    public static string ResolveVarDirectory(string contentRootPath)
    {
        return Path.Combine(ResolveRuntimeRoot(contentRootPath), "var");
    }

    public static string ResolveIconCacheDirectory(string contentRootPath, string? configuredPath)
    {
        return ResolveStablePath(contentRootPath, configuredPath, "icons");
    }

    public static string ResolveDataRootDirectory(string contentRootPath, string? configuredPath)
    {
        return ResolveStablePath(contentRootPath, configuredPath, null);
    }

    public static string ResolveDataDirectory(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredPath,
        string defaultRelativePath)
    {
        string dataRoot = ResolveDataRootDirectory(contentRootPath, configuredDataRoot);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(dataRoot, configuredPath));
        }

        return Path.GetFullPath(Path.Combine(dataRoot, defaultRelativePath));
    }

    public static string ResolveRuntimeRoot(string contentRootPath)
    {
        DirectoryInfo? current = new(contentRootPath);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Docs"))
                && Directory.Exists(Path.Combine(current.FullName, "Src")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return contentRootPath;
    }

    private static string ResolveStableLocalRoot()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.GetFullPath(Path.Combine(localAppData, "RimBob"));
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.GetFullPath(Path.Combine(userProfile, ".local", "share", "RimBob"));
        }

        return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RimBob"));
    }

    private static string ResolveStablePath(
        string contentRootPath,
        string? configuredPath,
        string? defaultRelativePath)
    {
        string stableRoot = ResolveStableLocalRoot();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(stableRoot, configuredPath));
        }

        return string.IsNullOrWhiteSpace(defaultRelativePath)
            ? stableRoot
            : Path.GetFullPath(Path.Combine(stableRoot, defaultRelativePath));
    }
}
