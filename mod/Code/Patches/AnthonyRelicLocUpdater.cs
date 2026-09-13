using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// Live relic descriptions (mirrors QuriousCraftingRelics ChaosRelicLocUpdater,
/// user order 2026-09-13: "东尼算法-遗物没有应有的文本描述...应当向怪异炼化遗物看齐").
///
/// BaseLib's ModelLocPatch evaluates ILocalizationProvider.Localization ONCE at
/// ModelDb.Init - at that point CurrentRunSeed is null, so the loc table gets
/// the generic fallback text baked in and in-game tooltips show no effects.
///
/// Fix: whenever the run seed is captured (new run or save load), rewrite the
/// "relics" loc-table entries for all 60 slots from the LIVE definitions -
/// same reflection into LocTable._translations that BaseLib itself uses.
/// Dedupe key = seed + fragment-pool fingerprint: if the definitions ever
/// change without the seed changing, the table must follow.
/// </summary>
internal static class AnthonyRelicLocUpdater
{
    private static readonly FieldInfo? LocDictionaryField =
        AccessTools.Field(typeof(LocTable), "_translations");

    private static string? _lastKey;

    /// <summary>Seed just captured (may be null when leaving a run).</summary>
    internal static void OnSeedCaptured(string? seed)
    {
        try
        {
            if (seed is null)
            {
                _lastKey = null;
                return;
            }
            string cacheKey = seed + " " + MainFile.FragmentPool.Fingerprint;
            if (cacheKey == _lastKey)
            {
                return;
            }
            if (MainFile.FragmentPool.Triggers.Count == 0)
            {
                return; // pool not built yet (early capture before init)
            }
            _lastKey = cacheKey;
            var definitions = AnthonyRelicRunRegistry.DefinitionsFor(
                seed, MainFile.FragmentPool);
            if (LocManager.Instance is null || LocDictionaryField?.GetValue(LocManager.Instance.GetTable("relics"))
                    is not Dictionary<string, string> dict)
            {
                MainFile.Logger.Error($"[{MainFile.ModId}] loc update: relics table not found");
                return;
            }
            bool chinese = TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            int entries = 0;
            foreach (var (slotModel, definition) in Pools.AnthonyRelicRegistry.SlotModels
                         .Zip(definitions, (m, d) => (m, d)))
            {
                string key = slotModel.Id.Entry;
                dict[$"{key}.title"] = chinese ? definition.NameZhs : definition.NameEn;
                dict[$"{key}.description"] = chinese ? definition.DescriptionZhs : definition.DescriptionEn;
                entries++;
            }
            MainFile.Logger.Info($"[{MainFile.ModId}] relic descriptions updated for seed {seed} ({entries} slots)");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] loc update failed: {e.Message}");
        }
    }
}
