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
/// MP DETERMINISM: NOTHING here participates in generation. Definitions are a
/// pure function of (mod id, version, run seed, slot), so both multiplayer
/// ends regenerate the identical relic set from the engine-synced seed even
/// if toggles differ. That is the structural fix for the live-config-in-
/// definition-key defect class (astra-advice item 5 / Qurious fingerprint
/// cache).
///
/// BUT "toggles may differ" is only true for GENERATION, not for every local
/// action. ReplaceVanillaRelics mutates a shared, replicated structure: the
/// run grab bag. Stripping it on an MP client would diverge the client's bag
/// from the host's, so the load-path hook skips the strip when this process is
/// a NetGameType.Client (AnthonyRelicPoolReplacementMpGuard). Do NOT add a
/// toggle that mutates replicated state without the same guard.
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
}
