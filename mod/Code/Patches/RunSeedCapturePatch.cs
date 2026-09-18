using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// Tracks the active run seed for AnthonyRelicRunRegistry (canonical-model-safe:
/// RelicModel.Owner asserts mutable and throws on canonical instances, so the
/// registry reads the seed from here instead of from the relic's owner).
///
/// Two capture points (Qurious-verified pattern):
/// 1. Prefix on SetUpNewSingleplayer / SetUpNewMultiplayer - BEFORE
///    InitializeNewRun populates the shared grab bag, so AnthonyRelicModel.Rarity
///    resolves real definitions (rarity spread across the reward deques).
/// 2. Postfix on RunManager.Launch (returns RunState) - the save-load /
///    general funnel fallback.
///
/// Single-target patch classes only: a patch class with two [HarmonyPatch]
/// attributes patches only the last one.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSingleplayer))]
internal static class RunSeedEarlyTrackSingleplayerPatch
{
    private static void Prefix(RunState state) => RunSeedEarlyCapture.Capture(state);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
internal static class RunSeedEarlyTrackMultiplayerPatch
{
    private static void Prefix(RunState state) => RunSeedEarlyCapture.Capture(state);
}

internal static class RunSeedEarlyCapture
{
    internal static void Capture(RunState? state)
    {
        try
        {
            string? seed = state?.Rng?.StringSeed;
            if (!string.IsNullOrEmpty(seed))
            {
                // Freeze the generation settings BEFORE the first definition
                // lookup of the run (user order 2026-09-18): the extra-pool
                // toggle and the pool weights are generation inputs, so a
                // mid-run settings edit must not re-roll relics the player
                // already holds. Unfrozen by ResetForRunEnd.
                Generation.GenerationSettings.Freeze();
                AnthonyRelicRunRegistry.CurrentRunSeed = seed;
                AnthonyRelicLocUpdater.OnSeedCaptured(seed);
                MainFile.Logger.Info($"[{MainFile.ModId}] run seed early-captured: {seed}; " +
                                     $"generation settings frozen: {Generation.GenerationSettings.Current.Key}");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] early seed capture failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
internal static class RunSeedLaunchTrackPatch
{
    private static void Postfix(RunState __result)
    {
        try
        {
            string? seed = __result?.Rng?.StringSeed;
            AnthonyRelicRunRegistry.CurrentRunSeed = seed;
            AnthonyRelicLocUpdater.OnSeedCaptured(seed);
            MainFile.Logger.Info($"[{MainFile.ModId}] run seed captured: {seed ?? "(null)"}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] seed capture failed: {e.Message}");
        }
    }
}
