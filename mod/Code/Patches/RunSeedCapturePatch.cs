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
                AnthonyRelicRunRegistry.CurrentRunSeed = seed;
                AnthonyRelicLocUpdater.OnSeedCaptured(seed);
                MainFile.Logger.Info($"[{MainFile.ModId}] run seed early-captured: {seed}");
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
