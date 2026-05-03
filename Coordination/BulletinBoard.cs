namespace RimAI.Coordination;

/// <summary>
/// The single inspectable surface for colony intent.
/// A console dump of the board should show every pawn's work traceable to
/// goal_id → minister → LaborRequest → assigned pawn.
///
/// Full lifecycle spec: design/communication.md
/// Full implementation: M3 slice.
/// </summary>
public sealed class BulletinBoard
{
    // Placeholder — implemented in M3.
    // Lifecycle: Open → Assigned → Completed | Deferred | Stuck
}
