using System.Security.Cryptography;
using System.Text;
using AutoAnthonyRelics.Data;

namespace AutoAnthonyRelics.Generation;

/// <summary>Shell rarity, mirroring the pool's OriginalRarity values.</summary>
public enum GeneratedRarity
{
    Basic,
    Common,
    Uncommon,
    Rare,
    Ancient,
    Unknown,
}

/// <summary>Shell card type, mirroring the pool's Type values.</summary>
public enum GeneratedCardType
{
    Attack,
    Skill,
    Power,
    Unknown,
}

/// <summary>
/// Shell target mode, mirroring the pool's Target values. The original calls
/// this TargetMode with members SingleEnemy / Other.
/// </summary>
public enum GeneratedTargetMode
{
    SingleEnemy,
    Other,
    Unknown,
}

/// <summary>Total parsers for the pool's PascalCase metadata strings.</summary>
internal static class GenerationEnumParsing
{
    public static GeneratedRarity ParseRarity(string? value) => value switch
    {
        "Basic" => GeneratedRarity.Basic,
        "Common" => GeneratedRarity.Common,
        "Uncommon" => GeneratedRarity.Uncommon,
        "Rare" => GeneratedRarity.Rare,
        "Ancient" => GeneratedRarity.Ancient,
        _ => GeneratedRarity.Unknown,
    };

    public static GeneratedCardType ParseCardType(string? value) => value switch
    {
        "Attack" => GeneratedCardType.Attack,
        "Skill" => GeneratedCardType.Skill,
        "Power" => GeneratedCardType.Power,
        _ => GeneratedCardType.Unknown,
    };

    public static GeneratedTargetMode ParseTargetMode(string? value) => value switch
    {
        "SingleEnemy" => GeneratedTargetMode.SingleEnemy,
        "Other" => GeneratedTargetMode.Other,
        _ => GeneratedTargetMode.Unknown,
    };
}

/// <summary>
/// One numeric slot resolved for a concrete card.
///
/// POLICY (user decision, this repository): the value is carried over from the
/// atom's own spec (BaseValue + Offset). The original re-derived values at
/// generation time from EffectBalanceModel / NumericGenerationTuning /
/// PercentageValueTuning, none of which are present in the reused data files.
/// Re-deriving is therefore impossible without inventing a balance table, so we
/// take the original's own "OriginalValueChance" branch to its limit and always
/// keep the original value. See research/fidelity-ledger.md.
/// </summary>
public sealed record ResolvedValue(string Id, int Value, bool Upgradable);

/// <summary>
/// One assembled operation: an atom fragment plus the generation-time decision
/// of which earlier operation (if any) it hangs off.
///
/// TriggerIndex is the mechanism contract's whole point - it is decided HERE, at
/// generation time, by TriggerBinder, and never inherited from the source
/// recipe's TriggerOwner. -1 means "standalone".
/// </summary>
public sealed record GeneratedOperation(
    JoinedFragment Source,
    int TriggerIndex,
    IReadOnlyList<ResolvedValue> Values)
{
    public string Template => Source.Template;
    public string SemanticId => Source.SemanticId;
    public FragmentScope Scope => Source.ParsedScope;
    public RuntimeSpec Spec => Source.Spec
        ?? throw new InvalidOperationException($"atom {Source.SemanticId} has no runtime spec");

    /// <summary>True for AbilityTrigger / ConditionalTrigger / AbilityRule.</summary>
    public bool IsTriggerScope => (int)Scope is >= 3 and <= 5;

    /// <summary>True for AbilityTrigger / ConditionalTrigger only (the original's `(uint)(scope - 3) &lt;= 1u`).</summary>
    public bool IsTriggerOrCondition => (int)Scope is 3 or 4;

    public bool HasTriggerLink => TriggerIndex >= 0;

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

    public bool HasValue(string id)
    {
        foreach (ResolvedValue value in Values)
        {
            if (string.Equals(value.Id, id, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Structural identity used by the "at most N of each field" and "no
    /// duplicate unique effect" rules. The original builds this from
    /// OperationRuntimeSpecCompiler.StructuralFieldKey (a compiled shape
    /// signature); we build the equivalent from the fields the data file
    /// actually carries.
    /// </summary>
    public string StructuralFieldKey
    {
        get
        {
            RuntimeSpec spec = Spec;
            var sb = new StringBuilder();
            sb.Append(ReplaceDigitRuns(Template)).Append('|')
              .Append(spec.Opcode).Append('|')
              .Append(spec.Variant).Append('|')
              .Append(spec.Target).Append('|')
              .Append(spec.SourceZone).Append('|')
              .Append(spec.DestinationZone).Append('|')
              .Append(spec.CardFilter);
            foreach (ResolvedValue value in Values)
            {
                sb.Append('|').Append(value.Id);
            }
            return sb.ToString();
        }
    }

    /// <summary>NumericTextSchema.Family: digit runs become '#'.</summary>
    internal static string ReplaceDigitRuns(string text)
    {
        int first = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                first = i;
                break;
            }
        }
        if (first < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 1);
        sb.Append(text, 0, first);
        int index = first;
        while (index < text.Length)
        {
            if (!char.IsDigit(text[index]))
            {
                sb.Append(text[index++]);
                continue;
            }
            sb.Append('#');
            do
            {
                index++;
            }
            while (index < text.Length && char.IsDigit(text[index]));
        }
        return sb.ToString();
    }
}

/// <summary>
/// A fully assembled card. Metadata (cost / type / target / tags) comes from the
/// shell recipe; the operation list is what the generator recombined.
///
/// Fingerprint is the identity that multiplayer sync and reproducibility checks
/// compare, so every field that can change behaviour is folded in.
/// </summary>
public sealed record GeneratedCard(
    string Character,
    GeneratedRarity Rarity,
    string ShellId,
    int Cost,
    int StarCost,
    bool HasStarCostX,
    GeneratedCardType Type,
    GeneratedTargetMode Target,
    IReadOnlyList<string> Tags,
    IReadOnlyList<GeneratedOperation> Operations)
{
    public string Fingerprint
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Character).Append('|').Append(Rarity).Append('|').Append(ShellId).Append('|')
              .Append(Cost).Append('|').Append(StarCost).Append('|').Append(HasStarCostX).Append('|')
              .Append(Type).Append('|').Append(Target).Append('|')
              .Append(string.Join(",", Tags)).Append('\n');
            foreach (GeneratedOperation operation in Operations)
            {
                sb.Append(operation.Template).Append('|')
                  .Append(operation.Scope).Append('|')
                  .Append(operation.TriggerIndex).Append('|')
                  .Append(operation.Spec.Opcode).Append('|')
                  .Append(operation.Spec.Variant);
                foreach (ResolvedValue value in operation.Values)
                {
                    sb.Append('|').Append(value.Id).Append('=').Append(value.Value);
                }
                sb.Append('\n');
            }
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash);
        }
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append($"{Character}/{ShellId} [{Rarity} {Type} target={Target} cost={Cost}]");
        if (StarCost >= 0 || HasStarCostX)
        {
            sb.Append($" starCost={StarCost} starX={HasStarCostX}");
        }
        sb.Append($" ops={Operations.Count}");
        for (int i = 0; i < Operations.Count; i++)
        {
            GeneratedOperation operation = Operations[i];
            sb.Append("\n    [").Append(i).Append("] ")
              .Append(operation.Template.PadRight(28))
              .Append(" scope=").Append(operation.Scope.ToString().PadRight(18))
              .Append(" op=").Append(operation.Spec.Opcode.PadRight(30))
              .Append(" variant=").Append(operation.Spec.Variant.PadRight(34))
              .Append(" triggerIndex=").Append(operation.TriggerIndex);
            if (operation.Values.Count > 0)
            {
                sb.Append(" values=").Append(string.Join(",", operation.Values.Select(v => $"{v.Id}:{v.Value}")));
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// The generator's randomness.
///
/// The original uses System.Random, whose sequence is explicitly documented as
/// not stable across .NET versions. Multiplayer card identity depends on this
/// sequence, so we own the algorithm instead: splitmix64, 64-bit state, seeded
/// from the first 8 bytes of SHA256(seed material). Determinism is then a
/// property of this file rather than of whatever runtime a player happens to
/// have, and "same (character, seed) -> same bytes" is testable in-process.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public string SeedMaterial { get; }

    public DeterministicRandom(string seedMaterial)
    {
        SeedMaterial = seedMaterial;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seedMaterial));
        _state = BitConverter.ToUInt64(hash, 0);
        if (_state == 0)
        {
            // splitmix64 degenerates at state 0 for the very first draw only;
            // nudging keeps the stream uniform and keeps the seed space total.
            _state = 0x9E3779B97F4A7C15UL;
        }
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, maxExclusive). maxExclusive must be positive.</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 1)
        {
            return 0;
        }
        // Rejection sampling: unbiased and independent of the modulus, so a
        // future change to the pool size cannot silently skew a distribution.
        ulong bound = (ulong)maxExclusive;
        ulong limit = ulong.MaxValue - (ulong.MaxValue % bound);
        ulong draw;
        do
        {
            draw = NextUInt64();
        }
        while (draw >= limit);
        return (int)(draw % bound);
    }

    /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
    public int Next(int minInclusive, int maxExclusive) =>
        maxExclusive <= minInclusive ? minInclusive : minInclusive + Next(maxExclusive - minInclusive);

    /// <summary>The original's `_random.Next(2) != 0` coin flip, spelled out.</summary>
    public bool CoinFlip() => Next(2) != 0;

    /// <summary>Uniform pick, or null when the sequence is empty.</summary>
    public T? PickUniform<T>(IReadOnlyList<T> items) =>
        items.Count == 0 ? default : items[Next(items.Count)];
}

/// <summary>
/// Seed derivation. The version string participates, so bumping
/// <see cref="Version"/> re-rolls every card - which is what makes the
/// reproducibility test (contract condition 7) falsifiable rather than
/// self-confirming.
/// </summary>
public static class GenerationSeed
{
    public const string Version = "v1";

    public static string Material(string modId, string character, long seed) =>
        $"{modId}/{Version}/{character}/{seed}";

    public static DeterministicRandom For(string modId, string character, long seed) =>
        new(Material(modId, character, seed));
}
