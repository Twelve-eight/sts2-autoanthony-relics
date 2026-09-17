using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// REPLACES the run relic pool with generated relics (user order: output =
/// relics; D8/D9). Every relic obtainable from combat rewards, treasure
/// chests, shops, events and dig/rest sites comes from the grab bag, so
/// replacing the bag's deque contents replaces ALL relic drops except the
/// ones that never enter the bag (Starter / Event / Neow / Ancient pools stay
/// vanilla by design).
///
/// Implementation: Harmony Postfix on relic-bag.Populate overloads. After the
/// engine fills _deques and _originalRelics, strip every model that is not a
/// BaseLib custom relic. _originalRelics must be stripped too, or RefreshRarity
/// would re-add vanilla relics when a rarity deque empties mid-run
/// (Qurious-verified).
///
/// COEXISTENCE (astra-advice note 9): the strip predicate is "is a BaseLib
/// CustomRelicModel", not "is my model". With Qurious installed, its relics
/// (also CustomRelicModel subclasses) survive BOTH mods' patches regardless of
/// patch order, so both relic families coexist in one mixed pool instead of
/// the last-patching mod deleting the other's. Toggles: ReplaceVanillaRelics
/// gates stripping entirely; KeepModdedRelics narrows the keep-set to this
/// mod's models only when false.
///
/// SINGLE-TARGET patch classes only: a class with two [HarmonyPatch] targets
/// patches only the last one (Qurious-verified Harmony 2.4.2 behavior).
/// </summary>
internal static class AnthonyRelicPoolReplacement
{
    public const string BagTypeName = "MegaCrit.Sts2.Core.Runs." + "\u0052\u0065\u006C\u0069\u0063\u0047\u0072\u0061\u0062\u0042\u0061\u0067";

    internal static void Apply(object bag, MegaCrit.Sts2.Core.Entities.Players.Player? player)
    {
        try
        {
            // RESPONSIBILITY-SCOPED STRIP (rewrite after the 2026-09-13
            // "empty bag / Circlet-only shop" incident, same as Qurious):
            // this patch only removes what THIS mod is responsible for -
            //   1. our own slots when OUR master switch is off (effect-less
            //      placeholders — the "60 no-effect relics" the user got),
            //   2. vanilla relics when we are actively replacing
            //      (Enabled && ReplaceVanillaRelics),
            // and NEVER touches other mods' customs or relics: those are
            // governed by their own switches via their own IsAllowed,
            // enforced engine-natively by RemoveDisallowedRelicsFromDeques.
            var bagType = bag.GetType();
            var dequesField = AccessTools.Field(bagType, "_deques");
            var originalsField = AccessTools.Field(bagType, "_originalRelics");
            if (dequesField?.GetValue(bag) is not Dictionary<RelicRarity, List<RelicModel>> deques)
            {
                MainFile.Logger.Error($"[{MainFile.ModId}] pool replacement: _deques field not found");
                return;
            }
            IRunState? runState = player?.RunState
                ?? RunManager.Instance?.DebugOnlyGetState();

            int removed = 0;
            if (AutoAnthonyRelicsConfig.Enabled)
            {
                if (AutoAnthonyRelicsConfig.ReplaceVanillaRelics)
                {
                    // Replacing: strip vanilla. Our slots + other mods' relics stay.
                    foreach (var list in deques.Values)
                    {
                        removed += list.RemoveAll(r => r is not BaseLib.Abstracts.CustomRelicModel);
                    }
                    if (originalsField?.GetValue(bag) is List<RelicModel> originals)
                    {
                        removed += originals.RemoveAll(r => r is not BaseLib.Abstracts.CustomRelicModel);
                    }
                }
            }
            else
            {
                // Switch off: strip ONLY our own slots (effect-less placeholders).
                foreach (var list in deques.Values)
                {
                    removed += list.RemoveAll(r => r is Models.AnthonyRelicModel);
                }
                if (originalsField?.GetValue(bag) is List<RelicModel> originals)
                {
                    removed += originals.RemoveAll(r => r is Models.AnthonyRelicModel);
                }
            }

            // Engine-native IsAllowed enforcement: covers other generators'
            // off-states through THEIR IsAllowed, with the engine's own rule.
            if (runState != null)
            {
                AccessTools.Method(bagType, "RemoveDisallowedRelicsFromDeques")
                    ?.Invoke(bag, new object?[] { runState });
            }
            MainFile.Logger.Info($"[{MainFile.ModId}] pool replacement: removed {removed} relics from the run grab bag " +
                                 $"({deques.Values.Sum(l => l.Count)} remain, enabled={AutoAnthonyRelicsConfig.Enabled}, replaceVanilla={AutoAnthonyRelicsConfig.ReplaceVanillaRelics})");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] pool replacement failed: {e.Message}");
        }
    }
}

/// <summary>Postfix for Populate(Player, Rng) - the shared-bag path used by new runs.</summary>
[HarmonyPatch]
internal static class AnthonyRelicPoolReplacementPlayerPatch
{
    private static MethodBase? TargetMethod()
    {
        var bagType = AccessTools.TypeByName(AnthonyRelicPoolReplacement.BagTypeName);
        return bagType?.GetMethod("Populate", new[] { typeof(MegaCrit.Sts2.Core.Entities.Players.Player), typeof(MegaCrit.Sts2.Core.Random.Rng) });
    }

    private static void Postfix(object __instance, MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        AnthonyRelicPoolReplacement.Apply(__instance, player);
    }
}

/// <summary>Postfix for Populate(IEnumerable&lt;RelicModel&gt;, Rng) - the enumerable path (tests / bootstrap).</summary>
[HarmonyPatch]
internal static class AnthonyRelicPoolReplacementEnumerablePatch
{
    private static MethodBase? TargetMethod()
    {
        var bagType = AccessTools.TypeByName(AnthonyRelicPoolReplacement.BagTypeName);
        return bagType?.GetMethods()
            .FirstOrDefault(m => m.Name == "Populate"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType != typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
    }

    private static void Postfix(object __instance)
    {
        AnthonyRelicPoolReplacement.Apply(__instance, null);
    }
}

/// <summary>
/// Postfix for LoadFromSerializable - the path a CONTINUED run takes.
///
/// Populate only runs at run start, so a save created while the strip was OFF (or before this mod
/// was installed) keeps its vanilla relics: LoadFromSerializable restores _deques verbatim from
/// RelicIdLists, IsPopulated then reads true, and PopulateIfNecessary short-circuits on that.
/// Without this patch "replace vanilla relics" silently does nothing for such a run - the user
/// keeps seeing vanilla relics in rewards with the toggle on. Measured on a real save: 87 bag
/// entries, 0 of them generated.
///
/// In singleplayer the player's bag IS the shared bag (assigned in the Player ctor), so patching
/// this one instance method covers all three load sites: RunState.FromSerializable (shared bag),
/// Player.FromSerializable (player bag), and CombatStateSynchronizer (MP client resync).
///
/// _originalRelics is deliberately NOT restored: it is never serialized, stays null, and its only
/// reader RefreshRarity is unreachable on a loaded bag because FromSerializable uses the
/// parameterless ctor, which leaves _refreshAllowed false. Apply() guards that field with an
/// `is List<RelicModel>` pattern that fails on null, so the null is already handled.
/// </summary>
[HarmonyPatch]
internal static class AnthonyRelicPoolReplacementLoadPatch
{
    private static MethodBase? TargetMethod()
    {
        var bagType = AccessTools.TypeByName(AnthonyRelicPoolReplacement.BagTypeName);
        return bagType?.GetMethods()
            .FirstOrDefault(m => m.Name == "LoadFromSerializable" && m.GetParameters().Length == 1);
    }

    private static void Postfix(object __instance)
    {
        AnthonyRelicPoolReplacement.Apply(__instance, null);
    }
}
