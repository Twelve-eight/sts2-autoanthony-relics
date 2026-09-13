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
        0);

    public IReadOnlyList<TriggerFragment> Triggers { get; }
    public IReadOnlyList<EffectFragment> TriggeredEffects { get; }
    public IReadOnlyList<EffectFragment> PassiveEffects { get; }
    public int SupportedAtomCount { get; }

    /// <summary>Stable fingerprint of the fragment pool contents (cache-key input).</summary>
    public string Fingerprint { get; }

    private RelicFragmentPool(IReadOnlyList<TriggerFragment> triggers,
        IReadOnlyList<EffectFragment> triggeredEffects,
        IReadOnlyList<EffectFragment> passiveEffects,
        int supportedAtomCount)
    {
        Triggers = triggers;
        TriggeredEffects = triggeredEffects;
        PassiveEffects = passiveEffects;
        SupportedAtomCount = supportedAtomCount;
        var sb = new System.Text.StringBuilder();
        foreach (var t in triggers) sb.Append('T').Append(t.Key).Append(';');
        foreach (var e in triggeredEffects) sb.Append('E').Append(e.Key).Append(';');
        foreach (var e in passiveEffects) sb.Append('P').Append(e.Key).Append(';');
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
            var bucket = isPassive ? passives : triggered;
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
            supported);
    }
}
