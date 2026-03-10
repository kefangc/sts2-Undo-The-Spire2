using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace UndoTheSpire2;

// Monster topology covers linked-monster runtime and pet ownership that the
// official full combat snapshot does not preserve.
internal static class UndoCreatureTopologyCodecRegistry
{
    public static HashSet<string> GetImplementedCodecIds()
    {
        return
        [
            "topology:DoorAndDoormaker",
            "topology:Decimillipede",
            "topology:TestSubject",
            "topology:InfestedPrism"
        ];
    }

    public static IReadOnlyList<CreatureTopologyState> Capture(IReadOnlyList<Creature> creatures)
    {
        UndoCreatureTopologyCaptureContext context = new()
        {
            Creatures = creatures
        };

        List<CreatureTopologyState> states = [];
        for (int i = 0; i < creatures.Count; i++)
        {
            Creature creature = creatures[i];
            MonsterModel? monster = creature.Monster;
            if (monster == null)
                continue;

            states.Add(CaptureCreatureTopologyState(monster, creatures, i, context));
        }

        return states;
    }

    public static RestoreCapabilityReport Restore(IReadOnlyList<CreatureTopologyState> states, IReadOnlyList<Creature> creatures)
    {
        if (states.Count == 0)
            return RestoreCapabilityReport.SupportedReport();

        Dictionary<string, Creature> creaturesByKey = UndoStableRefs.BuildCreatureKeyMap(creatures);
        UndoCreatureTopologyRestoreContext context = new()
        {
            Creatures = creatures
        };

        foreach (CreatureTopologyState state in states)
        {
            if (state.CreatureRef == null || !creaturesByKey.TryGetValue(state.CreatureRef.Key, out Creature? creature) || creature.Monster == null)
            {
                return new RestoreCapabilityReport
                {
                    Result = RestoreCapabilityResult.TopologyMismatch,
                    Detail = $"missing_creature:{state.CreatureRef?.Key}"
                };
            }

            RestoreCommonMonsterTopology(creature.Monster, state);
            if (!RestoreCodecState(creature.Monster, state, creaturesByKey, context))
            {
                return new RestoreCapabilityReport
                {
                    Result = RestoreCapabilityResult.TopologyMismatch,
                    Detail = $"topology_codec_failed:{state.RuntimeCodecId ?? "none"}"
                };
            }

            if (!ValidateCreatureRole(creature, state, context))
            {
                return new RestoreCapabilityReport
                {
                    Result = RestoreCapabilityResult.TopologyMismatch,
                    Detail = $"topology_role_mismatch:{state.CreatureRef.Key}:{state.Role}"
                };
            }
        }

        return RestoreCapabilityReport.SupportedReport();
    }

    private static CreatureTopologyState CaptureCreatureTopologyState(MonsterModel monster, IReadOnlyList<Creature> creatures, int index, UndoCreatureTopologyCaptureContext context)
    {
        MonsterMoveStateMachine? moveStateMachine = monster.MoveStateMachine;
        MonsterState? currentState = moveStateMachine == null
            ? null
            : UndoReflectionUtil.FindField(moveStateMachine.GetType(), "_currentState")?.GetValue(moveStateMachine) as MonsterState;
        string? followUpStateType = (currentState as MoveState)?.FollowUpState?.Id;
        bool isHalfDead = monster.Creature.GetPower<DoorRevivalPower>()?.IsHalfDead == true;

        string? runtimeCodecId = null;
        UndoCreatureTopologyRuntimeState? runtimePayload = null;
        IReadOnlyList<CreatureRef> linkedRefs = [];
        switch (monster)
        {
            case Door door:
                runtimeCodecId = "topology:DoorAndDoormaker";
                linkedRefs = [.. CaptureLinkedCreatureRef(creatures, door.Doormaker)];
                runtimePayload = new UndoDoorTopologyRuntimeState
                {
                    CodecId = runtimeCodecId,
                    DoormakerRef = UndoStableRefs.CaptureCreatureRef(creatures, door.Doormaker),
                    DeadStateFollowUpStateId = door.DeadState.FollowUpState?.Id,
                    TimesGotBackIn = door.Doormaker.Monster is Doormaker doormaker ? doormaker.TimesGotBackIn : null
                };
                break;
            case DecimillipedeSegment segment:
                runtimeCodecId = "topology:Decimillipede";
                linkedRefs = creatures
                    .Where(creature => creature.HasPower<ReattachPower>())
                    .Select(creature => UndoStableRefs.CaptureCreatureRef(creatures, creature))
                    .Where(static creatureRef => creatureRef != null)
                    .Cast<CreatureRef>()
                    .ToList();
                runtimePayload = new UndoDecimillipedeTopologyRuntimeState
                {
                    CodecId = runtimeCodecId,
                    StarterMoveIdx = segment.StarterMoveIdx,
                    SegmentRefs = linkedRefs
                };
                break;
            case TestSubject testSubject:
                runtimeCodecId = "topology:TestSubject";
                runtimePayload = new UndoTestSubjectTopologyRuntimeState
                {
                    CodecId = runtimeCodecId,
                    IsReviving = testSubject.Creature.GetPower<AdaptablePower>() != null
                        && UndoReflectionUtil.FindProperty(typeof(AdaptablePower), "IsReviving")?.GetValue(testSubject.Creature.GetPower<AdaptablePower>()) is bool isReviving
                        && isReviving
                };
                break;
            default:
                if (monster.Creature.GetPower<VitalSparkPower>() != null)
                    runtimeCodecId = "topology:InfestedPrism";
                break;
        }

        Creature creature = monster.Creature;
        CreatureRole role = creature.PetOwner != null ? CreatureRole.Pet : CreatureRole.Enemy;
        return new CreatureTopologyState
        {
            CreatureRef = new CreatureRef { Key = UndoStableRefs.BuildCreatureKey(creature, index) },
            Role = role,
            Side = creature.Side,
            MonsterId = monster.Id,
            PetOwnerPlayerNetId = creature.PetOwner?.NetId,
            SlotName = creature.SlotName,
            Exists = true,
            IsDead = creature.IsDead,
            IsHalfDead = isHalfDead,
            CurrentMoveId = currentState?.Id,
            NextMoveId = monster.NextMove?.Id,
            CurrentStateType = currentState?.GetType().FullName,
            FollowUpStateType = followUpStateType,
            LinkedCreatureRefs = linkedRefs,
            RuntimeCodecId = runtimeCodecId,
            RuntimePayload = runtimePayload
        };
    }

    private static IEnumerable<CreatureRef> CaptureLinkedCreatureRef(IReadOnlyList<Creature> creatures, Creature? creature)
    {
        CreatureRef? creatureRef = UndoStableRefs.CaptureCreatureRef(creatures, creature);
        if (creatureRef != null)
            yield return creatureRef;
    }

    private static void RestoreCommonMonsterTopology(MonsterModel monster, CreatureTopologyState state)
    {
        // Topology owns slot and linked-creature relationships only. Move-state
        // restoration is handled by UndoMonsterState plus reconciliation.
        monster.Creature.SlotName = state.SlotName;
    }

    private static bool RestoreCodecState(MonsterModel monster, CreatureTopologyState state, IReadOnlyDictionary<string, Creature> creaturesByKey, UndoCreatureTopologyRestoreContext context)
    {
        switch (state.RuntimePayload)
        {
            case UndoDoorTopologyRuntimeState doorState when monster is Door door:
                Creature? doormakerCreature = null;
                if (doorState.DoormakerRef != null && !creaturesByKey.TryGetValue(doorState.DoormakerRef.Key, out doormakerCreature))
                    return false;

                if (doorState.DoormakerRef != null)
                    UndoReflectionUtil.TrySetPropertyValue(door, "Doormaker", doormakerCreature);
                if (door.DeadState != null && doorState.DeadStateFollowUpStateId != null && door.MoveStateMachine.States.TryGetValue(doorState.DeadStateFollowUpStateId, out MonsterState? followUpState) && followUpState is MoveState moveState)
                    door.DeadState.FollowUpState = moveState;
                if (door.Doormaker.Monster is Doormaker doormaker && doorState.TimesGotBackIn.HasValue)
                    UndoReflectionUtil.TrySetPropertyValue(doormaker, "TimesGotBackIn", doorState.TimesGotBackIn.Value);
                return true;
            case UndoDecimillipedeTopologyRuntimeState decimillipedeState when monster is DecimillipedeSegment segment:
                segment.StarterMoveIdx = decimillipedeState.StarterMoveIdx;
                return decimillipedeState.SegmentRefs.All(creatureRef => creaturesByKey.ContainsKey(creatureRef.Key));
            case UndoTestSubjectTopologyRuntimeState:
                return true;
            default:
                return true;
        }
    }

    private static bool ValidateCreatureRole(Creature creature, CreatureTopologyState state, UndoCreatureTopologyRestoreContext context)
    {
        return state.Role switch
        {
            CreatureRole.Pet => ValidatePetTopology(creature, state, context),
            CreatureRole.Enemy => creature.PetOwner == null && creature.Side == CombatSide.Enemy,
            _ => true
        };
    }

    private static bool ValidatePetTopology(Creature creature, CreatureTopologyState state, UndoCreatureTopologyRestoreContext context)
    {
        if (state.PetOwnerPlayerNetId is not ulong ownerNetId)
            return false;

        Player? owner = TryResolvePlayer(ownerNetId, context.Creatures);
        if (owner == null)
            return false;

        return creature.PetOwner == owner
            && creature.Side == owner.Creature.Side
            && owner.PlayerCombatState.Pets.Contains(creature);
    }

    private static Player? TryResolvePlayer(ulong ownerNetId, IReadOnlyList<Creature> creatures)
    {
        return creatures.Select(static creature => creature.Player)
            .FirstOrDefault(player => player?.NetId == ownerNetId);
    }
}



