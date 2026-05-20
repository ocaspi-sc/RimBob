using System.Reflection;
using System.Text.RegularExpressions;

namespace RimBob.Host;

public sealed class HostRuntimeIdentity
{
    private const string BuildNumberMetadataKey = "RimBobBuildNumber";
    private const string BuildDateTimeMetadataKey = "RimBobBuildDateTime";

    private static readonly Regex DashboardAssetPattern = new(
        "assets/(?<asset>[^\"']+\\.(?:js|css))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public HostRuntimeIdentity(string contentRoot)
    {
        Assembly assembly = typeof(HostRuntimeIdentity).Assembly;
        Version? assemblyVersion = assembly.GetName().Version;

        StartedAt = DateTimeOffset.UtcNow;
        InstanceId = Guid.NewGuid().ToString("N");
        RimBobVersion = assemblyVersion is null ? "unknown" : assemblyVersion.ToString(3);
        BuildVersion = assemblyVersion?.ToString() ?? "unknown";
        BuildNumber = ResolveAssemblyMetadata(assembly, BuildNumberMetadataKey) ?? "unknown";
        RunningVersion = BuildNumber == "unknown" ? RimBobVersion : BuildNumber;
        BuildDateTime = ResolveAssemblyMetadata(assembly, BuildDateTimeMetadataKey) ?? "unknown";
        BuildInformationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? BuildVersion;
        BuildRevision = ExtractBuildRevision(BuildInformationalVersion);
        BuildRevisionShort = BuildRevision is null
            ? null
            : BuildRevision[..Math.Min(8, BuildRevision.Length)];
        DashboardAssetVersion = ResolveDashboardAssetVersion(contentRoot);
        ReloadToken = $"{BuildInformationalVersion}|{BuildNumber}|{BuildDateTime}|{DashboardAssetVersion}";
    }

    public DateTimeOffset StartedAt { get; }
    public string InstanceId { get; }
    public string RimBobVersion { get; }
    public string RunningVersion { get; }
    public string BuildNumber { get; }
    public string BuildDateTime { get; }
    public string BuildVersion { get; }
    public string BuildInformationalVersion { get; }
    public string? BuildRevision { get; }
    public string? BuildRevisionShort { get; }
    public string DashboardAssetVersion { get; }
    public string ReloadToken { get; }

    private static string? ExtractBuildRevision(string informationalVersion)
    {
        int marker = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (marker < 0 || marker == informationalVersion.Length - 1) return null;

        return informationalVersion[(marker + 1)..];
    }

    private static string? ResolveAssemblyMetadata(Assembly assembly, string key) =>
        assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)
            ?.Value;

    private static string ResolveDashboardAssetVersion(string contentRoot)
    {
        string indexPath = Path.Combine(contentRoot, "wwwroot", "index.html");
        if (!File.Exists(indexPath)) return "missing";

        string html = File.ReadAllText(indexPath);
        MatchCollection matches = DashboardAssetPattern.Matches(html);
        List<string> assets = [];
        foreach (Match match in matches)
        {
            string asset = match.Groups["asset"].Value.Trim();
            if (asset.Length > 0) assets.Add(asset);
        }

        if (assets.Count == 0) return "unknown";

        return string.Join("|", assets.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }
}
