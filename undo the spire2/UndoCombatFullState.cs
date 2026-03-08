using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace UndoTheSpire2;

internal sealed class UndoCombatFullState
{
    public UndoCombatFullState(
        NetFullCombatState fullState,
        int roundNumber,
        CombatSide currentSide,
        ActionSynchronizerCombatState synchronizerCombatState,
        uint nextActionId,
        uint nextHookId,
        uint nextChecksumId,
        IReadOnlyList<UndoMonsterState> monsterStates,
        IReadOnlyList<UndoPlayerPileCardCostState> cardCostStates)
    {
        FullState = fullState;
        RoundNumber = roundNumber;
        CurrentSide = currentSide;
        SynchronizerCombatState = synchronizerCombatState;
        NextActionId = nextActionId;
        NextHookId = nextHookId;
        NextChecksumId = nextChecksumId;
        MonsterStates = monsterStates;
        CardCostStates = cardCostStates;
    }

    public NetFullCombatState FullState { get; }

    public int RoundNumber { get; }

    public CombatSide CurrentSide { get; }

    public ActionSynchronizerCombatState SynchronizerCombatState { get; }

    public uint NextActionId { get; }

    public uint NextHookId { get; }

    public uint NextChecksumId { get; }

    public IReadOnlyList<UndoMonsterState> MonsterStates { get; }

    public IReadOnlyList<UndoPlayerPileCardCostState> CardCostStates { get; }
}

internal sealed class UndoPlayerPileCardCostState
{
    public required ulong PlayerNetId { get; init; }

    public required PileType PileType { get; init; }

    public required IReadOnlyList<UndoCardCostState> Cards { get; init; }
}

