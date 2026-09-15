using System;
using System.Collections.Generic;
using AutoAnthonyRelics.Generation;

namespace AutoAnthonyRelics;

/// <summary>
/// Per-run cache of generated relic definitions. Definitions are a pure
/// function of the run seed, the generator ALGORITHM VERSION and the fragment
/// pool (ledger/catalog data) - the cache key carries all three (astra AAR-4:
/// a pool/ledger change or an algorithm change with the same seed must never
/// serve a stale cached pool), so save-loads / reconnects regenerate the
/// identical relics from the engine-restored seed. Bounded to a few seeds
/// (map previews and menus can query relics without a run).
/// </summary>
public static class AnthonyRelicRunRegistry
{
    private const int CacheLimit = 4;
    private static readonly object Gate = new();
    // AAR-1 (measured 2026-09-15): the key was a concatenated string built on EVERY call -
    // 96 B for an 8-char seed, 128 B for a 24-char seed, measured at 128.3 ns - while the
    // dictionary lookup alone costs 0 B / 38.5 ns and a frozen-context comparison costs
    // 0 B / 6.2 ns. A 60-slot sweep paid 5760 B purely to build keys. The cache is now keyed
    // by an ordinal tuple over the same three components (seed, generator version, pool
    // fingerprint), so warm hits allocate nothing. Coverage of all three components is
    // pinned by tools/relic-probe (the key was under-covered twice: round 2 added the pool
    // fingerprint, AAR-4 added SeedVersion).
    private static readonly Dictionary<(string Seed, string Version, string Fingerprint), IReadOnlyList<GeneratedRelicDefinition>> Cache = new();
    private static readonly Queue<(string Seed, string Version, string Fingerprint)> Order = new();

    /// <summary>Seed of the active run, captured by the seed-tracking patches.</summary>
    public static string? CurrentRunSeed { get; set; }

    public static IReadOnlyList<GeneratedRelicDefinition> DefinitionsFor(string runSeed, RelicFragmentPool pool)
    {
        lock (Gate)
        {
            // Key = seed + ALGORITHM VERSION + fragment-pool fingerprint (second-round review
            // 2026-09-13 added the fingerprint; third round AAR-4 added SeedVersion):
            // definitions depend on the pool, the ledger and the generator, so a data, ledger
            // or algorithm change with the same seed must not serve a stale cached pool.
            (string Seed, string Version, string Fingerprint) key = (runSeed, RelicGenerator.SeedVersion, pool.Fingerprint);
            if (Cache.TryGetValue(key, out IReadOnlyList<GeneratedRelicDefinition>? cached))
            {
                return cached;
            }
            IReadOnlyList<GeneratedRelicDefinition> generated = RelicGenerator.Generate(runSeed, pool);
            while (Order.Count >= CacheLimit)
            {
                Cache.Remove(Order.Dequeue());
            }
            Order.Enqueue(key);
            Cache[key] = generated;
            return generated;
        }
    }

    /// <summary>
    /// Run-end reset (astra AAR-7): called from the RunManager.CleanUp
    /// postfix. Drops the active seed - menu/canonical queries must not
    /// resolve the previous run - and clears the cache (definitions are a
    /// pure function of (seed, version, fingerprint), so clearing costs a
    /// regeneration at most and cannot change outcomes).
    /// </summary>
    public static void ResetForRunEnd()
    {
        CurrentRunSeed = null;
        lock (Gate)
        {
            Cache.Clear();
            Order.Clear();
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
