namespace RimAI.Core.Versioning;

/// <summary>
/// Wrapper around an aggregate value with a monotonic version number.
/// Version increments on every Update call; consumers (briefings) cache
/// against snapshotted versions and recompute when any input version changes.
/// See Docs/design/state-store.md.
/// </summary>
public sealed class Versioned<T>(T initial)
{
    public T    Value   { get; private set; } = initial;
    public long Version { get; private set; }

    public void Update(T newValue)
    {
        Value = newValue;
        Version++;
    }
}
