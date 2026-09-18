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
public sealed record TriggerFragment(string Kind, string? Condition, IReadOnlyList<string> SourceAtoms)
{
    public string Key => $"{Kind}|{Condition ?? "-"}";
}

public sealed record EffectFragment(
    string Opcode,
    string? Variant,
    string? Target,
    IReadOnlyList<ResolvedValue> Values,
    bool IsPassive,
    IReadOnlyList<string> SourceAtoms,
    string? Condition = null)
{
    /// <summary>
    /// Which authored pool this fragment came from. Set from the LEDGER SECTION
    /// it was listed under (supported vs extraSupported), never inferred from
    /// the opcode: the settings toggle must be able to switch a pool off as
    /// data, and the same opcode could legitimately appear in both pools later.
    /// </summary>
    public FragmentPoolKind Pool { get; init; } = FragmentPoolKind.Core;

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
        // Extra pool (user order 2026-09-18): hand Ethereal exhausts the card at
        // end of turn, which is a pure cost for the owner.
        "ethereal_hand_card",
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
    /// A NEGATIVE effect: something a player would not want, i.e. what the
    /// "disable all negative effects" option removes (user order 2026-09-19).
    ///
    /// Two disjoint sources, and the definition is their union on purpose:
    /// - <see cref="DownsideOpcodes"/>: triggered effects that hurt the owner
    ///   (lose HP / max HP / gold, add a curse, hand Ethereal).
    /// - <see cref="RestrictionOpcodes"/>: the Ancient veto/penalty affixes.
    ///   They are negative even though the generator always ships them WITH an
    ///   offsetting benefit (RestrictionOffsets) - the offset is what makes the
    ///   pair fair, not what makes the restriction stop being a cost. A player
    ///   asking for "no negative effects" is asking to not receive a
    ///   gold-gain veto at all, not to receive it with a consolation prize.
    ///
    /// DELIBERATELY NOT NEGATIVE - <c>retain_hand</c> (RunicPyramid's
    /// ShouldFlush returning false, i.e. "your hand is not discarded at end of
    /// turn"). It is a veto-shaped hook like the restrictions, so it looks like
    /// one at a glance, but it only ever KEEPS cards the player would otherwise
    /// lose. The user called this out explicitly. It stays in
    /// <see cref="BenefitOpcodes"/>.
    ///
    /// Also not negative: <c>modify_card_cost</c> is listed in
    /// RestrictionOpcodes (it is SpikedGauntlets' "Powers cost +1" veto, paired
    /// with +1 Energy), and <c>enemy_strength_gain</c> is
    /// PhilosophersStone's "enemies gain Strength" (paired with +1 Energy).
    /// Both are genuinely negative to their owner.
    ///
    /// SIGN-NEGATIVE <c>modify_hand_draw</c> IS negative. The opcode carries
    /// both signs and polarity is per-fragment, not per-opcode:
    /// BagOfPreparation is +2 (a benefit) while BigMushroom's ledger fix sets
    /// amount = -2 ("少抽2张牌", a real cost - the generator's own
    /// RestrictionOffsets comment already calls it "the -2 downside"). A purely
    /// opcode-keyed set would have missed it, which is why this is a value test.
    /// </summary>
    public bool IsNegative => IsDownside || IsRestriction
        || (string.Equals(Opcode, "modify_hand_draw", StringComparison.Ordinal) && Amount < 0);

    /// <summary>
    /// Extra-pool opcodes (user order 2026-09-18) whose target is the owner's
    /// HAND, i.e. the effects that need a populated hand to do anything.
    ///
    /// WHY THIS SET EXISTS: the hand is drawn inside SetupPlayerTurn, so an
    /// effect on the hand is a no-op at any hook that fires before that draw.
    /// Verified in the decompile (CombatManager):
    /// - <c>:594</c> BeforeCombatStart (combat_start) - before StartTurn, no hand.
    /// - <c>:721</c> BeforeSideTurnStart (turn_start_early) - no hand.
    /// - <c>:778</c> awaits the SetupPlayerTurn task, whose <c>:924</c> draws.
    /// - <c>:783</c> AfterSideTurnStart (turn_start) - hand IS present.
    /// So `turn_start` may run these inline, while `combat_start` must defer to
    /// the post-draw pass. QuriousCraftingRelics hit the same wall and moved its
    /// combat-start enchants to AfterPlayerTurnStartLate
    /// (ChaosRelicModel.cs:519 "the hand is empty and every enchant loop
    /// iterated zero cards").
    ///
    /// The generator may only pair these opcodes with
    /// <see cref="HandEffectTriggers"/>; anything else would ship a relic whose
    /// text promises an effect no hook ever runs.
    /// </summary>
    public static readonly HashSet<string> HandEffectOpcodes = new(StringComparer.Ordinal)
    {
        "retain_hand_card", "sly_hand_card", "ethereal_hand_card", "enchant_hand",
    };

    /// <summary>
    /// The only triggers a <see cref="HandEffectOpcodes"/> effect may ride.
    /// <c>turn_start</c> runs them inline (hand present). <c>combat_start</c>
    /// runs them from the deferred post-draw pass, gated to the first turn so
    /// the source's once-per-combat meaning survives - which is exactly what
    /// QuriousCraftingRelics does with its <c>turn &lt;= 1</c> check.
    /// <c>turn_start_early</c> is excluded because no hand exists there.
    /// </summary>
    public static readonly HashSet<string> HandEffectTriggers = new(StringComparer.Ordinal)
    {
        "turn_start", "combat_start",
    };

    /// <summary>
    /// Extra-pool opcodes whose target is the owner's MASTER DECK. The deck
    /// exists outside combat, so these run inline at their trigger - but they
    /// are restricted to <c>obtained</c>, mirroring the source content
    /// (QuriousCraftingRelics' <c>X_PICKUP_*</c> templates fire on pickup) and
    /// keeping a per-turn deck-wide enchant from being reachable.
    /// </summary>
    public static readonly HashSet<string> DeckEffectOpcodes = new(StringComparer.Ordinal)
    {
        "enchant_deck",
    };

    /// <summary>The only trigger a <see cref="DeckEffectOpcodes"/> effect may ride.</summary>
    public static readonly HashSet<string> DeckEffectTriggers = new(StringComparer.Ordinal)
    {
        "obtained",
    };

    public bool IsHandEffect => HandEffectOpcodes.Contains(Opcode);

    public bool IsDeckEffect => DeckEffectOpcodes.Contains(Opcode);

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
        // Extra pool (user order 2026-09-18). These reach into the owner's HAND,
        // which only exists inside a combat: without this rule the generator
        // could pair them with `obtained` or `gold_gained` (both reachable with
        // no PlayerCombatState) and the relic would carry dead text. The
        // `enchant_deck` variants are deliberately NOT listed - they operate on
        // the deck and are meant to run at obtain time, outside combat.
        "retain_hand_card", "sly_hand_card", "ethereal_hand_card", "enchant_hand",
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
/// <summary>Which authored pool a fragment belongs to (user order 2026-09-18).</summary>
public enum FragmentPoolKind
{
    /// <summary>The original pool: vanilla + Ancient atoms from the main ledger.</summary>
    Core,

    /// <summary>The opt-in pool ported from QuriousCraftingRelics' extra catalog.</summary>
    Extra,
}

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
    ///
    /// <paramref name="includeExtraPool"/> joins the ledger's
    /// <c>extraSupported</c> atoms (user order 2026-09-18). The flag is a
    /// generation input, so it MUST reach the pool fingerprint - otherwise
    /// toggling it would serve a stale cached definition set. Callers get that
    /// for free because <see cref="Fingerprint"/> is computed from the built
    /// contents (see its own note).
    /// </summary>
    public static RelicFragmentPool Build(RelicAtomPool atoms, RelicLedger ledger, bool includeExtraPool)
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

        // Core first, then extra: identical shapes across the two pools fold
        // into ONE fragment with unioned provenance (the dedup below), so the
        // extra pool can never silently duplicate a core effect. A fragment
        // both pools contribute stays Core - the extra pool is the opt-in
        // ADDITION, so a shape the core pool already ships must not vanish when
        // the extra pool is switched off.
        foreach ((LedgerEntry entry, FragmentPoolKind kind) in EnumerateEntries(ledger, includeExtraPool))
        {
            if (!byId.TryGetValue(entry.AtomId, out RelicAtom? atom))
            {
                throw new InvalidOperationException($"ledger entry {entry.AtomId} has no matching atom");
            }
            if (kind == FragmentPoolKind.Core)
            {
                supported++;
            }
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
                // SORTED, not raw dictionary order. `fix.Values` is a
                // Dictionary<string,int>, and .NET randomizes string hash codes
                // per PROCESS, so its enumeration order is not guaranteed stable
                // across runs. The order here becomes the order of `values`,
                // which feeds ValuesKey -> the fragment Key -> the relic
                // fingerprint, i.e. generation output. Today every ledger fix
                // carries exactly ONE value key so the order is trivial, but a
                // future entry with two would make generation process-dependent
                // (same seed, different relics) - a defect that would be very
                // hard to attribute. Ordinal sort removes the class outright and
                // is a no-op for the current single-key data.
                foreach (KeyValuePair<string, int> injection in
                         fix.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
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
            var effect = new EffectFragment(opcode, variant, target, values, isPassive, new[] { atom.Id }, effectCondition)
            {
                Pool = kind,
            };
            // Passive fragments split by polarity: strict benefits ride their
            // own 5% gate, everything else (hand-draw modifiers either sign,
            // restriction affixes) rides the passive gate.
            var bucket = !isPassive
                ? triggered
                : effect.IsBenefit ? benefits : passives;
            // Dedup: two atoms with identical effect shapes collapse into ONE
            // fragment (the later one still validated the ledger, nothing is
            // silently lost) - but their PROVENANCE is unioned, not
            // overwritten. A last-write-wins survivor would make the fragment
            // claim an arbitrary single origin: modify_max_energy is
            // contributed by seven Ancient relics and gain_energy by two
            // (GremlinHorn, Lantern), so a name derived from a survivor field
            // would look sourced while actually being arbitrary. The name is
            // the player-visible payoff of provenance, so provenance must be
            // the whole set.
            if (bucket.TryGetValue(effect.Key, out EffectFragment? existing))
            {
                effect = effect with
                {
                    SourceAtoms = Union(existing.SourceAtoms, atom.Id),
                    Pool = existing.Pool == FragmentPoolKind.Core ? FragmentPoolKind.Core : kind,
                };
            }
            bucket[effect.Key] = effect;

            if (!isPassive)
            {
                string triggerKind = fix.Trigger ?? spec.Trigger!.Kind;
                string condition = fix.Condition ?? spec.Condition?.Kind ?? "";
                var fragment = new TriggerFragment(triggerKind, condition.Length == 0 ? null : condition, new[] { atom.Id });
                // Same union rule as the effects: `obtained` folds 15 atoms and
                // `turn_start|first_turn` folds 4.
                if (triggers.TryGetValue(fragment.Key, out TriggerFragment? existingTrigger))
                {
                    fragment = fragment with { SourceAtoms = Union(existingTrigger.SourceAtoms, atom.Id) };
                }
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

    /// <summary>
    /// The ledger entries to build from, each tagged with its pool: the core
    /// <c>supported</c> list, then - only when the extra pool is enabled - the
    /// <c>extraSupported</c> list. Ordering is stable (core before extra) so the
    /// dedup's unioned provenance is a pure function of the data, and so the
    /// built pool is byte-identical across runs for the same inputs.
    /// </summary>
    private static IEnumerable<(LedgerEntry Entry, FragmentPoolKind Kind)> EnumerateEntries(
        RelicLedger ledger,
        bool includeExtraPool)
    {
        foreach (LedgerEntry entry in ledger.Supported)
        {
            yield return (entry, FragmentPoolKind.Core);
        }
        if (!includeExtraPool)
        {
            yield break;
        }
        foreach (LedgerEntry entry in ledger.ExtraSupported)
        {
            yield return (entry, FragmentPoolKind.Extra);
        }
    }

    /// <summary>
    /// Union of a fragment's accumulated provenance and one new atom id.
    /// Ordinal-sorted so the result is a pure function of the SET, never of
    /// the ledger's array order - the pool is built once and its contents feed
    /// the determinism contract, so an order-dependent list here would be an
    /// order-dependent name downstream.
    /// </summary>
    private static IReadOnlyList<string> Union(IReadOnlyList<string> existing, string added)
    {
        var set = new SortedSet<string>(existing, StringComparer.Ordinal) { added };
        return set.ToArray();
    }
}
