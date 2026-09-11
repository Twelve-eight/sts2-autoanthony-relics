using BaseLib.Config;

namespace AutoAnthonyRelics;

/// <summary>
/// Runtime toggles (Settings -> Mod Settings), SimpleModConfig pattern.
///
/// NAMING RULE: the property name must already be in the Title_Snake form that
/// BaseLib's Slugify produces, because the loc key is
/// {ModPrefix}Slugify(propertyName).title and that slugifier is lossy on
/// ALL_CAPS_SNAKE (QuriousCraftingRelics measured 145/152 broken names before
/// the fix). Single-word names are the safest: "Enabled" slugifies to itself.
///
/// MP DETERMINISM: everything here feeds the seeded generator, so both ends
/// must agree. These are Tier-1 keys in the sts2-mpconfigsync sense.
/// </summary>
[ConfigHoverTipsByDefault]
internal class AutoAnthonyRelicsConfig : SimpleModConfig
{
    /// <summary>Master switch. When false, the engine's native pool is left alone.</summary>
    public static bool Enabled { get; set; } = true;
}
