using System.Linq;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace UndoTheSpire2;

// Restores special-case creature visuals whose presentation depends on runtime
// state outside NetFullCombatState. This layer owns visuals only, not topology.
internal static class UndoSpecialCreatureVisualNormalizer
{
    internal sealed class PaelsLegionVisualExpectation
    {
        public required string Trigger { get; init; }

        public required IReadOnlyList<string> AcceptableAnimationNames { get; init; }
    }

    public static void Refresh(CombatState combatState, NCombatRoom combatRoom)
    {
        foreach (Creature creature in combatState.Allies)
        {
            if (creature.Monster is not MegaCrit.Sts2.Core.Models.Monsters.PaelsLegion)
                continue;

            if (!TryGetPaelsLegionExpectation(creature, out PaelsLegionVisualExpectation? expectation, out PaelsLegion? relic))
                continue;

            WarmCreatureVisualScene(creature);
            NCreature? creatureNode = combatRoom.GetCreatureNode(creature);
            if (creatureNode == null)
                continue;

            creatureNode.Visuals.SetUpSkin(creature.Monster);
            creatureNode.SetAnimationTrigger(expectation.Trigger);
        }
    }

    public static bool TryGetPaelsLegionExpectation(Creature creature, out PaelsLegionVisualExpectation? expectation)
    {
        bool matched = TryGetPaelsLegionExpectation(creature, out PaelsLegionVisualExpectation? resolvedExpectation, out _);
        expectation = resolvedExpectation;
        return matched;
    }

    private static bool TryGetPaelsLegionExpectation(Creature creature, out PaelsLegionVisualExpectation? expectation, out PaelsLegion? relic)
    {
        expectation = null;
        relic = null;
        if (creature.Monster is not MegaCrit.Sts2.Core.Models.Monsters.PaelsLegion)
            return false;

        Player? owner = creature.PetOwner;
        if (owner == null)
            return false;

        relic = owner.GetRelic<PaelsLegion>();
        if (relic == null)
            return false;

        string trigger = GetPaelsLegionVisualTrigger(relic);
        expectation = new PaelsLegionVisualExpectation
        {
            Trigger = trigger,
            AcceptableAnimationNames = trigger switch
            {
                "BlockTrigger" => ["block", "block_loop"],
                "SleepTrigger" => ["sleep"],
                "WakeUpTrigger" => ["wake_up", "idle_loop"],
                _ => ["idle_loop"]
            }
        };
        return true;
    }

    // Warm the pet visuals scene explicitly so restore does not depend on it
    // already being present in the preload cache.
    private static void WarmCreatureVisualScene(Creature creature)
    {
        if (creature.Monster == null)
            return;

        string? scenePath = creature.Monster.AssetPaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(scenePath))
            return;

        _ = PreloadManager.Cache.GetScene(scenePath);
    }

    private static string GetPaelsLegionVisualTrigger(PaelsLegion relic)
    {
        int cooldown = UndoReflectionUtil.FindProperty(relic.GetType(), "Cooldown")?.GetValue(relic) is int value ? value : 0;
        bool triggeredBlockLastTurn = UndoReflectionUtil.FindProperty(relic.GetType(), "TriggeredBlockLastTurn")?.GetValue(relic) is bool triggered && triggered;
        if (cooldown <= 0)
            return "Idle";

        return triggeredBlockLastTurn ? "BlockTrigger" : "SleepTrigger";
    }
}
