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
    /// </summary>
    public const string SeedVersion = "relics-v3";

    /// <summary>Chance a slot samples a second effect (both bound to the trigger).</summary>
    private const int TwoEffectChancePercent = 30;

    /// <summary>Chance a slot is a passive-only relic (modifier fragments, no trigger).</summary>
    private const int PassiveRelicChancePercent = 15;

    /// <summary>
    /// Downside fragments (EffectFragment.IsDownside) weigh 140 vs 100 for
    /// everything else: their per-pick share is 1.4x uniform, i.e. their
    /// appearance probability is raised 40% relative to the pre-downside
    /// distribution (user order 2026-09-15). Deliberately NOT a config key -
    /// no config may participate in generation (see GeneratedRelicDefinition).
    /// </summary>
    private const int DownsideWeight = 140;
    private const int NormalWeight = 100;

    /// <summary>
    /// Generation-policy exclusions.
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
    /// </summary>
    private static bool Excluded(TriggerFragment? trigger, EffectFragment effect) =>
        (trigger is not null
            && trigger.Kind == "gold_gained"
            && effect.Opcode == "gain_gold")
        || (effect.Opcode == "gain_max_hp"
            && (trigger is null || trigger.Kind != "obtained"));

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

            if (pool.PassiveEffects.Count > 0 && random.Next(100) < PassiveRelicChancePercent)
            {
                EffectFragment passive = PickUniquely(random, pool.PassiveEffects,
                    e => usedPassives.Add(e.ShapeKey), e => !usedPassives.Contains(e.ShapeKey) && !Excluded(null, e))
                    ?? pool.PassiveEffects[random.Next(pool.PassiveEffects.Count)];
                usedPassives.Add(passive.ShapeKey);
                effects.Add(passive);
            }
            else
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

            var (adj, noun) = PickName(random, usedNames);

            var definition = new GeneratedRelicDefinition(slot, rarity, adj, noun, trigger, effects);

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
                if (pool.PassiveEffects.Count > 0 && random.Next(100) < PassiveRelicChancePercent)
                {
                    var passive = pool.PassiveEffects[random.Next(pool.PassiveEffects.Count)];
                    effects.Add(passive);
                }
                else
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
                (adj, noun) = PickName(random, usedNames);
                definition = new GeneratedRelicDefinition(slot, rarity, adj, noun, trigger, effects);
                retries++;
            }

            usedFingerprints.Add(definition.Fingerprint);
            usedNames.Add(definition.NameEn);
            results.Add(definition);
        }

        return results;
    }

    private static (string en, string zhs) PickName(DeterministicRandom random, ISet<string> used)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            int adj = random.Next(24);
            int noun = random.Next(24);
            string en = RelicText.NameEn(adj, noun);
            if (used.Add(en))
            {
                return (en, RelicText.NameZhs(adj, noun));
            }
        }
        // 24x24 space exhausted within a run (cannot happen at 60 slots);
        // fall back to a suffixed unique name rather than looping forever.
        string fallback = RelicText.NameEn(random.Next(24), random.Next(24)) + " " + used.Count;
        used.Add(fallback);
        return (fallback, RelicText.NameZhs(random.Next(24), random.Next(24)));
    }

    private static T? PickUniquely<T>(
        DeterministicRandom random,
        IReadOnlyList<T> pool,
        Func<T, bool> markUsed,
        Func<T, bool> eligible)
        where T : class
        => PickWeightedUniquely(random, pool, markUsed, eligible, _ => NormalWeight);

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

    private static int WeightOf(EffectFragment effect) =>
        effect.IsDownside ? DownsideWeight : NormalWeight;

    private const string ModId = "AutoAnthonyRelics";
}
