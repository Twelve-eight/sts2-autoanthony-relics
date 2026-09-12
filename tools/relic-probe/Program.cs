using System.Text;
using AutoAnthonyRelics.Data;
using AutoAnthonyRelics.Generation;

namespace RelicProbe;

/// <summary>
/// Isolated probe for the relic vertical slice. Runs WITHOUT Godot: it loads
/// the mod assembly's data + generation layers (pure) and the engine assembly
/// only for the RelicRarity enum. It never touches Godot statics (native
/// access violation outside the engine) and never claims in-game acceptance.
///
/// Covered here (component/integration level):
/// P1 ledger integrity, fragment splitting, generation determinism +
/// independence, text coverage, executor/ledger drift, value corrections.
/// NOT covered here (needs the real game, see DEVLOG acceptance section):
/// Harmony patch mounting, bag replacement, relic obtain/save/load, MP.
/// </summary>
internal static class Program
{
    private static int _failures;

    private static void Check(bool condition, string label, string detail = "")
    {
        if (condition)
        {
            Console.WriteLine($"PASS  {label}");
        }
        else
        {
            _failures++;
            Console.WriteLine($"FAIL  {label}  {detail}");
        }
    }

    public static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        // ---- 1. Ledger integrity: every supported entry resolves; Build throws otherwise.
        RelicAtomPool atoms = RelicAtomData.LoadAtoms();
        RelicLedger ledger = RelicAtomData.LoadLedger();
        RelicFragmentPool pool = RelicFragmentPool.Build(atoms, ledger);
        Check(atoms.Atoms.Count == 140, "atom pool loads 140 atoms", $"{atoms.Atoms.Count}");
        Check(ledger.Supported.Count == 25, "ledger has 25 supported entries", $"{ledger.Supported.Count}");
        Check(ledger.Rejected.Count == 26, "ledger has 26 rejected entries", $"{ledger.Rejected.Count}");
        Check(pool.Triggers.Count >= 10, "trigger fragments >= 10", $"{pool.Triggers.Count}");
        Check(pool.TriggeredEffects.Count >= 12, "triggered effect fragments >= 12", $"{pool.TriggeredEffects.Count}");
        Check(pool.PassiveEffects.Count >= 2, "passive fragments >= 2", $"{pool.PassiveEffects.Count}");

        // ---- 2. Text coverage: every fragment renders in both languages.
        bool textOk = true;
        foreach (TriggerFragment trigger in pool.Triggers)
        {
            try { _ = RelicText.TriggerEn(trigger); _ = RelicText.TriggerZhs(trigger); }
            catch (Exception e) { textOk = false; Console.WriteLine($"  text miss: trigger {trigger.Key}: {e.Message}"); }
        }
        foreach (EffectFragment effect in pool.TriggeredEffects.Concat(pool.PassiveEffects))
        {
            try { _ = RelicText.EffectEn(effect); _ = RelicText.EffectZhs(effect); }
            catch (Exception e) { textOk = false; Console.WriteLine($"  text miss: effect {effect.Key}: {e.Message}"); }
        }
        Check(textOk, "every fragment renders EN + ZHS");

        // ---- 3. Executor drift: every triggered effect opcode/variant/target must be
        // in the executor's supported set (mirrors AnthonyRelicModel's switch;
        // variant is wildcard there except for apply_power).
        var supported = new HashSet<string>
        {
            "apply_power|vigor|self|False", "apply_power|strength|self|False", "apply_power|thorns|self|False",
            "apply_power|vulnerable|all_enemies|False", "gain_block|-|self|False", "heal|-|self|False",
            "gain_energy|-|self|False", "draw_cards|-|self|False", "gain_max_hp|-|self|False",
            "gain_gold|-|self|False", "gain_max_potion|-|self|False", "deal_damage|-|all_enemies|False",
        };
        var passiveSupported = new HashSet<string> { "modify_hand_draw" };
        bool executorOk = true;
        foreach (EffectFragment effect in pool.TriggeredEffects)
        {
            string variant = effect.Opcode == "apply_power" ? effect.Variant ?? "-" : "-";
            string key = $"{effect.Opcode}|{variant}|{effect.Target}|{effect.IsPassive}";
            if (!supported.Contains(key))
            {
                executorOk = false;
                Console.WriteLine($"  executor drift: {key} from {effect.SourceAtom}");
            }
        }
        foreach (EffectFragment effect in pool.PassiveEffects)
        {
            if (!passiveSupported.Contains(effect.Opcode))
            {
                executorOk = false;
                Console.WriteLine($"  executor drift (passive): {effect.Opcode} from {effect.SourceAtom}");
            }
        }
        Check(executorOk, "every ledger fragment has an executor case");

        // ---- 4. Value corrections the ledger claims.
        Check(Find(pool.TriggeredEffects, e => e.Opcode == "apply_power" && e.Variant == "vulnerable")?.Amount == 1,
            "BagOfMarbles fix: 1 Vulnerable to ALL enemies");
        Check(Find(pool.PassiveEffects, e => e.Opcode == "modify_hand_draw" && e.Amount == -2) is not null,
            "BigMushroom fix: turn-1 draw -2 (sign restored)");
        Check(Find(pool.TriggeredEffects, e => e.Opcode == "apply_power" && e.Variant == "strength")?.Amount == 1,
            "Vajra fix: Strength 1 (value restored)");
        Check(pool.TriggeredEffects.Count(e => e.Opcode == "gain_max_potion" && e.Amount == 2) == 1,
            "PotionBelt fix: 2 potion slots");
        Check(pool.TriggeredEffects.Count(e => e.Opcode == "gain_max_potion" && e.Amount == 1) == 1,
            "PhialHolster kept as distinct fragment: 1 potion slot");
        var flagon = Find(pool.TriggeredEffects, e => e.Opcode == "deal_damage");
        Check(flagon is not null && flagon.Amount == 20 && flagon.Target == "all_enemies",
            "ScreamingFlagon fix: damage 20 to all enemies");
        Check(Find(pool.PassiveEffects, e => e.Opcode == "modify_hand_draw" && e.Amount == -2)?.Condition == "first_turn",
            "passive condition rides the effect (BigMushroom first_turn)");

        // ---- 5. Value sanity: no negative amounts outside the draw modifier.
        bool valueOk = true;
        foreach (EffectFragment effect in pool.TriggeredEffects.Concat(pool.PassiveEffects))
        {
            if (effect.Opcode != "modify_hand_draw" && effect.Amount <= 0)
            {
                valueOk = false;
                Console.WriteLine($"  bad amount: {effect.Key} amount={effect.Amount}");
            }
        }
        Check(valueOk, "no zero/negative amounts outside modify_hand_draw");

        // ---- 6. Generation: determinism, shape, spread, independence.
        IReadOnlyList<GeneratedRelicDefinition> run1 = RelicGenerator.Generate("seed-alpha", pool);
        IReadOnlyList<GeneratedRelicDefinition> run2 = RelicGenerator.Generate("seed-alpha", pool);
        IReadOnlyList<GeneratedRelicDefinition> run3 = RelicGenerator.Generate("seed-beta", pool);

        Check(run1.Count == RelicGenerator.SlotCount && run3.Count == RelicGenerator.SlotCount,
            "60 slots per run", $"{run1.Count}/{run3.Count}");
        Check(run1.Select(d => d.Fingerprint).SequenceEqual(run2.Select(d => d.Fingerprint)),
            "same seed -> byte-identical relic set");
        Check(!run1.Select(d => d.Fingerprint).SequenceEqual(run3.Select(d => d.Fingerprint)),
            "different seed -> different relic set");

        bool shapeOk = run1.All(d => d.Effects.Count > 0
            && (d.Trigger is null ? d.Effects.All(e => e.IsPassive) : d.Effects.All(e => !e.IsPassive)));
        Check(shapeOk, "every relic is either trigger+effects or passive-only");

        Check(run1.Select(d => d.NameEn).Distinct().Count() == RelicGenerator.SlotCount,
            "names unique within a run");

        var rarities = run1.Select(d => d.Rarity).Distinct().ToList();
        Check(rarities.Count == 3, "all three rarities present", string.Join(",", rarities));

        bool recursionOk = run1.All(d => d.Trigger is null
            || d.Trigger.Kind != "gold_gained"
            || d.Effects.All(e => e.Opcode != "gain_gold"));
        Check(recursionOk, "no gold_gained -> gain_gold recursion pairing");

        // Independence observable: trigger and effect sampled from DIFFERENT
        // source relics. Structural independence is guaranteed by construction
        // (no code path reads the source pairing); this counts the observable.
        int crossSource = run1.Count(d => d.Trigger is not null
            && d.Effects.All(e => SourceRelic(e.SourceAtom) != SourceRelic(d.Trigger.SourceAtom)));
        Check(crossSource >= 45, "cross-source recombination dominant",
            $"{crossSource}/{run1.Count(d => d.Trigger is not null)}");

        // ---- 7. Sample print for the log.
        Console.WriteLine();
        Console.WriteLine("---- sample relics (seed-alpha, first 10) ----");
        foreach (GeneratedRelicDefinition definition in run1.Take(10))
        {
            Console.WriteLine($"[{definition.Rarity}] {definition.NameEn} / {definition.NameZhs}");
            Console.WriteLine($"    EN: {definition.DescriptionEn}");
            Console.WriteLine($"    ZHS: {definition.DescriptionZhs}");
        }

        // ---- 8. Soak: 200 seeds x 60 slots, all invariants, fragment reachability.
        var usageTriggers = new HashSet<string>(StringComparer.Ordinal);
        var usageEffects = new HashSet<string>(StringComparer.Ordinal);
        bool soakShape = true;
        for (int i = 0; i < 200; i++)
        {
            var run = RelicGenerator.Generate($"soak-{i}", pool);
            if (run.Count != RelicGenerator.SlotCount) { soakShape = false; break; }
            foreach (var d in run)
            {
                if (d.Trigger is not null) usageTriggers.Add(d.Trigger.Key);
                foreach (var e in d.Effects) usageEffects.Add(e.Key);
                if (d.Effects.Count == 0) { soakShape = false; }
            }
        }
        Check(soakShape, "soak: 200 seeds, every slot well-formed");
        Check(usageTriggers.Count == pool.Triggers.Count, "soak: every trigger fragment reachable",
            $"{usageTriggers.Count}/{pool.Triggers.Count}");
        Check(usageEffects.Count == pool.TriggeredEffects.Count + pool.PassiveEffects.Count,
            "soak: every effect fragment reachable",
            $"{usageEffects.Count}/{pool.TriggeredEffects.Count + pool.PassiveEffects.Count}");

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "PROBE OK" : $"PROBE FAILED: {_failures} check(s)");
        return _failures == 0 ? 0 : 1;
    }

    private static string SourceRelic(string atomId)
    {
        int hash = atomId.IndexOf('#');
        return hash < 0 ? atomId : atomId[..hash];
    }

    private static EffectFragment? Find(IEnumerable<EffectFragment> pool, Func<EffectFragment, bool> predicate) =>
        pool.FirstOrDefault(predicate);
}
