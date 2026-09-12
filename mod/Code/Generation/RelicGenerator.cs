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
            for (int i = 0; i < Effects.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(RelicText.EffectEn(Effects[i]));
            }
            return sb.ToString();
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
            for (int i = 0; i < Effects.Count; i++)
            {
                sb.Append(RelicText.EffectZhs(Effects[i]));
            }
            return sb.ToString();
        }
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
    public const string SeedVersion = "relics-v1";

    /// <summary>Chance a slot samples a second effect (both bound to the trigger).</summary>
    private const int TwoEffectChancePercent = 30;

    /// <summary>Chance a slot is a passive-only relic (modifier fragments, no trigger).</summary>
    private const int PassiveRelicChancePercent = 15;

    /// <summary>
    /// A gain_gold effect on a gold_gained trigger would recurse (gaining gold
    /// grants gold). The generation-time ban is the v1 recursion boundary;
    /// richer limiter semantics are future work and tracked in the fidelity
    /// ledger.
    /// </summary>
    private static bool Forbidden(TriggerFragment? trigger, EffectFragment effect) =>
        trigger is not null
        && trigger.Kind == "gold_gained"
        && effect.Opcode == "gain_gold";

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
                    e => usedPassives.Add(e.ShapeKey), e => !usedPassives.Contains(e.ShapeKey))
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
                    EffectFragment? picked = PickUniquely(random, pool.TriggeredEffects,
                        e => effects.Exists(x => x.ShapeKey == e.ShapeKey),
                        e => !Forbidden(trigger, e) && effects.All(x => x.ShapeKey != e.ShapeKey));
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
                        EffectFragment? picked = PickUniquely(random, pool.TriggeredEffects,
                            e => effects.Exists(x => x.ShapeKey == e.ShapeKey),
                            e => !Forbidden(trigger, e) && effects.All(x => x.ShapeKey != e.ShapeKey));
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
    {
        // Uniform over the eligible subset, no replacement within the relic.
        var candidates = new List<int>();
        for (int i = 0; i < pool.Count; i++)
        {
            if (eligible(pool[i]))
            {
                candidates.Add(i);
            }
        }
        if (candidates.Count == 0)
        {
            return null;
        }
        T picked = pool[candidates[random.Next(candidates.Count)]];
        markUsed(picked);
        return picked;
    }

    private const string ModId = "AutoAnthonyRelics";
}
