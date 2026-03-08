using System.Collections.Generic;

namespace UndoTheSpire2;

internal sealed class UndoMonsterState
{
    public required string CreatureKey { get; init; }

    public string? NextMoveId { get; init; }

    public string? CurrentStateId { get; init; }

    public bool SpawnedThisTurn { get; init; }

    public bool PerformedFirstMove { get; init; }

    public bool NextMovePerformedAtLeastOnce { get; init; }

    public IReadOnlyList<string> StateLogIds { get; init; } = [];
}
