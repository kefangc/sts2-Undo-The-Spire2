using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace UndoTheSpire2;

// Reconciles monsters whose final intent depends on restored powers or
// transient stun moves that are not part of the canonical move-state machine.
internal interface IUndoCreatureReconciliationCodec
{
    string CodecId { get; }

    bool CanHandle(MonsterModel monster);

    void Reconcile(MonsterModel monster, UndoMonsterState? state);
}

internal static class UndoCreatureReconciliationCodecRegistry
{
    private static readonly IReadOnlyList<IUndoCreatureReconciliationCodec> Codecs =
    [
        new SlumberingBeetleReconciliationCodec(),
        new LagavulinMatriarchReconciliationCodec(),
        new CeremonialBeastReconciliationCodec(),
        new WrigglerReconciliationCodec(),
        new GenericTransientStunReconciliationCodec()
    ];

    public static HashSet<string> GetImplementedCodecIds()
    {
        return [.. Codecs.Select(static codec => codec.CodecId)];
    }

    public static RestoreCapabilityReport Restore(IReadOnlyList<UndoMonsterState> monsterStates, IReadOnlyList<Creature> creatures)
    {
        if (creatures.Count == 0)
            return RestoreCapabilityReport.SupportedReport();

        Dictionary<string, UndoMonsterState> statesByKey = monsterStates.ToDictionary(static state => state.CreatureKey);
        for (int i = 0; i < creatures.Count; i++)
        {
            Creature creature = creatures[i];
            MonsterModel? monster = creature.Monster;
            if (monster?.MoveStateMachine == null)
                continue;

            string creatureKey = UndoStableRefs.BuildCreatureKey(creature, i);
            statesByKey.TryGetValue(creatureKey, out UndoMonsterState? state);
            foreach (IUndoCreatureReconciliationCodec codec in Codecs)
            {
                if (!codec.CanHandle(monster))
                    continue;

                codec.Reconcile(monster, state);
            }
        }

        return RestoreCapabilityReport.SupportedReport();
    }

    private static bool TryRestoreTransientStunnedMove(MonsterModel monster, UndoMonsterState? state, Func<IReadOnlyList<Creature>, Task> stunMove, string? fallbackFollowUpId = null)
    {
        if (state?.NextMoveId != MonsterModel.stunnedMoveId)
            return false;

        monster.Creature.StunInternal(stunMove, state.TransientNextMoveFollowUpId ?? fallbackFollowUpId);
        return true;
    }

    private static bool TryRestoreTransientStunnedMove(MonsterModel monster, UndoMonsterState? state)
    {
        if (state?.NextMoveId != MonsterModel.stunnedMoveId)
            return false;

        monster.Creature.StunInternal(static _ => Task.CompletedTask, state.TransientNextMoveFollowUpId);
        return true;
    }

    private static bool TrySetMove(MonsterModel monster, string? moveId)
    {
        if (string.IsNullOrWhiteSpace(moveId) || monster.MoveStateMachine == null)
            return false;

        if (!monster.MoveStateMachine.States.TryGetValue(moveId, out MonsterState? nextState) || nextState is not MoveState moveState)
            return false;

        monster.SetMoveImmediate(moveState, true);
        return true;
    }

    private sealed class SlumberingBeetleReconciliationCodec : IUndoCreatureReconciliationCodec
    {
        public string CodecId => "reconcile:SlumberingBeetle.MoveIntent";

        public bool CanHandle(MonsterModel monster)
        {
            return monster is SlumberingBeetle;
        }

        public void Reconcile(MonsterModel monster, UndoMonsterState? state)
        {
            SlumberingBeetle beetle = (SlumberingBeetle)monster;
            if (TryRestoreTransientStunnedMove(beetle, state, beetle.WakeUpMove, "ROLL_OUT_MOVE"))
                return;

            if (beetle.Creature.HasPower<SlumberPower>())
            {
                TrySetMove(beetle, "SNORE_MOVE");
                return;
            }

            if (!beetle.IsAwake)
            {
                beetle.Creature.StunInternal(beetle.WakeUpMove, state?.TransientNextMoveFollowUpId ?? "ROLL_OUT_MOVE");
                return;
            }

            if (beetle.NextMove?.Id == "SNORE_MOVE" || state?.NextMoveId == "ROLL_OUT_MOVE")
                TrySetMove(beetle, "ROLL_OUT_MOVE");
        }
    }

    private sealed class LagavulinMatriarchReconciliationCodec : IUndoCreatureReconciliationCodec
    {
        public string CodecId => "reconcile:LagavulinMatriarch.MoveIntent";

        public bool CanHandle(MonsterModel monster)
        {
            return monster is LagavulinMatriarch;
        }

        public void Reconcile(MonsterModel monster, UndoMonsterState? state)
        {
            LagavulinMatriarch lagavulin = (LagavulinMatriarch)monster;
            if (TryRestoreTransientStunnedMove(lagavulin, state, lagavulin.WakeUpMove, "SLASH_MOVE"))
                return;

            if (lagavulin.Creature.HasPower<AsleepPower>())
            {
                TrySetMove(lagavulin, "SLEEP_MOVE");
                return;
            }

            if (!lagavulin.IsAwake)
            {
                lagavulin.Creature.StunInternal(lagavulin.WakeUpMove, state?.TransientNextMoveFollowUpId ?? "SLASH_MOVE");
                return;
            }

            if (lagavulin.NextMove?.Id == "SLEEP_MOVE")
                TrySetMove(lagavulin, "SLASH_MOVE");
        }
    }

    private sealed class CeremonialBeastReconciliationCodec : IUndoCreatureReconciliationCodec
    {
        public string CodecId => "reconcile:CeremonialBeast.TransientStun";

        public bool CanHandle(MonsterModel monster)
        {
            return monster is CeremonialBeast;
        }

        public void Reconcile(MonsterModel monster, UndoMonsterState? state)
        {
            CeremonialBeast beast = (CeremonialBeast)monster;
            TryRestoreTransientStunnedMove(beast, state, beast.StunnedMove, beast.BeastCryState?.StateId);
        }
    }

    private sealed class WrigglerReconciliationCodec : IUndoCreatureReconciliationCodec
    {
        public string CodecId => "reconcile:Wriggler.StartStunned";

        public bool CanHandle(MonsterModel monster)
        {
            return monster is Wriggler;
        }

        public void Reconcile(MonsterModel monster, UndoMonsterState? state)
        {
            Wriggler wriggler = (Wriggler)monster;
            if (wriggler.StartStunned && state?.NextMoveId == "SPAWNED_MOVE")
                TrySetMove(wriggler, "SPAWNED_MOVE");
        }
    }

    private sealed class GenericTransientStunReconciliationCodec : IUndoCreatureReconciliationCodec
    {
        public string CodecId => "reconcile:GenericTransientStun";

        public bool CanHandle(MonsterModel monster)
        {
            return monster is BowlbugRock or ThievingHopper or FatGremlin or SneakyGremlin or CeremonialBeast or Wriggler;
        }

        public void Reconcile(MonsterModel monster, UndoMonsterState? state)
        {
            TryRestoreTransientStunnedMove(monster, state);
        }
    }
}
