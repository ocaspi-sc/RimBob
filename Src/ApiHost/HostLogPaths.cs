namespace RimBob.Host;

internal static class HostLogPaths
{
    public static string ResolveLogsDirectory(string contentRootPath, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(ResolveStableLocalRoot(contentRootPath), configuredPath));
        }

        return Path.Combine(ResolveStableLocalRoot(contentRootPath), "logs");
    }

    public static string ResolveVarDirectory(string contentRootPath)
    {
        return Path.Combine(ResolveRuntimeRoot(contentRootPath), "var");
    }

    public static string ResolveIconCacheDirectory(string contentRootPath, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(ResolveStableLocalRoot(contentRootPath), configuredPath));
        }

        return Path.Combine(ResolveStableLocalRoot(contentRootPath), "icons");
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

    private static string ResolveStableLocalRoot(string contentRootPath)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "RimBob");
        }

        return ResolveVarDirectory(contentRootPath);
    }
}
