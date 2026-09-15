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

    // Frozen active context (AAR-1): the overwhelmingly common access pattern is many lookups
    // in a row for the SAME (seed, version, fingerprint) - a render pass resolves every relic
    // slot against one pool. Comparing three strings costs ~6 ns and allocates nothing, versus
    // ~38.5 ns for the dictionary lookup alone (slice measurement), so a frozen context wins.
    //
    // THREADING: this field is read WITHOUT the lock, so it is published as ONE immutable
    // snapshot object - a single atomic reference write. Publishing the four values separately
    // would let a reader observe a new seed paired with a previous result (torn read), i.e.
    // return another run's relics. `volatile` keeps the write from being reordered with the
    // state it describes.
    private sealed record FrozenContext(
        string Seed,
        string Version,
        string Fingerprint,
        IReadOnlyList<GeneratedRelicDefinition> Result);

    private static volatile FrozenContext? _frozen;

    /// <summary>Seed of the active run, captured by the seed-tracking patches.</summary>
    public static string? CurrentRunSeed { get; set; }

    public static IReadOnlyList<GeneratedRelicDefinition> DefinitionsFor(string runSeed, RelicFragmentPool pool)
    {
        // Fast path: same context as the last resolved lookup. Value equality (not reference
        // equality) so a distinct string instance with the same text still hits.
        FrozenContext? frozen = _frozen;
        if (frozen != null
            && frozen.Seed == runSeed
            && frozen.Version == RelicGenerator.SeedVersion
            && frozen.Fingerprint == pool.Fingerprint)
        {
            return frozen.Result;
        }

        lock (Gate)
        {
            // Key = seed + ALGORITHM VERSION + fragment-pool fingerprint (second-round review
            // 2026-09-13 added the fingerprint; third round AAR-4 added SeedVersion):
            // definitions depend on the pool, the ledger and the generator, so a data, ledger
            // or algorithm change with the same seed must not serve a stale cached pool.
            (string Seed, string Version, string Fingerprint) key = (runSeed, RelicGenerator.SeedVersion, pool.Fingerprint);
            if (Cache.TryGetValue(key, out IReadOnlyList<GeneratedRelicDefinition>? cached))
            {
                _frozen = new FrozenContext(key.Seed, key.Version, key.Fingerprint, cached);
                return cached;
            }
            IReadOnlyList<GeneratedRelicDefinition> generated = RelicGenerator.Generate(runSeed, pool);
            while (Order.Count >= CacheLimit)
            {
                (string Seed, string Version, string Fingerprint) evicted = Order.Dequeue();
                Cache.Remove(evicted);
                // Keep the frozen context consistent with the cache: if the evicted entry is the
                // frozen one, clear it so the fast path cannot serve an un-cached result.
                FrozenContext? current = _frozen;
                if (current != null && current.Seed == evicted.Seed && current.Version == evicted.Version
                    && current.Fingerprint == evicted.Fingerprint)
                {
                    _frozen = null;
                }
            }
            Order.Enqueue(key);
            Cache[key] = generated;
            _frozen = new FrozenContext(key.Seed, key.Version, key.Fingerprint, generated);
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
            // The frozen fast-path context must be dropped with the cache, or a menu query
            // after a run would still be served the previous run's definitions.
            _frozen = null;
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
