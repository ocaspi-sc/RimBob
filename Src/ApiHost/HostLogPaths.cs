namespace RimAI.Host;

internal static class HostLogPaths
{
    public static string ResolveLogsDirectory(string contentRootPath)
    {
        DirectoryInfo? current = new(contentRootPath);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Docs"))
                && Directory.Exists(Path.Combine(current.FullName, "Src")))
            {
                return Path.Combine(current.FullName, "logs");
            }

            current = current.Parent;
        }

        return Path.Combine(contentRootPath, "logs");
    }
}
