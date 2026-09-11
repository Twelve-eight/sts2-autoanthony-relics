using System;
using System.Reflection;
using AutoAnthonyRelics.Data;
using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace AutoAnthonyRelics;

/// <summary>
/// Mod entry point.
///
/// Stage A scope: register the config, apply Harmony patches, and load +
/// validate the generation pool at startup. Loading the pool here is not
/// busywork - it turns a broken embed (or a data revision with an opcode we do
/// not know) into a startup log line instead of a mid-run failure on the first
/// generated card.
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "AutoAnthonyRelics";
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

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

            AnthonyCatalog catalog = AnthonyCatalog.Instance;
            CatalogStats stats = catalog.Stats();
            Logger.Info($"[{ModId}] pool loaded: {stats.RecipeCount} recipes, " +
                        $"{stats.FragmentCount} fragments, {stats.SpecCount} specs, " +
                        $"{stats.DistinctVariants} variants, {stats.DistinctOpcodeVariantPairs} opcode/variant pairs, " +
                        $"characters: {string.Join("/", catalog.Characters)}");

            CatalogValidation validation = catalog.Validate();
            if (validation.Ok)
            {
                Logger.Info($"[{ModId}] pool validation: clean");
            }
            else
            {
                foreach (string problem in validation.Problems)
                {
                    Logger.Error($"[{ModId}] pool problem: {problem}");
                }
            }

            Logger.Info($"[{ModId}] initialized: enabled={AutoAnthonyRelicsConfig.Enabled}");
        }
        catch (Exception e)
        {
            Logger.Error($"[{ModId}] initializer failed: {e}");
            throw;
        }
    }
}
