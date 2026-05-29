namespace RimBob.State;

public interface IColonyStateRefresher
{
    Task RefreshAllAsync(CancellationToken ct = default);
}
