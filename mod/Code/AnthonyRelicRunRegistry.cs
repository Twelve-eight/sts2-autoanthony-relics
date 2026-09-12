using System;
using System.Collections.Generic;
using AutoAnthonyRelics.Generation;

namespace AutoAnthonyRelics;

/// <summary>
/// Per-run cache of generated relic definitions. Definitions are a pure
/// function of the run seed (no config participates), so the cache is keyed
/// by the seed alone and save-loads / reconnects regenerate the identical
/// relics from the engine-restored seed. Bounded to a few seeds (map previews
/// and menus can query relics without a run).
/// </summary>
public static class AnthonyRelicRunRegistry
{
    private const int CacheLimit = 4;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyList<GeneratedRelicDefinition>> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    /// <summary>Seed of the active run, captured by the seed-tracking patches.</summary>
    public static string? CurrentRunSeed { get; set; }

    public static IReadOnlyList<GeneratedRelicDefinition> DefinitionsFor(string runSeed, RelicFragmentPool pool)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(runSeed, out IReadOnlyList<GeneratedRelicDefinition>? cached))
            {
                return cached;
            }
            IReadOnlyList<GeneratedRelicDefinition> generated = RelicGenerator.Generate(runSeed, pool);
            while (Order.Count >= CacheLimit)
            {
                Cache.Remove(Order.Dequeue());
            }
            Order.Enqueue(runSeed);
            Cache[runSeed] = generated;
            return generated;
        }
    }

    public static GeneratedRelicDefinition? DefinitionFor(int slot, RelicFragmentPool pool)
    {
        string? seed = CurrentRunSeed;
        if (string.IsNullOrEmpty(seed))
        {
            return null;
        }
        IReadOnlyList<GeneratedRelicDefinition> definitions = DefinitionsFor(seed, pool);
        return (uint)slot < (uint)definitions.Count ? definitions[slot] : null;
    }
}
