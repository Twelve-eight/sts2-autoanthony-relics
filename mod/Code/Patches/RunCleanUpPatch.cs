using System;

using HarmonyLib;

using MegaCrit.Sts2.Core.Runs;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// Run-scope cleanup (astra AAR-7): CleanUp fires when a run ends (finish,
/// abandon, disconnect, return to menu). Without this, CurrentRunSeed
/// survived into menus and a post-run menu or next run could observe stale
/// process-global seed state. The definition cache is cleared too - it is a
/// pure function of (seed, SeedVersion, pool fingerprint), so clearing costs
/// a regeneration at most and never changes outcomes.
/// </summary>
[HarmonyPatch(typeof(RunManager), "CleanUp")]
internal static class RunCleanUpPatch
{
    private static void Postfix()
    {
        try
        {
            AnthonyRelicRunRegistry.ResetForRunEnd();
            MainFile.Logger.Info($"[{MainFile.ModId}] run cleaned up: seed and definition cache reset");
        }
        catch (Exception e)
        {
            // Cleanup must never break session teardown.
            MainFile.Logger.Error($"[{MainFile.ModId}] run cleanup failed: {e.Message}");
        }
    }
}
