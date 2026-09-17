using System;
using System.Collections.Generic;
using System.Linq;
using AutoAnthonyRelics.Data;

namespace AutoAnthonyRelics.Generation;

/// <summary>
/// Independent relic fragments. The source atom fuses a trigger with its
/// effect (extractor shape); splitting them here is what makes trigger and
/// effect sample INDEPENDENTLY - the AutoAnthony recombination core, applied
/// to relics. The source pairing is never carried over: generation samples a
/// trigger from one relic and an effect from a different relic.
/// </summary>
public sealed record TriggerFragment(string Kind, string? Condition, string SourceAtom)
{
    public string Key => $"{Kind}|{Condition ?? "-"}";
}

public sealed record EffectFragment(
    string Opcode,
    string? Variant,
    string? Target,
    IReadOnlyList<ResolvedValue> Values,
    bool IsPassive,
    string SourceAtom,
    string? Condition = null)
{
    public string Key => $"{Opcode}|{Variant ?? "-"}|{Target ?? "-"}|{IsPassive}|{ValuesKey}";

    /// <summary>Shape without values: same-shape fragments must not co-exist in one relic.</summary>
    public string ShapeKey => $"{Opcode}|{Variant ?? "-"}|{Target ?? "-"}|{IsPassive}";

    private string ValuesKey => string.Join(",", Values.Select(v => $"{v.Id}:{v.Value}"));

    /// <summary>
    /// Downside opcodes (downside pool, user order 2026-09-15): triggered
    /// effects that hurt the owner. Sampling is UNIFORM as of the 2026-09-17
    /// order, which cancelled the 1.4x weight these fragments used to carry -
    /// the set now documents polarity (probe assertions, text) and no longer
    /// changes any draw. add_curse fragments differ per curse via Variant, so
    /// Key/ShapeKey stay distinct.
    /// </summary>
    public static readonly HashSet<string> DownsideOpcodes = new(StringComparer.Ordinal)
    {
        "lose_hp", "lose_max_hp", "lose_gold", "add_curse",
    };

    public bool IsDownside => DownsideOpcodes.Contains(Opcode);

    /// <summary>
    /// Restriction opcodes (user order 2026-09-17): Ancient relics whose effect
    /// is a veto or a penalty the engine applies through a named query hook
    /// rather than a command. They are passive (trigger-less), so they can only
    /// reach a relic through the passive slot.
    ///
    /// Each one ships in the engine paired with an offsetting benefit
    /// (RelicGenerator.RestrictionOffsets); a restriction affix is never
    /// generated without its offset, because the engine itself never ships a
    /// bare restriction and a pure-cost relic the player is forced to pick up
    /// would be a design defect, not a downside.
    /// </summary>
    public static readonly HashSet<string> RestrictionOpcodes = new(StringComparer.Ordinal)
    {
        "restrict_gold", "restrict_potion", "restrict_card_play",
        "restrict_draw", "modify_card_cost", "enemy_strength_gain",
    };

    public bool IsRestriction => RestrictionOpcodes.Contains(Opcode);

    /// <summary>
    /// Benefit opcodes: the Ancient relics' strictly-beneficial passives. The
    /// generator gates these behind its own 5% roll (user order 2026-09-17),
    /// separate from the 15% passive-relic roll, so a strictly-good passive
    /// stays rare.
    ///
    /// modify_hand_draw is deliberately NOT listed even when its amount is
    /// positive. The order scopes the 5% rate to the Ancient relics' benefits;
    /// BagOfPreparation (+2, a vanilla Common) already sits in the passive
    /// band, and folding it in here would silently move existing pool content
    /// to a different rate. Its polarity is per-fragment (sign), not per-opcode.
    /// </summary>
    public static readonly HashSet<string> BenefitOpcodes = new(StringComparer.Ordinal)
    {
        "modify_max_energy", "retain_hand", "extra_turn",
        "expand_card_pool", "enchant_reward",
    };

    public bool IsBenefit => BenefitOpcodes.Contains(Opcode);

    /// <summary>
    /// Opcodes whose engine command needs a live combat context (energy /
    /// block / draw pile / enemies) and NREs without one: the executor skips
    /// them, with a log line, when the owner has no PlayerCombatState
    /// (AnthonyRelicModel). The generator must never promise one of these in a
    /// context that cannot supply it (RelicGenerator.Excluded), so the set
    /// lives here - on the fragment shape both sides already agree on -
    /// instead of being owned by either side alone.
    ///
    /// Downside opcodes are NOT in this set on purpose: the engine itself fires
    /// obtain-time self-damage / LoseMaxHp / LoseGold / AddCurseToDeck outside
    /// combat (FragrantMushroom, PrecariousShears, LeafyPoultice, SilkenTress,
    /// CursedPearl), and lose_hp's only triggers are obtained/turn_start.
    /// </summary>
    public static readonly HashSet<string> CombatScopedOpcodes = new(StringComparer.Ordinal)
    {
        "apply_power", "gain_block", "gain_energy", "draw_cards", "deal_damage",
    };

    public int Amount => ValueOf("amount", 0);

    public int ValueOf(string id, int fallback = 0)
    {
        foreach (ResolvedValue value in Values)
        {
            if (string.Equals(value.Id, id, StringComparison.Ordinal))
            {
                return value.Value;
            }
        }
        return fallback;
    }

    public bool HasValue(string id) => Values.Any(v => string.Equals(v.Id, id, StringComparison.Ordinal));
}

/// <summary>
/// The fragment pools derived from the ledger's supported atoms. Built once
/// at startup; the generator samples from these only, so an atom without a
/// hand-verified ledger entry can never reach a generated relic.
/// </summary>
public sealed class RelicFragmentPool
{
    public static readonly RelicFragmentPool EmptyInstance = new(
        Array.Empty<TriggerFragment>(),
        Array.Empty<EffectFragment>(),
        Array.Empty<EffectFragment>(),
        Array.Empty<EffectFragment>(),
        0);

    public IReadOnlyList<TriggerFragment> Triggers { get; }
    public IReadOnlyList<EffectFragment> TriggeredEffects { get; }
    /// <summary>
    /// Passives that are NOT strict benefits: the hand-draw modifiers (either
    /// sign) and the restriction affixes. These are what the passive slot
    /// samples at PassiveRelicChancePercent.
    /// </summary>
    public IReadOnlyList<EffectFragment> PassiveEffects { get; }
    /// <summary>
    /// Strictly-beneficial passives (EffectFragment.IsBenefit). Split out of
    /// PassiveEffects because they are gated by their own, much smaller roll
    /// (RelicGenerator.BenefitRelicChancePercent, user order 2026-09-17): left
    /// in the passive pool they would appear at the passive rate, and the
    /// passive draw is uniform (it does not consult WeightOf), so a weight
    /// could not hold them back.
    /// </summary>
    public IReadOnlyList<EffectFragment> BenefitEffects { get; }
    public int SupportedAtomCount { get; }

    /// <summary>Stable fingerprint of the fragment pool contents (cache-key input).</summary>
    public string Fingerprint { get; }

    private RelicFragmentPool(IReadOnlyList<TriggerFragment> triggers,
        IReadOnlyList<EffectFragment> triggeredEffects,
        IReadOnlyList<EffectFragment> passiveEffects,
        IReadOnlyList<EffectFragment> benefitEffects,
        int supportedAtomCount)
    {
        Triggers = triggers;
        TriggeredEffects = triggeredEffects;
        PassiveEffects = passiveEffects;
        BenefitEffects = benefitEffects;
        SupportedAtomCount = supportedAtomCount;
        var sb = new System.Text.StringBuilder();
        foreach (var t in triggers) sb.Append('T').Append(t.Key).Append(';');
        foreach (var e in triggeredEffects) sb.Append('E').Append(e.Key).Append(';');
        foreach (var e in passiveEffects) sb.Append('P').Append(e.Key).Append(';');
        foreach (var e in benefitEffects) sb.Append('B').Append(e.Key).Append(';');
        Fingerprint = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    /// <summary>
    /// Ledger fixes are applied first (they carry the hand-verified
    /// correction), then the atom spec is split into its trigger part and its
    /// effect part. An atom with no Trigger in its spec is a passive.
    /// </summary>
    public static RelicFragmentPool Build(RelicAtomPool atoms, RelicLedger ledger)
    {
        var byId = new Dictionary<string, RelicAtom>(StringComparer.Ordinal);
        foreach (RelicAtom atom in atoms.Atoms)
        {
            byId[atom.Id] = atom;
        }

        var triggers = new Dictionary<string, TriggerFragment>(StringComparer.Ordinal);
        var triggered = new Dictionary<string, EffectFragment>(StringComparer.Ordinal);
        var passives = new Dictionary<string, EffectFragment>(StringComparer.Ordinal);
        var benefits = new Dictionary<string, EffectFragment>(StringComparer.Ordinal);
        int supported = 0;

        foreach (LedgerEntry entry in ledger.Supported)
        {
            if (!byId.TryGetValue(entry.AtomId, out RelicAtom? atom))
            {
                throw new InvalidOperationException($"ledger entry {entry.AtomId} has no matching atom");
            }
            supported++;

            Data.LedgerFix fix = entry.Fix ?? new Data.LedgerFix();
            Data.RelicSpec spec = atom.Spec;

            string opcode = fix.Opcode ?? spec.Opcode;
            string variant = fix.Variant ?? spec.Variant ?? "";
            string target = fix.Target ?? spec.Target ?? "";

            var values = new List<ResolvedValue>();
            foreach (Data.RelicValueSlot slot in spec.Values)
            {
                int baseValue = slot.BaseValue;
                if (fix.Values is not null && fix.Values.TryGetValue(slot.Id, out int overridden))
                {
                    baseValue = overridden;
                }
                values.Add(new ResolvedValue(slot.Id, baseValue + slot.Offset, slot.Upgradable));
            }
            // Fix may also inject a value the extractor dropped entirely.
            if (fix.Values is not null)
            {
                foreach (KeyValuePair<string, int> injection in fix.Values)
                {
                    if (values.All(v => !string.Equals(v.Id, injection.Key, StringComparison.Ordinal)))
                    {
                        values.Add(new ResolvedValue(injection.Key, injection.Value, false));
                    }
                }
            }

            bool isPassive = spec.Trigger is null;
            // Condition placement: for triggered atoms the condition gates the
            // trigger (rides the TriggerFragment). For passive atoms there is
            // no trigger, so the condition rides the effect itself - dropping
            // it here would turn "turn 1 only" passives into every-turn ones.
            string? effectCondition = isPassive
                ? (fix.Condition ?? spec.Condition?.Kind)
                : null;
            var effect = new EffectFragment(opcode, variant, target, values, isPassive, atom.Id, effectCondition);
            // Passive fragments split by polarity: strict benefits ride their
            // own 5% gate, everything else (hand-draw modifiers either sign,
            // restriction affixes) rides the passive gate.
            var bucket = !isPassive
                ? triggered
                : effect.IsBenefit ? benefits : passives;
            // Dedup: two atoms with identical effect shapes collapse (the
            // later one still validated the ledger, nothing is silently lost).
            bucket[effect.Key] = effect;

            if (!isPassive)
            {
                string triggerKind = fix.Trigger ?? spec.Trigger!.Kind;
                string condition = fix.Condition ?? spec.Condition?.Kind ?? "";
                var fragment = new TriggerFragment(triggerKind, condition.Length == 0 ? null : condition, atom.Id);
                triggers[fragment.Key] = fragment;
            }
        }

        return new RelicFragmentPool(
            triggers.Values.OrderBy(t => t.Key, StringComparer.Ordinal).ToArray(),
            triggered.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToArray(),
            passives.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToArray(),
            benefits.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToArray(),
            supported);
    }
}
