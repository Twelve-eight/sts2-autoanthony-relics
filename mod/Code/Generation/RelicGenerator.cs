using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace AutoAnthonyRelics.Generation;

/// <summary>
/// One generated relic: an independently sampled trigger fragment plus
/// independently sampled effect fragment(s). All effects hang off the single
/// trigger (TriggerIndex semantics collapse to "bound" here - relics have no
/// OnPlay, so unlike cards there is no implicit trigger to leave effects
/// unbound to; passive relics instead sample from the passive pool).
///
/// NO CONFIG PARTICIPATES IN GENERATION. The definition is a pure function of
/// (mod id, version, seed, slot). That is the structural fix for the
/// live-config-in-definition-key defect class: preferences only gate whether
/// the mod acts, never what a generated relic is, so saves/reconnects
/// regenerate the identical relic from the engine-restored run seed.
/// </summary>
public sealed record GeneratedRelicDefinition(
    int Slot,
    RelicRarity Rarity,
    string NameEn,
    string NameZhs,
    TriggerFragment? Trigger,
    IReadOnlyList<EffectFragment> Effects)
{
    public string DescriptionEn
    {
        get
        {
            var sb = new StringBuilder();
            if (Trigger is not null)
            {
                sb.Append(RelicText.TriggerEn(Trigger));
            }
            // The effects of one relic are CONCURRENT list items of a single
            // sentence: fragments no longer carry their own sentence period,
            // the join adds comma/"and" per list position and one final
            // period (user report 2026-09-15: "gain 14 Block. gain 1
            // Strength." read as two unrelated sentences).
            sb.Append(JoinEn(Effects));
            return SentenceCase(sb.ToString());
        }
    }

    public string DescriptionZhs
    {
        get
        {
            var sb = new StringBuilder();
            if (Trigger is not null)
            {
                sb.Append(RelicText.TriggerZhs(Trigger));
            }
            sb.Append(JoinZhs(Effects));
            return sb.ToString();
        }
    }

    private static string JoinEn(IReadOnlyList<EffectFragment> effects)
    {
        switch (effects.Count)
        {
            case 0: return "";
            case 1: return RelicText.EffectEn(effects[0]);
            case 2: return RelicText.EffectEn(effects[0]) + " and " + RelicText.EffectEn(effects[1]);
            default:
                var parts = new List<string>(effects.Count);
                foreach (EffectFragment effect in effects)
                {
                    parts.Add(RelicText.EffectEn(effect));
                }
                return string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[^1];
        }
    }

    private static string JoinZhs(IReadOnlyList<EffectFragment> effects)
    {
        var parts = new List<string>(effects.Count);
        foreach (EffectFragment effect in effects)
        {
            parts.Add(RelicText.EffectZhs(effect));
        }
        if (parts.Count == 0)
        {
            return "";
        }
        string body = string.Join("，", parts);
        return body + "。";
    }

    /// <summary>Capitalize the first letter (passive-only relics start with a lowercase verb) and end with one period.</summary>
    private static string SentenceCase(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }
        var chars = trimmed.ToCharArray();
        chars[0] = char.ToUpperInvariant(chars[0]);
        return new string(chars) + ".";
    }

    /// <summary>Identity of the generated content, for run-internal dedup and probe assertions.</summary>
    public string Fingerprint
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Rarity).Append('|');
            if (Trigger is not null)
            {
                sb.Append(Trigger.Kind).Append('/').Append(Trigger.Condition ?? "-").Append('|');
            }
            foreach (EffectFragment effect in Effects)
            {
                sb.Append(effect.Key);
                foreach (ResolvedValue value in effect.Values)
                {
                    sb.Append('|').Append(value.Id).Append('=').Append(value.Value);
                }
            }
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash);
        }
    }

    public string DescribeForLog()
    {
        var sb = new StringBuilder();
        sb.Append("slot ").Append(Slot).Append(" [").Append(Rarity).Append("] \"")
          .Append(NameEn).Append("\" / \"").Append(NameZhs).Append("\" :: ")
          .Append(DescriptionEn);
        return sb.ToString();
    }
}

public static class RelicGenerator
{
    public const int SlotCount = 60;

    /// <summary>
    /// v3: max-HP generation policy (user order 2026-09-15). Eligibility
    /// rules changed (gain_max_hp only under the obtained trigger), so the
    /// version bump keeps the run-registry cache key cleanly separated from
    /// v2 saves' generated sets.
    /// v4: execution-context eligibility (WS-0916-05): combat-scoped opcodes
    /// are no longer paired with triggers that can fire without a combat
    /// context, all_enemies effects no longer with triggers that fire after
    /// the last enemy is dead, and passive relics only carry the opcode the
    /// passive hooks execute. Same reason as v2->v3: the eligibility rules
    /// changed, so existing saves must regenerate rather than be served
    /// relics the executor silently skips.
    /// v5: Ancient restriction/benefit affixes (user order 2026-09-17). The
    /// passive slot became a three-way band (5% benefit / 15% passive / 80%
    /// triggered) decided by one extra roll, and the passive opcode set
    /// widened - both change the RNG consumption shape, so the same seed
    /// would otherwise yield v4 relics while claiming to be v5.
    /// v6: source-derived names (user order 2026-09-17). The name is no longer
    /// drawn from the fragment stream - it is a pure function of the chosen
    /// fragments, on its own "/name" stream. That is still an RNG-shape change
    /// (the old code consumed two Next(24) draws per slot, and on the retry
    /// path consumed them in a different order relative to fragment draws), so
    /// the same reason as v4->v5 applies: a v5 save must regenerate rather
    /// than be served relics whose names were composed under the old scheme.
    /// </summary>
    public const string SeedVersion = "relics-v6";

    /// <summary>Chance a slot samples a second effect (both bound to the trigger).</summary>
    private const int TwoEffectChancePercent = 30;

    /// <summary>Chance a slot is a passive-only relic (modifier fragments, no trigger).</summary>
    private const int PassiveRelicChancePercent = 15;

    /// <summary>
    /// Chance a slot is a strictly-beneficial passive relic (user order
    /// 2026-09-17). Rolled on the SAME draw as <see cref="PassiveRelicChancePercent"/>,
    /// so the two are disjoint slot-level bands: 15% passive, 5% benefit, 80%
    /// triggered. It is a slot-level gate rather than a fragment weight because
    /// the passive draw is uniform - PickUniquely hardcodes NormalWeight - so a
    /// weight could not express "5% of relics" at all.
    /// </summary>
    private const int BenefitRelicChancePercent = 5;

    /// <summary>
    /// Restriction affix -> the offsetting benefit the engine ships it with.
    /// The engine never ships a bare restriction (Ectoplasm pairs gold-loss
    /// with +1 Energy, Fiddle pairs no-draw with +2 hand draw, ..), and the
    /// passive slot holds one fragment, so a restriction is always generated
    /// together with its offset (Excluded rule 5). Without this a generated
    /// restriction relic would be pure cost, which the player is forced to pick
    /// up and cannot remove.
    /// </summary>
    private static readonly Dictionary<string, string> RestrictionOffsets = new(StringComparer.Ordinal)
    {
        ["restrict_gold"] = "modify_max_energy",
        ["restrict_potion"] = "modify_max_energy",
        ["restrict_card_play"] = "modify_max_energy",
        ["restrict_draw"] = "modify_hand_draw",
        ["modify_card_cost"] = "modify_max_energy",
        ["enemy_strength_gain"] = "modify_max_energy",
    };

    private const int NormalWeight = 100;

    /// <summary>
    /// Downside fragments draw UNIFORMLY with everything else (user order
    /// 2026-09-17, superseding the 2026-09-15 order). They previously carried
    /// weight 140 vs 100, i.e. a 1.4x per-pick share that raised their
    /// appearance probability 40% above uniform; that bonus is cancelled.
    /// Sampling is uniform and this class no longer weights anything.
    /// Deliberately NOT a config key - no config may participate in
    /// generation (see GeneratedRelicDefinition).
    /// </summary>

    /// <summary>
    /// Triggers that can fire while the owner has no combat context at all
    /// (<c>Player.PlayerCombatState is null</c>, i.e. no combat has started
    /// yet this run).
    ///
    /// PROOF (engine decompile, sts2.decompiled.cs): PlayerCombatState is
    /// assigned in exactly one place in the whole assembly -
    /// <c>Player.ResetCombatState()</c> -> <c>PlayerCombatState = new
    /// PlayerCombatState(this)</c> - and is never assigned null again, so the
    /// property is null exactly before the run's first combat setup. Of the
    /// trigger kinds in the pool, only these two can fire in that window:
    /// <c>obtained</c> (RelicCmd.Obtain, before any combat) and
    /// <c>gold_gained</c> (Hook.AfterGoldGained from PlayerCmd.GainGold, which
    /// the engine calls out of combat for reward gold and for events like
    /// ColossalFlower / SunkenTreasury). <c>room_entered</c> is gated to
    /// CombatRoom by the executor, and CombatRoom.StartCombat calls SetUpCombat
    /// (-> ResetCombatState) before Hook.AfterRoomEntered, so it always has a
    /// combat context. Every other trigger in the pool is dispatched from the
    /// combat turn loop or from combat creature events.
    /// </summary>
    private static readonly HashSet<string> TriggersWithoutCombatContext = new(StringComparer.Ordinal)
    {
        "obtained", "gold_gained",
    };

    /// <summary>
    /// Triggers that fire only after every enemy of the combat is already dead,
    /// so an effect that resolves against live enemies cannot do anything.
    ///
    /// PROOF (engine decompile): <c>combat_end</c> is Hook.AfterCombatEnd and
    /// <c>combat_victory</c> is Hook.AfterCombatVictory, and both are called
    /// only from CombatManager.EndCombatInternal, which is reached only through
    /// IsCombatEnding - a predicate that returns true only when no primary
    /// enemy is alive. The last primary enemy's death also cascades into every
    /// remaining (secondary) enemy (CreatureCmd.KillWithoutCheckingWinCondition
    /// kills the surviving teammates when they are all secondary), so no enemy
    /// is left alive. The executor resolves <c>all_enemies</c> effects against
    /// <c>Creature.CombatState.HittableEnemies</c>, which is
    /// <c>Enemies.Where(e =&gt; e.IsHittable)</c> and IsHittable is false for a
    /// dead creature - i.e. the empty list. A relic reading "at the end of each
    /// combat, deal 20 damage to ALL enemies" is therefore dead text, which is
    /// the same defect as the out-of-combat skip below.
    /// </summary>
    private static readonly HashSet<string> TriggersAfterEnemiesAreDead = new(StringComparer.Ordinal)
    {
        "combat_end", "combat_victory",
    };

    /// <summary>The fragment target that resolves against live enemies.</summary>
    private const string EnemyTarget = "all_enemies";

    /// <summary>
    /// Opcodes a passive (trigger-less) relic may carry. The executor
    /// implements one override per opcode here (AnthonyRelicModel's passive
    /// modifiers); a passive fragment with any other opcode would be a relic
    /// whose text promises an effect nothing ever runs.
    ///
    /// Was a single constant ("modify_hand_draw") until 2026-09-17. It became
    /// a set when the Ancient restriction/benefit affixes entered the pool:
    /// with one constant every restriction passive was structurally
    /// unsampleable, so rule 3 rejected the entire new pool.
    /// </summary>
    private static readonly HashSet<string> PassiveOpcodes = new(StringComparer.Ordinal)
    {
        "modify_hand_draw",
        "modify_max_energy",
        "restrict_gold",
        "restrict_potion",
        "restrict_card_play",
        "restrict_draw",
        "modify_card_cost",
        "enemy_strength_gain",
        "retain_hand",
        "extra_turn",
        "expand_card_pool",
        "enchant_reward",
    };

    /// <summary>
    /// Generation-policy exclusions. A relic must never promise an effect the
    /// executor cannot run in the context the paired trigger provides, so this
    /// predicate is the generation-time half of the executor's own guards and
    /// is applied at EVERY sampling site (triggered draws, both dedup-retry
    /// draws and the passive path).
    /// 1. Recursion boundary (v1): a gain_gold effect on a gold_gained
    ///    trigger would recurse (gaining gold grants gold); richer limiter
    ///    semantics are future work tracked in the fidelity ledger.
    /// 2. Max-HP policy (user order 2026-09-15): generated relics may raise
    ///    Max HP ONLY under the "obtained" trigger (拾起时). Any other
    ///    trigger - and trigger-less passive sampling - must never carry
    ///    gain_max_hp, so mid-run growth (ChosenCheese combat-end /
    ///    DragonFruit gold-gained gains) is out of policy for generated
    ///    content. The fragments stay in the pool and ledger as data-
    ///    fidelity records; they are merely unsampleable here.
    ///    LoseMaxHp downsides are unaffected.
    /// 3. Passive slots execute only the opcodes in <see cref="PassiveOpcodes"/>
    ///    (one executor override each). Restrictions additionally require their
    ///    engine offset (rule 5).
    /// 4. Execution-context eligibility (WS-0916-05, v4): a combat-scoped
    ///    opcode must not ride a trigger that can fire without a combat
    ///    context (the executor logs "skipped: no combat context" and drops
    ///    it), and an effect that resolves against live enemies must not ride
    ///    a trigger that fires after every enemy is dead. Both rules reject
    ///    pairs only - the fragments stay in the pool, every one of them keeps
    ///    at least one legal trigger, and no weight, budget, candidate order or
    ///    RNG draw changes for the candidates that stay eligible.
    /// 5. Restriction pairing (v5, user order 2026-09-17): a restriction affix
    ///    is sampled only together with the offsetting benefit the engine ships
    ///    it with (see RestrictionOffsets). The engine never ships a bare
    ///    restriction, and the passive slot holds ONE fragment, so the offset
    ///    is what keeps a generated restriction relic from being pure cost.
    /// </summary>
    internal static bool Excluded(TriggerFragment? trigger, EffectFragment effect) =>
        (trigger is not null
            && trigger.Kind == "gold_gained"
            && effect.Opcode == "gain_gold")
        || (effect.Opcode == "gain_max_hp"
            && (trigger is null || trigger.Kind != "obtained"))
        || (trigger is null && !PassiveOpcodes.Contains(effect.Opcode))
        || (trigger is not null
            && TriggersWithoutCombatContext.Contains(trigger.Kind)
            && EffectFragment.CombatScopedOpcodes.Contains(effect.Opcode))
        || (trigger is not null
            && TriggersAfterEnemiesAreDead.Contains(trigger.Kind)
            && effect.Target == EnemyTarget)
        || (trigger is null
            && effect.IsRestriction
            && !RestrictionOffsets.ContainsKey(effect.Opcode));

    public static IReadOnlyList<GeneratedRelicDefinition> Generate(string runSeed, RelicFragmentPool pool)
    {
        if (pool.Triggers.Count == 0 || pool.TriggeredEffects.Count == 0)
        {
            throw new InvalidOperationException("fragment pool is empty; refusing to generate");
        }

        var results = new List<GeneratedRelicDefinition>(SlotCount);
        var usedFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var usedPassives = new HashSet<string>(StringComparer.Ordinal);

        for (int slot = 0; slot < SlotCount; slot++)
        {
            // Per-slot stream: stable against pool-size changes and against
            // adding slots (slot N never shifts slot N+1's stream).
            var random = new DeterministicRandom($"{ModId}/{SeedVersion}/{runSeed}/slot/{slot}");

            RelicRarity rarity = random.Next(100) switch
            {
                < 45 => RelicRarity.Common,
                < 80 => RelicRarity.Uncommon,
                _ => RelicRarity.Rare,
            };

            TriggerFragment? trigger = null;
            var effects = new List<EffectFragment>();

            effects.AddRange(PickPassives(random, pool, usedPassives));

            if (effects.Count == 0)
            {
                trigger = pool.Triggers[random.Next(pool.Triggers.Count)];

                int effectCount = random.Next(100) < TwoEffectChancePercent ? 2 : 1;
                for (int i = 0; i < effectCount; i++)
                {
                    EffectFragment? picked = PickWeightedUniquely(random, pool.TriggeredEffects,
                        e => effects.Exists(x => x.ShapeKey == e.ShapeKey),
                        e => !Excluded(trigger, e) && effects.All(x => x.ShapeKey != e.ShapeKey),
                        WeightOf);
                    if (picked is null)
                    {
                        break; // pool exhausted against the constraints; one-effect relic
                    }
                    effects.Add(picked);
                }
            }

            // Naming runs AFTER the fragments are final (it is a function of
            // them) and on its OWN RNG stream, so it cannot perturb fragment
            // sampling. The fragment stream above is untouched.
            var nameRandom = new DeterministicRandom($"{ModId}/{SeedVersion}/{runSeed}/slot/{slot}/name");
            var (nameEn, nameZhs) = PickName(nameRandom, trigger, effects, usedNames);

            var definition = new GeneratedRelicDefinition(slot, rarity, nameEn, nameZhs, trigger, effects);

            // Cross-run-content dedup: retry a few times so 60 slots stay
            // distinct; the fragment space is large enough that this virtually
            // always succeeds on the first draw.
            int retries = 0;
            while (usedFingerprints.Contains(definition.Fingerprint) && retries < 8)
            {
                random = new DeterministicRandom($"{ModId}/{SeedVersion}/{runSeed}/slot/{slot}/retry/{retries}");
                rarity = random.Next(100) switch
                {
                    < 45 => RelicRarity.Common,
                    < 80 => RelicRarity.Uncommon,
                    _ => RelicRarity.Rare,
                };
                effects.Clear();
                trigger = null;
                effects.AddRange(PickPassives(random, pool, usedPassives));
                if (effects.Count == 0)
                {
                    trigger = pool.Triggers[random.Next(pool.Triggers.Count)];
                    int effectCount = random.Next(100) < TwoEffectChancePercent ? 2 : 1;
                    for (int i = 0; i < effectCount; i++)
                    {
                        EffectFragment? picked = PickWeightedUniquely(random, pool.TriggeredEffects,
                            e => effects.Exists(x => x.ShapeKey == e.ShapeKey),
                            e => !Excluded(trigger, e) && effects.All(x => x.ShapeKey != e.ShapeKey),
                            WeightOf);
                        if (picked is null)
                        {
                            break;
                        }
                        effects.Add(picked);
                    }
                }
                // The retry re-rolls the fragments, so the name must be
                // re-derived from the NEW fragments. It draws from the retry's
                // own "/name" stream for the same reason as above.
                var retryNameRandom = new DeterministicRandom($"{ModId}/{SeedVersion}/{runSeed}/slot/{slot}/retry/{retries}/name");
                var (retryEn, retryZhs) = PickName(retryNameRandom, trigger, effects, usedNames);
                definition = new GeneratedRelicDefinition(slot, rarity, retryEn, retryZhs, trigger, effects);
                retries++;
            }

            usedFingerprints.Add(definition.Fingerprint);
            results.Add(definition);
        }

        return results;
    }

    /// <summary>
    /// Pick the passive fragments for one slot, or null to generate a triggered
    /// relic instead.
    ///
    /// Bands are disjoint and decided by ONE roll, so the slot rates are the
    /// constants themselves: &lt; 5 benefit, &lt; 20 passive (i.e. 5% + 15%),
    /// else triggered. A benefit relic is a single strict-benefit fragment; a
    /// passive relic is either one non-benefit passive (hand-draw modifier) or
    /// a restriction together with its offsetting benefit.
    ///
    /// Returns the chosen fragments (empty when the pools are exhausted, which
    /// makes the caller fall through to the triggered path).
    /// </summary>
    private static List<EffectFragment> PickPassives(
        DeterministicRandom random,
        RelicFragmentPool pool,
        ISet<string> usedPassives)
    {
        var chosen = new List<EffectFragment>();
        if (pool.PassiveEffects.Count == 0 && pool.BenefitEffects.Count == 0)
        {
            return chosen;
        }

        int roll = random.Next(100);
        bool wantBenefit = roll < BenefitRelicChancePercent;
        bool wantPassive = roll < BenefitRelicChancePercent + PassiveRelicChancePercent;

        if (wantBenefit)
        {
            EffectFragment? benefit = PickUniquely(random, pool.BenefitEffects,
                e => usedPassives.Add(e.ShapeKey),
                e => !usedPassives.Contains(e.ShapeKey) && !Excluded(null, e))
                ?? PickEligiblePassive(random, pool.BenefitEffects);
            if (benefit is not null)
            {
                usedPassives.Add(benefit.ShapeKey);
                chosen.Add(benefit);
                return chosen;
            }
            // No benefit left this run: fall through to the passive band rather
            // than silently turning the slot into a triggered relic.
            wantPassive = true;
        }

        if (!wantPassive)
        {
            return chosen;
        }

        // Restriction pairing: a restriction fragment is only ever taken with
        // its engine offset, so the relic is never pure cost. Both fragments
        // come from the pools and are marked used like any other passive.
        //
        // The pair is drawn UNIFORMLY over the eligible pairs (one Next(int)),
        // not first-match: a first-match scan would ignore the RNG and let
        // restrictions consume the whole passive band, starving the hand-draw
        // passives - a far larger bias than the weight this change cancels.
        var pairs = new List<(EffectFragment Restriction, EffectFragment Offset)>();
        foreach (EffectFragment candidate in pool.PassiveEffects)
        {
            if (!candidate.IsRestriction
                || usedPassives.Contains(candidate.ShapeKey)
                || Excluded(null, candidate)
                || !RestrictionOffsets.TryGetValue(candidate.Opcode, out string? offsetOpcode))
            {
                continue;
            }
            // The offset must be the BENEFICIAL side of its opcode. modify_hand_draw
            // carries both signs (BagOfPreparation +2, BigMushroom -2) and the
            // pool is ordered by Key, so a plain FirstOrDefault would pick the
            // -2 downside and ship "no draw" + "draw 2 fewer". Amount > 0
            // selects the benefit; the other offset opcode (modify_max_energy)
            // is positive-only.
            EffectFragment? offset = pool.BenefitEffects.Concat(pool.PassiveEffects).FirstOrDefault(e =>
                string.Equals(e.Opcode, offsetOpcode, StringComparison.Ordinal)
                && e.Amount > 0
                && !Excluded(null, e));
            if (offset is not null)
            {
                pairs.Add((candidate, offset));
            }
        }
        if (pairs.Count > 0)
        {
            var (restriction, offset) = pairs[random.Next(pairs.Count)];
            // Only the RESTRICTION is consumed. The offset is a generic
            // engine benefit (+1 Energy, +2 hand draw) whose atoms all collapse
            // to one fragment per opcode, so consuming it would exhaust the
            // offset pool after a single restriction relic and cap the whole
            // run at one restriction - leaving four of the six unsampleable.
            usedPassives.Add(restriction.ShapeKey);
            chosen.Add(restriction);
            chosen.Add(offset);
            return chosen;
        }

        // Final fallback: a plain passive (hand-draw modifier either sign).
        // Restrictions are excluded here on purpose - they may only enter
        // through the pairing branch above, otherwise this uniform draw would
        // emit a bare restriction with no offset.
        EffectFragment? passive = PickUniquely(random, pool.PassiveEffects,
            e => usedPassives.Add(e.ShapeKey),
            e => !e.IsRestriction && !usedPassives.Contains(e.ShapeKey) && !Excluded(null, e))
            ?? PickEligiblePassive(random, pool.PassiveEffects.Where(e => !e.IsRestriction).ToList());
        if (passive is not null)
        {
            usedPassives.Add(passive.ShapeKey);
            chosen.Add(passive);
        }
        return chosen;
    }

    /// <summary>
    /// Derive the relic's name from the source relics its atoms came from.
    ///
    /// This is the AAR equivalent of the original Auto-Anthonyology's card-name
    /// scheme (`ChaosCardGenerator.CardNameGenerator`). The original does NOT
    /// name from the surviving source atom: `BuildSourcePool` builds a pool of
    /// EVERY source recipe weighted by similarity to the generated card, then
    /// samples TWO `NameParts` from it and composes their chunks
    /// (`ComposeChinese` / `ComposeEnglish`). Two properties matter here:
    ///  * the pool is over all recipes with a floor weight of 1 (`weight = 1 +
    ///    ..`), so it is never exhausted and a second morpheme always exists;
    ///  * a matching source can outweigh that floor by up to 2283:1, so the two
    ///    sampled parts are drawn overwhelmingly from the sources the card was
    ///    actually built from - which is what makes the origin visible to a
    ///    reader. (Ratio derived from the formula's own bounds, not measured:
    ///    DiceSimilarity is 200*|AB|/(|A|+|B|) <= 100 for identical sets.)
    ///
    /// The AAR analog: "similar to the generated card" becomes exact provenance
    /// (which source relic contributed an atom), so the dominant pool is simply
    /// the relic's own provenance set. The floor survives only to supply a second
    /// morpheme for a relic whose fragments trace to a SINGLE source (a passive
    /// relic, which has one fragment); such a relic still always gets its real
    /// source as the stem, so the connection stays visible.
    ///
    /// PROVENANCE IS THE WHOLE SET, not a survivor field. Fragments fold by
    /// effect shape (RelicFragmentPool.Build), so e.g. modify_max_energy has
    /// seven contributing relics; naming from a last-write-wins `SourceAtom`
    /// would produce a name that LOOKS sourced but is arbitrary.
    ///
    /// RNG: the caller gives this its OWN stream (a "/name" suffix), so naming
    /// consumes nothing from the fragment stream and fragment sampling stays
    /// byte-identical. Both the pool order and the weights are pure functions of
    /// the fragment set, never of dictionary enumeration order.
    /// </summary>
    private static (string en, string zhs) PickName(
        DeterministicRandom random,
        TriggerFragment? trigger,
        IReadOnlyList<EffectFragment> effects,
        ISet<string> used)
    {
        // The relic's own provenance: every source relic that contributed an
        // atom to the trigger or any effect. Ordinal-sorted, so the candidate
        // order cannot depend on the ledger's array order.
        var provenance = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string atom in trigger?.SourceAtoms ?? Array.Empty<string>())
        {
            provenance.Add(SourceOf(atom));
        }
        foreach (EffectFragment effect in effects)
        {
            foreach (string atom in effect.SourceAtoms)
            {
                provenance.Add(SourceOf(atom));
            }
        }
        if (provenance.Count == 0)
        {
            throw new InvalidOperationException("generated relic has no source provenance to name from");
        }

        var stems = new List<string>(provenance);
        // Stem: always a REAL origin, so the connection is visible in every
        // generated name (the requirement) rather than only most of the time.
        // Tail: drawn from the full source pool, provenance-weighted, exactly as
        // the original samples its second NameParts from a pool whose matching
        // sources dominate a floor of 1. The floor is what keeps the pair space
        // large enough to stay unique when several relics in one run share the
        // same provenance set (the probe asserts 60 distinct names per run).
        var tailPool = new List<(string Source, int Weight)>(RelicText.AllSources.Count);
        long totalTailWeight = 0;
        foreach (string source in RelicText.AllSources)
        {
            int weight = provenance.Contains(source) ? ProvenanceWeight : 1;
            tailPool.Add((source, weight));
            totalTailWeight += weight;
        }

        for (int attempt = 0; attempt < 64; attempt++)
        {
            string stem = stems[random.Next(stems.Count)];
            string tail = WeightedSource(random, tailPool, totalTailWeight);
            if (string.Equals(stem, tail, StringComparison.Ordinal))
            {
                continue; // would read "Gremlin Gremlin"
            }
            RelicText.NameMorpheme stemMorpheme = RelicText.Morpheme(stem);
            RelicText.NameMorpheme tailMorpheme = RelicText.Morpheme(tail);
            string en = RelicText.ComposeEn(stemMorpheme.En, tailMorpheme.En);
            string zhs = RelicText.ComposeZhs(stemMorpheme.Zhs, tailMorpheme.Zhs);
            if (used.Add(NameKey(en, zhs)))
            {
                return (en, zhs);
            }
        }

        // Deterministic sweep: the weighted draws are probabilistic, so a
        // collision-free pair must stay reachable when they keep colliding.
        foreach (string stem in stems)
        {
            foreach ((string tail, int _) in tailPool)
            {
                if (string.Equals(stem, tail, StringComparison.Ordinal))
                {
                    continue;
                }
                RelicText.NameMorpheme stemMorpheme = RelicText.Morpheme(stem);
                RelicText.NameMorpheme tailMorpheme = RelicText.Morpheme(tail);
                string en = RelicText.ComposeEn(stemMorpheme.En, tailMorpheme.En);
                string zhs = RelicText.ComposeZhs(stemMorpheme.Zhs, tailMorpheme.Zhs);
                if (used.Add(NameKey(en, zhs)))
                {
                    return (en, zhs);
                }
            }
        }

        // Unreachable: at least 45 x 44 = 1980 distinct names against 60 slots.
        throw new InvalidOperationException(
            $"name space exhausted after {used.Count} names; {RelicText.AllSources.Count} morphemes are not enough for {SlotCount} slots");
    }

    /// <summary>
    /// Dedup key for a composed name. Both languages participate: the probe
    /// asserts English uniqueness, and a ZHS collision with distinct English
    /// (were the tables ever to hold a duplicate morpheme) would still show two
    /// identically-named relics to a Chinese player.
    /// </summary>
    private static string NameKey(string en, string zhs) => en + "\u0000" + zhs;

    /// <summary>One weighted draw (cumulative weights, as the original's WeightedNameSourcePool.Sample).</summary>
    private static string WeightedSource(DeterministicRandom random, IReadOnlyList<(string Source, int Weight)> pool, long totalWeight)
    {
        long roll = random.Next((int)Math.Min(totalWeight, int.MaxValue));
        long accumulated = 0;
        foreach ((string source, int weight) in pool)
        {
            accumulated += weight;
            if (roll < accumulated)
            {
                return source;
            }
        }
        return pool[^1].Source;
    }

    /// <summary>
    /// Weight of a source relic that actually contributed an atom to this relic.
    /// The original lets a matching source dominate the floor by up to 2283:1
    /// (`weight = 1 + (dice*1000 + dice)/50 + primary*80 + sameTypeSameCost*30 +
    /// sameType*10`, with DiceSimilarity topping out at 100 for identical sets);
    /// here the match is exact provenance rather than a similarity score, so one
    /// strong weight captures the same intent.
    /// </summary>
    private const int ProvenanceWeight = 64;

    /// <summary>The source relic of an atom id (<c>Akabeko#AfterSideTurnStart#0</c>).</summary>
    private static string SourceOf(string atomId)
    {
        int hash = atomId.IndexOf('#');
        return hash < 0 ? atomId : atomId[..hash];
    }

    private static T? PickUniquely<T>(
        DeterministicRandom random,
        IReadOnlyList<T> pool,
        Func<T, bool> markUsed,
        Func<T, bool> eligible)
        where T : class
        => PickWeightedUniquely(random, pool, markUsed, eligible, _ => NormalWeight);

    /// <summary>
    /// Uniform draw over the passives that pass <see cref="Excluded"/>, or null
    /// when the pool holds no eligible passive at all.
    ///
    /// This exists because the passive fallback used to index the raw pool
    /// (<c>pool.PassiveEffects[random.Next(count)]</c>), bypassing eligibility
    /// entirely: it could hand a relic a fragment no passive hook executes,
    /// i.e. text that promises an effect nothing runs. An ineligible passive is
    /// never returned - the caller generates a triggered relic instead.
    ///
    /// Draw cost matches the raw index it replaces: exactly one
    /// <see cref="DeterministicRandom.Next(int)"/> when any eligible passive
    /// exists, so a slot that stays on the passive path keeps its stream.
    /// </summary>
    private static EffectFragment? PickEligiblePassive(
        DeterministicRandom random,
        IReadOnlyList<EffectFragment> passives)
    {
        var candidates = new List<EffectFragment>(passives.Count);
        foreach (EffectFragment passive in passives)
        {
            if (!Excluded(null, passive))
            {
                candidates.Add(passive);
            }
        }
        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }

    /// <summary>
    /// Weighted variant of PickUniquely: uniform over total weight, no
    /// replacement within the relic. Integer weights only - DeterministicRandom
    /// has no floating API and integer arithmetic keeps the stream identical
    /// across platforms.
    /// </summary>
    private static T? PickWeightedUniquely<T>(
        DeterministicRandom random,
        IReadOnlyList<T> pool,
        Func<T, bool> markUsed,
        Func<T, bool> eligible,
        Func<T, int> weightOf)
        where T : class
    {
        var candidates = new List<int>();
        long totalWeight = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            if (eligible(pool[i]))
            {
                candidates.Add(i);
                totalWeight += weightOf(pool[i]);
            }
        }
        if (candidates.Count == 0)
        {
            return null;
        }
        T picked = pool[candidates[^1]];
        if (totalWeight > 0)
        {
            int roll = random.Next((int)Math.Min(totalWeight, int.MaxValue));
            int accumulated = 0;
            foreach (int index in candidates)
            {
                accumulated += weightOf(pool[index]);
                if (roll < accumulated)
                {
                    picked = pool[index];
                    break;
                }
            }
        }
        markUsed(picked);
        return picked;
    }

    /// <summary>
    /// Uniform for every fragment (user order 2026-09-17): the downside bonus
    /// (140 vs 100) was cancelled, so this is now a constant. It is kept as a
    /// hook rather than deleted because PickWeightedUniquely's signature and
    /// the deterministic draw sequence are built around it - replacing it with
    /// a literal would be a larger change for no behavioural difference.
    /// </summary>
    private static int WeightOf(EffectFragment effect) => NormalWeight;

    private const string ModId = "AutoAnthonyRelics";
}
