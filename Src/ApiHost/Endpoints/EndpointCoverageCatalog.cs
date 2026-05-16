using RimBob.Coordination;

namespace RimBob.Host.Endpoints;

public sealed record EndpointCoverageRow(string Endpoint, string State, string Note);

public sealed record EndpointCoverageContext(
    AgendaStore AgendaStore,
    MinisterRegistry Ministers)
{
    public string CapabilityNames(Func<MinisterDescriptor, bool> capability)
    {
        string[] labels = Ministers.Ministers
            .Where(descriptor => descriptor.Ready && capability(descriptor))
            .Select(descriptor => descriptor.Label)
            .ToArray();

        return labels.Length == 0 ? "no wired ministers" : string.Join(" and ", labels);
    }
}

public sealed class EndpointCoverageCatalog
{
    private readonly object gate = new();
    private readonly List<EndpointCoverageEntry> entries = [];

    public void Register(string endpoint, string state, string note) =>
        Register(endpoint, _ => state, _ => note);

    public void Register(
        string endpoint,
        Func<EndpointCoverageContext, string> state,
        Func<EndpointCoverageContext, string> note)
    {
        lock (gate)
        {
            if (entries.Any(entry => entry.Endpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase)))
                return;

            entries.Add(new EndpointCoverageEntry(endpoint, state, note));
        }
    }

    public IReadOnlyList<EndpointCoverageRow> Snapshot(EndpointCoverageContext context)
    {
        EndpointCoverageEntry[] current;
        lock (gate)
        {
            current = entries.ToArray();
        }

        return current
            .Select(entry => new EndpointCoverageRow(entry.Endpoint, entry.State(context), entry.Note(context)))
            .ToArray();
    }

    private sealed record EndpointCoverageEntry(
        string Endpoint,
        Func<EndpointCoverageContext, string> State,
        Func<EndpointCoverageContext, string> Note);
}
