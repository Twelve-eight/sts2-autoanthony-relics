using BaseLib.Config;

namespace AutoAnthonyRelics;

/// <summary>
/// Runtime toggles (Settings -> Mod Settings), SimpleModConfig pattern.
///
/// NAMING RULE: the property name must already be in the Title_Snake form that
/// BaseLib's Slugify produces, because the loc key is
/// {ModPrefix}Slugify(propertyName).title and that slugifier is lossy on
/// ALL_CAPS_SNAKE (QuriousCraftingRelics measured 145/152 broken names before
/// the fix). Camel-case multi-word names are safe; single-word safest.
///
/// MP DETERMINISM: the generation keys below DO participate in generation
/// (changed 2026-09-18, user order "扩遗物账本..做它的开关. 设置页面可调每种词条池的
/// 生成权重"). Definitions are a pure function of (seed, SeedVersion, pool
/// fingerprint, weight profile), so two ends with DIFFERENT generation keys
/// generate DIFFERENT relics. EnableExtraEffectPool and the four weights are
/// therefore Tier-1 MP determinism keys - both ends must match, exactly like
/// QuriousCraftingRelics.EnableExtraPool (that mod's OWN property - note it is
/// still named EnableExtraPool, which is exactly why this mod's key had to
/// change). This is a deliberate reversal of the
/// older "nothing here participates in generation" rule, because the user asked
/// for generation knobs: the choice is between "toggles may differ" and "toggles
/// can shape generation", and the latter was requested explicitly.
///
/// How it stays safe: a generation key change must never serve a STALE cached
/// definition, so the keys are packed into GenerationSettings.Key and that value
/// is a component of the definition cache key (AnthonyRelicRunRegistry) - see
/// GenerationSettings. They are deliberately NOT folded into
/// RelicFragmentPool.Fingerprint: the pool is always built with the extra atoms
/// present (MainFile), and the toggle is applied at generation time by
/// RelicGenerator.Excluded rule 6, so the fingerprint cannot express it.
///
/// SEPARATELY: "toggles may differ" is still false for every local action.
/// ReplaceVanillaRelics mutates a shared, replicated structure (the run grab
/// bag); stripping it on an MP client would diverge the client's bag from the
/// host's, so the load-path hook skips the strip when this process is a
/// NetGameType.Client (AnthonyRelicPoolReplacementMpGuard). Do NOT add a toggle
/// that mutates replicated state without the same guard.
/// </summary>
[ConfigHoverTipsByDefault]
internal class AutoAnthonyRelicsConfig : SimpleModConfig
{
    /// <summary>Master switch. When false, the engine's native pool is left alone.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// When true, vanilla relics are stripped from the run grab bag so
    /// generated relics replace the drop pool (user order: output = relics).
    /// </summary>
    public static bool ReplaceVanillaRelics { get; set; } = true;

    /// <summary>
    /// Join the EXTRA effect pool (ported from the sister mod
    /// QuriousCraftingRelics, user order 2026-09-18). OFF by default: the extra
    /// fragments are card-level effects (hand Retain/Sly/Ethereal, enchantments)
    /// that vanilla relics never do, so a run that leaves this off keeps the
    /// original feel. A Tier-1 MP determinism key - both ends must match.
    ///
    /// NAME CHOICE IS LOAD-BEARING (real incident 2026-09-19). This property was
    /// first called <c>EnableExtraPool</c>, which is ALSO one of
    /// QuriousCraftingRelics' pre-rename legacy scalar keys
    /// (AutoAnthonyRelics/mod/Code/ConfigMigration.cs KnownLegacyScalarKeys).
    /// That migration identifies a legacy file by KEY SET - any single matching
    /// key claims the file - and both mods' cfgs are named AutoAnthonyRelics.cfg
    /// (BaseLib derives the name from the ROOT NAMESPACE, and Qurious was
    /// renamed from AutoAnthonyRelics). So AAR's own config was renamed to
    /// .v0.5.1.bak and its keys merged into Qurious's cfg, silently resetting
    /// every AAR setting.
    ///
    /// Do NOT rename this back, and do not add a property whose name appears in
    /// Qurious's legacy key list (EnableChaosRelics, EnableExtraPool,
    /// ChaosRelicMultiplier, ChaosRelicBudget*, ChaosRelicNegativeChance*) or in
    /// its template-scoped prefixes (Cost_/Refund_/Min_/Max_). Qurious's side of
    /// the guard was hardened too (ownership now requires the legacy file to be
    /// a strict subset), but the cheap defence is to not collide.
    /// </summary>
    public static bool EnableExtraEffectPool { get; set; } = false;

    /// <summary>
    /// Relative sampling weight of the CORE triggered fragments (the default
    /// pool: damage / block / energy / draw / powers / downsides). Only the
    /// ratio between the weights matters; 100 is the neutral value that
    /// reproduces the pre-2026-09-18 uniform draw when all four are 100.
    /// Tier-1 MP determinism key.
    /// </summary>
    [ConfigSlider(0, 400, 10)]
    public static int WeightTriggeredCore { get; set; } = 100;

    /// <summary>
    /// Relative sampling weight of the CORE passive band (hand-draw modifiers
    /// either sign, and the Ancient restriction affixes).
    ///
    /// This scales the BAND's share of the slot roll (RelicGenerator.PickBand):
    /// raising it makes passive relics more common, 0 switches the band off
    /// entirely. It does NOT reweight which passive is chosen inside the band -
    /// every candidate there carries this same weight, so that pick is
    /// arithmetically uniform, and the restriction PAIR is drawn uniformly too
    /// (RelicGenerator's pairs[random.Next(pairs.Count)]). Those are deliberate:
    /// the asked-for knob is "how much does this pool contribute", not
    /// "which member of the pool wins".
    /// Tier-1 MP determinism key.
    /// </summary>
    [ConfigSlider(0, 400, 10)]
    public static int WeightPassiveCore { get; set; } = 100;

    /// <summary>
    /// Relative sampling weight of the CORE benefit band (the Ancient
    /// strictly-beneficial affixes).
    ///
    /// Scales the band's share of the slot roll, exactly like
    /// <see cref="WeightPassiveCore"/>; 0 switches the benefit band off. It does
    /// NOT choose between benefit fragments (they all carry this same weight).
    /// Tier-1 MP determinism key.
    /// </summary>
    [ConfigSlider(0, 400, 10)]
    public static int WeightBenefitCore { get; set; } = 100;

    /// <summary>
    /// Relative sampling weight of the EXTRA pool fragments. Ignored while
    /// EnableExtraEffectPool is off (they are not in the pool at all). Tier-1 MP
    /// determinism key.
    /// </summary>
    [ConfigSlider(0, 400, 10)]
    public static int WeightExtra { get; set; } = 100;
}
