using System;
using System.Linq;
using System.Reflection;
using AutoAnthonyRelics.Generation;
using AutoAnthonyRelics.Patches;
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

            // Bag-patch target self-check. Harmony's CreateClassProcessor().Patch() does NOT fail
            // when a [HarmonyPatch] class's TargetMethod() returns null - it silently patches
            // nothing. That is how a renamed engine type degrades this feature into a no-op with a
            // green "8 patch class(es) applied" line. Resolve the target explicitly so the log says
            // whether vanilla-relic stripping is actually armed, and say WHICH overloads were found.
            try
            {
                var bagType = AccessTools.TypeByName(AnthonyRelicPoolReplacement.BagTypeName);
                if (bagType == null)
                {
                    Logger.Error($"[{ModId}] vanilla-relic replacement is DISARMED: type " +
                                 $"'{AnthonyRelicPoolReplacement.BagTypeName}' not found (engine renamed it?)");
                }
                else
                {
                    var populates = bagType.GetMethods()
                        .Where(m => m.Name == "Populate")
                        .Select(m => string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)))
                        .ToList();
                    var loads = bagType.GetMethods()
                        .Where(m => m.Name == "LoadFromSerializable")
                        .Select(m => string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)))
                        .ToList();
                    Logger.Info($"[{ModId}] vanilla-relic replacement armed: {bagType.FullName} " +
                                $"Populate overloads = [{string.Join(" | ", populates)}], " +
                                $"LoadFromSerializable = [{string.Join(" | ", loads)}], " +
                                $"replaceVanilla={AutoAnthonyRelicsConfig.ReplaceVanillaRelics}");
                    if (loads.Count == 0)
                    {
                        Logger.Error($"[{ModId}] continued runs will NOT be stripped: no " +
                                     $"LoadFromSerializable on {bagType.FullName}");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[{ModId}] bag target self-check failed: {e.Message}");
            }

            // Relic data: atoms (extractor candidates) + ledger (hand-audited
            // verdicts) -> independent trigger/effect fragment pools.
            Data.RelicAtomPool atoms = Data.RelicAtomData.LoadAtoms();
            Data.RelicLedger ledger = Data.RelicAtomData.LoadLedger();
            FragmentPool = RelicFragmentPool.Build(atoms, ledger);
            Logger.Info($"[{ModId}] relic pool: {atoms.Atoms.Count} extracted atoms, " +
                        $"{ledger.Supported.Count} ledger-supported, {ledger.Rejected.Count} ledger-rejected; " +
                        $"fragments: {FragmentPool.Triggers.Count} triggers, " +
                        $"{FragmentPool.TriggeredEffects.Count} triggered effects, " +
                        $"{FragmentPool.PassiveEffects.Count} passives, " +
                        $"{FragmentPool.BenefitEffects.Count} benefits; " +
                        $"restrictions: {FragmentPool.PassiveEffects.Count(e => e.IsRestriction)}");

            Logger.Info($"[{ModId}] initialized: enabled={AutoAnthonyRelicsConfig.Enabled}, " +
                        $"replaceVanilla={AutoAnthonyRelicsConfig.ReplaceVanillaRelics}, " +
                        // The generator version belongs in the log: it is part of the definition
                        // cache key, so it decides whether an existing save regenerates. Without
                        // it, an in-game log cannot be attributed to a build (2026-09-18: a log
                        // was misread as evidence for a fix that was not in the running binary).
                        $"seedVersion={Generation.RelicGenerator.SeedVersion}");
        }
        catch (Exception e)
        {
            Logger.Error($"[{ModId}] initializer failed: {e}");
            throw;
        }
    }
}
