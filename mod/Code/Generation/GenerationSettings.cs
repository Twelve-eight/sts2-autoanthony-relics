using System;

namespace AutoAnthonyRelics.Generation;

/// <summary>
/// The generation-time settings snapshot (user order 2026-09-18: "做它的开关. 设置页面
/// 可调每种词条池的生成权重").
///
/// WHY THIS TYPE EXISTS: until 2026-09-18 no config participated in generation,
/// so the definition cache key was (seed, SeedVersion, pool fingerprint). The
/// extra-pool toggle and the four pool weights ARE generation inputs, so they
/// must reach the cache key or a settings change would serve a stale cached
/// definition set - the exact defect class the fingerprint was added for.
///
/// Two properties make that safe:
/// 1. <see cref="Key"/> is a pure, COLLISION-FREE function of the five values
///    and is a component of the definition cache key
///    (AnthonyRelicRunRegistry.DefinitionsFor).
/// 2. The values are FROZEN at seed capture. Editing a weight mid-run must not
///    re-roll an existing run's relics: definitions are re-derived on every
///    lookup, so a live config read would silently change what an already-held
///    relic does. This is the same freeze the sister mod QuriousCraftingRelics
///    applies to its template bounds (QuriousGenerationSnapshot).
///
/// MP DETERMINISM: these are Tier-1 keys. Two ends with different values
/// generate different relics, so the settings page must say "both ends must
/// match" - same treatment as QuriousCraftingRelics.EnableExtraPool (that mod's
/// own property, whose name is the collision this mod's key had to move away
/// from).
///
/// A readonly STRUCT on purpose: <see cref="Current"/> is read on every
/// definition lookup, and the registry around it is allocation-measured (see
/// AnthonyRelicRunRegistry's AAR-1 note). A record would allocate one object
/// per lookup on the unfrozen path.
/// </summary>
internal readonly struct GenerationSettings
{
    /// <summary>Bits per weight in <see cref="Key"/>. Sliders cap at 400, so 10 bits (0..1023) is spare.</summary>
    private const int WeightBits = 10;
    private const int WeightMask = (1 << WeightBits) - 1;

    internal GenerationSettings(
        bool includeExtraPool,
        int weightTriggeredCore,
        int weightPassiveCore,
        int weightBenefitCore,
        int weightExtra)
    {
        IncludeExtraPool = includeExtraPool;
        WeightTriggeredCore = Clamp(weightTriggeredCore);
        WeightPassiveCore = Clamp(weightPassiveCore);
        WeightBenefitCore = Clamp(weightBenefitCore);
        WeightExtra = Clamp(weightExtra);
    }

    /// <summary>
    /// Non-negative because a negative weight would make the weighted pick's
    /// `roll &lt; accumulated` walk past a candidate it should have been able to
    /// select; capped at the packing width so <see cref="Key"/> stays exact.
    /// </summary>
    private static int Clamp(int value) => value < 0 ? 0 : value > WeightMask ? WeightMask : value;

    internal bool IncludeExtraPool { get; }
    internal int WeightTriggeredCore { get; }
    internal int WeightPassiveCore { get; }
    internal int WeightBenefitCore { get; }
    internal int WeightExtra { get; }

    /// <summary>
    /// Values used when nothing has bound the settings page: the shipped
    /// defaults. Kept here (not read from AutoAnthonyRelicsConfig) so this type
    /// carries NO BaseLib dependency - see <see cref="ConfigSource"/>.
    /// </summary>
    internal static readonly GenerationSettings Default = new(
        includeExtraPool: false,
        weightTriggeredCore: 100, weightPassiveCore: 100,
        weightBenefitCore: 100, weightExtra: 100);

    /// <summary>
    /// Supplies the live settings-page values. Bound by the mod at startup
    /// (MainFile.BindGenerationSettings) to a delegate that reads
    /// AutoAnthonyRelicsConfig.
    ///
    /// WHY INDIRECT: this type is part of the generator's PURE source layer,
    /// which tools/relic-eligibility-probe compiles directly (no mod build
    /// output, so a stale artifact cannot be probed). That project references
    /// only sts2.dll - reading AutoAnthonyRelicsConfig here would drag in BaseLib
    /// and break the only independent oracle for RelicGenerator.Excluded
    /// (2026-09-19). Leaving it null makes the probe measure the DEFAULT
    /// settings, which is exactly the configuration it wants to model.
    /// </summary>
    internal static Func<GenerationSettings>? ConfigSource;

    /// <summary>Live values from the settings page, or the defaults when unbound.</summary>
    internal static GenerationSettings FromConfig() => ConfigSource?.Invoke() ?? Default;

    // NOT `GenerationSettings?`: a struct cannot hold a field of
    // Nullable<itself> - the loader hits a circular layout dependency and every
    // access dies with TypeLoadException ("Could not load type
    // AutoAnthonyRelics.Generation.GenerationSettings"), which is how this was
    // found (2026-09-19, the probe crashed on the first Generate call). A plain
    // field plus an explicit flag has identical semantics and no cycle.
    private static bool _hasFrozen;
    private static GenerationSettings _frozen;
    private static GenerationSettings _live = FromConfig();

    /// <summary>
    /// The settings generation must use: the frozen snapshot inside a run,
    /// otherwise the settings page's current values. Mirrors the pool freeze so
    /// a mid-run edit cannot change an in-progress run's relics.
    ///
    /// The unfrozen path re-reads the config only when a value actually
    /// changed, so the common case is five field comparisons and no allocation.
    /// </summary>
    internal static GenerationSettings Current
    {
        get
        {
            if (_hasFrozen)
            {
                return _frozen;
            }
            GenerationSettings live = _live;
            GenerationSettings current = FromConfig();
            // Explicit field comparison, not Equals: the default ValueType.Equals
            // is reflection-based, and this runs on every definition lookup.
            if (live.IncludeExtraPool == current.IncludeExtraPool
                && live.WeightTriggeredCore == current.WeightTriggeredCore
                && live.WeightPassiveCore == current.WeightPassiveCore
                && live.WeightBenefitCore == current.WeightBenefitCore
                && live.WeightExtra == current.WeightExtra)
            {
                return live;
            }
            return _live = current;
        }
    }

    /// <summary>
    /// Snapshot the settings page values for the run. IDEMPOTENT: the first
    /// freeze of a run wins. Two capture points call this (the early
    /// SetUpNew* prefix and the RunManager.Launch postfix used by save-load), and
    /// the Launch funnel must not overwrite the snapshot the early capture took -
    /// if it did, a settings edit between the two would silently re-roll the
    /// relics the player already holds, which is exactly what the freeze exists
    /// to prevent.
    /// </summary>
    internal static void Freeze()
    {
        if (_hasFrozen)
        {
            return;
        }
        _frozen = FromConfig();
        _hasFrozen = true;
    }

    /// <summary>
    /// Freeze an EXPLICIT snapshot, bypassing the settings page. Exists so the
    /// probes can exercise "extra pool on" and a skewed weight profile without
    /// writing user config (they run outside Godot and must not touch the
    /// player's settings file). Always overwrites: unlike <see cref="Freeze"/>
    /// this is a test seam, not a run-lifecycle transition.
    /// </summary>
    internal static void FreezeExplicit(bool includeExtraPool, int weightTriggeredCore,
        int weightPassiveCore, int weightBenefitCore, int weightExtra)
    {
        _frozen = new GenerationSettings(includeExtraPool, weightTriggeredCore,
            weightPassiveCore, weightBenefitCore, weightExtra);
        _hasFrozen = true;
    }

    internal static void Unfreeze() => _hasFrozen = false;

    /// <summary>
    /// The five values packed into one integer, for the definition cache key.
    /// A perfect encoding, not a hash: 1 + 4x10 bits fits in a long, so two
    /// different settings can never share a key (a hash would risk serving one
    /// setting's relics for another's).
    /// </summary>
    internal long Key
    {
        get
        {
            long key = IncludeExtraPool ? 1L : 0L;
            key = (key << WeightBits) | (uint)WeightTriggeredCore;
            key = (key << WeightBits) | (uint)WeightPassiveCore;
            key = (key << WeightBits) | (uint)WeightBenefitCore;
            key = (key << WeightBits) | (uint)WeightExtra;
            return key;
        }
    }

    /// <summary>
    /// Weight for one fragment. The BAND matters as well as the pool: the
    /// settings page exposes a separate weight for the strictly-beneficial
    /// passives (the Ancient benefit affixes), which are a different band from
    /// the ordinary passives (hand-draw modifiers, restriction affixes).
    /// </summary>
    internal int WeightFor(EffectFragment effect) => effect.Pool == FragmentPoolKind.Extra
        ? WeightExtra
        : effect.IsBenefit ? WeightBenefitCore
        : effect.IsPassive ? WeightPassiveCore
        : WeightTriggeredCore;
}
