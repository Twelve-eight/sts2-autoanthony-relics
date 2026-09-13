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
            // Runs whenever the mod is loaded — NOT gated on Enabled: with the
            // master switch OFF our own registered relics must be REMOVED from
            // the bag (their hooks are gated off -> effect-less placeholders).
            // (User report 2026-09-13: 60 effect-less placeholders obtainable
            // while this mod's switch was off — they were ours, kept alive by
            // the other generator's keep-all-custom predicate and by this patch
            // early-returning instead of stripping them.)
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

            bool Keep(RelicModel r)
            {
                if (r is Models.AnthonyRelicModel)
                {
                    return AutoAnthonyRelicsConfig.Enabled; // own: only when on
                }
                if (r is BaseLib.Abstracts.CustomRelicModel)
                {
                    // Other mods' customs: respect THEIR gates (a disabled
                    // generator's relics must not be obtainable). KeepModdedRelics
                    //=false additionally removes other mods' relics entirely.
                    if (!AutoAnthonyRelicsConfig.KeepModdedRelics)
                    {
                        return false;
                    }
                    return runState != null && r.IsAllowed(runState);
                }
                return AutoAnthonyRelicsConfig.Enabled && AutoAnthonyRelicsConfig.ReplaceVanillaRelics; // vanilla: only when replacing
            }

            int removed = 0;
            foreach (var list in deques.Values)
            {
                removed += list.RemoveAll(r => !Keep(r));
            }
            if (originalsField?.GetValue(bag) is List<RelicModel> originals)
            {
                removed += originals.RemoveAll(r => !Keep(r));
            }
            // Engine-native IsAllowed enforcement on top of ours.
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
