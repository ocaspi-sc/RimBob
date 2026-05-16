namespace RimBob.Host;

internal static class HostLogPaths
{
    public static string ResolveLogsDirectory(string contentRootPath)
    {
        return Path.Combine(ResolveRuntimeRoot(contentRootPath), "logs");
    }

    public static string ResolveVarDirectory(string contentRootPath)
    {
        return Path.Combine(ResolveRuntimeRoot(contentRootPath), "var");
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
}
