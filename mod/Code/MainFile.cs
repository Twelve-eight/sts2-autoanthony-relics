using System;
using System.Reflection;
using AutoAnthonyRelics.Generation;
using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace AutoAnthonyRelics;

/// <summary>
/// Mod entry point.
///
/// Startup loads the relic atom pool + the hand-audited ledger and builds the
/// fragment pool once. The ledger is deny-by-default: atoms without a
/// verified "supported" entry never reach the generator, so an extractor
/// defect degrades the pool instead of corrupting it. The card-direction
/// catalog (AnthonyCatalog / card recipe JSONs) is intentionally NOT loaded
/// here: this build's product is relics, and a card-pool load must never read
/// as relic initialization success.
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "AutoAnthonyRelics";
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    /// <summary>Built once at startup; sampled by the generator, read by the host.</summary>
    public static RelicFragmentPool FragmentPool { get; private set; } =
        RelicFragmentPool.EmptyInstance;

    public static void Initialize()
    {
        try
        {
            // Settings -> Mod Settings UI registration.
            ModConfigRegistry.Register(ModId, new AutoAnthonyRelicsConfig());

            // Per-type Harmony isolation (Spire1 pattern): one bad patch class
            // must never abort the rest of the set.
            Harmony harmony = new(ModId);
            int patchedClasses = 0;
            int failedClasses = 0;
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    patchedClasses++;
                }
                catch (Exception e)
                {
                    failedClasses++;
                    Logger.Error($"[{ModId}] Harmony patch class {type.FullName} failed (continuing): {e.Message}");
                }
            }
            Logger.Info($"[{ModId}] Harmony: {patchedClasses} patch class(es) applied, {failedClasses} failed");

            // Relic data: atoms (extractor candidates) + ledger (hand-audited
            // verdicts) -> independent trigger/effect fragment pools.
            Data.RelicAtomPool atoms = Data.RelicAtomData.LoadAtoms();
            Data.RelicLedger ledger = Data.RelicAtomData.LoadLedger();
            FragmentPool = RelicFragmentPool.Build(atoms, ledger);
            Logger.Info($"[{ModId}] relic pool: {atoms.Atoms.Count} extracted atoms, " +
                        $"{ledger.Supported.Count} ledger-supported, {ledger.Rejected.Count} ledger-rejected; " +
                        $"fragments: {FragmentPool.Triggers.Count} triggers, " +
                        $"{FragmentPool.TriggeredEffects.Count} triggered effects, " +
                        $"{FragmentPool.PassiveEffects.Count} passives");

            Logger.Info($"[{ModId}] initialized: enabled={AutoAnthonyRelicsConfig.Enabled}, " +
                        $"replaceVanilla={AutoAnthonyRelicsConfig.ReplaceVanillaRelics}, " +
                        $"keepModded={AutoAnthonyRelicsConfig.KeepModdedRelics}");
        }
        catch (Exception e)
        {
            Logger.Error($"[{ModId}] initializer failed: {e}");
            throw;
        }
    }
}
