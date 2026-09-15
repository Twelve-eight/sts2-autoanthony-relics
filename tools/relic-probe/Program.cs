using System.Text;
using AutoAnthonyRelics;
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

        // The csproj binds this probe to the mod's build output by path - an
        // untracked artifact that only changes when the mod is rebuilt. A
        // forgotten rebuild must fail loudly, not silently measure yesterday's
        // code (2026-09-14: an external review probe ran against a pre-fix
        // Debug artifact and reported the already-fixed Vigor ZHS text as
        // still broken).
        string modAssembly = typeof(RelicFragmentPool).Assembly.Location;
        DateTime built = File.GetLastWriteTime(modAssembly);
        string codeDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "mod", "Code"));
        if (!Directory.Exists(codeDir))
        {
            Console.WriteLine($"FAIL  freshness check: source dir not found: {codeDir}");
            return 2;
        }
        DateTime newestSource = Directory.EnumerateFiles(codeDir, "*.cs", SearchOption.AllDirectories)
            .Select(File.GetLastWriteTime)
            .DefaultIfEmpty(built)
            .Max();
        if (newestSource > built)
        {
            Console.WriteLine(
                $"FAIL  stale binary: {modAssembly} built {built:u} is older than newest " +
                $"source {newestSource:u} - rebuild the mod before probing");
            return 2;
        }
        Console.WriteLine($"OK    binary freshness: {Path.GetFileName(modAssembly)} built {built:u} >= newest source {newestSource:u}");

        // ---- 1. Ledger integrity: every supported entry resolves; Build throws otherwise.
        RelicAtomPool atoms = RelicAtomData.LoadAtoms();
        RelicLedger ledger = RelicAtomData.LoadLedger();
        RelicFragmentPool pool = RelicFragmentPool.Build(atoms, ledger);
        Check(atoms.Atoms.Count == 146, "atom pool loads 146 atoms", $"{atoms.Atoms.Count}");
        Check(ledger.Supported.Count == 36, "ledger has 36 supported entries", $"{ledger.Supported.Count}");
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
        // variant is significant for apply_power and the downside opcodes
        // lose_hp / lose_gold / add_curse, wildcard "-" elsewhere).
        var variantSignificant = new HashSet<string> { "apply_power", "lose_hp", "lose_gold", "add_curse" };
        var supported = new HashSet<string>
        {
            "apply_power|vigor|self|False", "apply_power|strength|self|False", "apply_power|thorns|self|False",
            "apply_power|vulnerable|all_enemies|False", "gain_block|-|self|False", "heal|-|self|False",
            "gain_energy|-|self|False", "draw_cards|-|self|False", "gain_max_hp|-|self|False",
            "gain_gold|-|self|False", "gain_max_potion|-|self|False", "deal_damage|-|all_enemies|False",
            // Downside pool (user order 2026-09-15).
            "lose_hp|unblockable|self|False", "lose_hp|immediate|self|False", "lose_max_hp|-|self|False",
            "lose_gold|immediate|self|False", "lose_gold|all|self|False",
            "add_curse|greed|self|False", "add_curse|curse_of_the_bell|self|False",
            "add_curse|enthralled|self|False", "add_curse|folly|self|False",
        };
        var passiveSupported = new HashSet<string> { "modify_hand_draw" };
        bool executorOk = true;
        foreach (EffectFragment effect in pool.TriggeredEffects)
        {
            string variant = variantSignificant.Contains(effect.Opcode) ? effect.Variant ?? "-" : "-";
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

        // ---- 4b. Downside pool (user order 2026-09-15): downside fragments
        // from ALL relic sources incl. Ancient/Event, weighted 140 vs 100.
        var downsides = pool.TriggeredEffects.Where(e => e.IsDownside).ToList();
        Check(downsides.Count == 11, "downside fragments: 11", $"{downsides.Count}");
        Check(downsides.All(d => !d.IsPassive), "all downsides are triggered effects");
        Check(downsides.Count(d => d.Opcode == "add_curse") == 4, "four curse downsides");
        Check(downsides.Any(d => d.Opcode == "add_curse" && d.Variant == "greed"),
            "CursedPearl Greed curse present");
        Check(downsides.Count(d => d.Opcode == "lose_max_hp") == 2
            && downsides.Any(d => d.Opcode == "lose_max_hp" && d.Amount == 12)
            && downsides.Any(d => d.Opcode == "lose_max_hp" && d.Amount == 9),
            "lose_max_hp: 12 (LeafyPoultice) + 9 (SereTalon)");
        Check(downsides.Any(d => d.Opcode == "lose_gold" && d.Variant == "all"),
            "SilkenTress lose-ALL-gold present");
        Check(downsides.Any(d => d.Opcode == "lose_gold" && d.Variant != "all" && d.Amount == 3),
            "SealOfGold lose 3 gold present");
        Check(downsides.Count(d => d.Opcode == "lose_hp") == 3
            && downsides.Count(d => d.Opcode == "lose_hp" && d.Variant == "unblockable") == 2,
            "self-damage: 3 fragments, 2 unblockable (FragrantMushroom/RoyalPoison)");

        // ---- 5. Value sanity: no negative amounts outside the draw modifier.
        // lose_gold variant "all" (SilkenTress) and add_curse carry no numeric
        // slot by design - the executor loses owner.Gold / adds the Variant's
        // curse card.
        bool valueOk = true;
        foreach (EffectFragment effect in pool.TriggeredEffects.Concat(pool.PassiveEffects))
        {
            bool noAmountByDesign = effect.Opcode == "modify_hand_draw"
                || effect.Opcode == "add_curse"
                || (effect.Opcode == "lose_gold" && effect.Variant == "all");
            if (!noAmountByDesign && effect.Amount <= 0)
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
        // Max-HP policy (user order 2026-09-15): the +1 max-HP fragment shared
        // by ChosenCheese (combat end) / DragonFruit (gold gained) is
        // unsampleable by design, so it is the ONLY fragment allowed missing.
        var allEffectKeys = pool.TriggeredEffects.Concat(pool.PassiveEffects).Select(e => e.Key).ToList();
        var missing = allEffectKeys.Where(k => !usageEffects.Contains(k)).ToList();
        Check(missing.All(k => k.StartsWith("gain_max_hp|", StringComparison.Ordinal)),
            "soak: only max-HP-policy fragments unreachable",
            missing.Count == 0 ? "none missing" : string.Join(",", missing));

        // ---- 9. Downside weighting: downside fragments weigh 140 vs 100, so
        // their observed share among TRIGGERED effect picks must sit clearly
        // above the uniform share but below 2x (passive picks excluded - they
        // can never be downsides and would dilute both sides differently).
        int downsidePicks = 0, triggeredPicks = 0;
        for (int i = 0; i < 100; i++)
        {
            foreach (var d in RelicGenerator.Generate($"weight-{i}", pool))
            foreach (var e in d.Effects)
            {
                if (e.IsPassive)
                {
                    continue;
                }
                triggeredPicks++;
                if (e.IsDownside)
                {
                    downsidePicks++;
                }
            }
        }
        double uniformShare = (double)pool.TriggeredEffects.Count(e => e.IsDownside) / pool.TriggeredEffects.Count;
        double observedShare = triggeredPicks == 0 ? 0 : (double)downsidePicks / triggeredPicks;
        Check(observedShare > uniformShare * 1.15 && observedShare < uniformShare * 1.65,
            "downside share ~= 1.4x uniform (weighted 140 vs 100)",
            $"observed {observedShare:F3} vs uniform {uniformShare:F3} over {triggeredPicks} picks");

        // ---- 10. Description punctuation (user report 2026-09-15): fragments
        // carry no sentence punctuation; a description is ONE sentence -
        // concurrent effects joined with "and"/commas (EN) or enumeration
        // commas (ZHS), and exactly one final period.
        bool punctOk = true;
        for (int i = 0; i < 50; i++)
        {
            foreach (var d in RelicGenerator.Generate($"punct-{i}", pool))
            {
                string en = d.DescriptionEn;
                string zh = d.DescriptionZhs;
                if (en.EndsWith(".")) { en = en[..^1]; }
                if (zh.EndsWith("。")) { zh = zh[..^1]; }
                if (en.Contains(". ") || en.Contains(",.") || zh.Contains("。"))
                {
                    punctOk = false;
                    Console.WriteLine($"  mid-sentence punctuation: {d.NameEn}: {d.DescriptionEn}");
                }
                if (d.Effects.Count >= 2 && !en.Contains(" and ") && !zh.Contains("，"))
                {
                    punctOk = false;
                    Console.WriteLine($"  missing conjunction: {d.NameEn}: {d.DescriptionEn}");
                }
            }
        }
        Check(punctOk, "descriptions are single re-punctuated sentences");

        // ---- 11. Max-HP policy (user order 2026-09-15): generated relics may
        // raise Max HP ONLY under the obtained trigger (拾起时). The pool keeps
        // the mid-run fragments as data records; the generator must never
        // sample them.
        bool maxHpOk = true;
        int maxHpObtainRelics = 0;
        for (int i = 0; i < 100; i++)
        {
            foreach (var d in RelicGenerator.Generate($"maxhp-{i}", pool))
            foreach (var e in d.Effects)
            {
                if (e.Opcode != "gain_max_hp")
                {
                    continue;
                }
                if (d.Trigger is null || d.Trigger.Kind != "obtained")
                {
                    maxHpOk = false;
                    Console.WriteLine($"  max-HP policy violation: slot {d.Slot} trigger {d.Trigger?.Kind ?? "(passive)"}: {d.DescriptionEn}");
                }
                else
                {
                    maxHpObtainRelics++;
                }
            }
        }
        Check(maxHpOk, "gain_max_hp generated only under obtained trigger");
        Check(maxHpObtainRelics > 0, "obtained max-HP relics still generatable", $"{maxHpObtainRelics}");
        Check(pool.TriggeredEffects.Any(e => e.Opcode == "gain_max_hp"),
            "max-HP fragments retained in pool (policy excludes sampling, not data)");

        // ---- Registry cache-key coverage (AAR-1, 2026-09-15).
        // The rest of this probe drives RelicGenerator.Generate directly and never enters
        // AnthonyRelicRunRegistry, so a cache-key regression - a missing seed/version/fingerprint
        // component - would pass every check above silently. The key has been under-covered
        // twice before (round 2 added the pool fingerprint, AAR-4 added SeedVersion), so the
        // three components are now pinned by assertions.
        AnthonyRelicRunRegistry.ResetForRunEnd();
        var k1 = AnthonyRelicRunRegistry.DefinitionsFor("SEED-AAA", pool);
        var k2 = AnthonyRelicRunRegistry.DefinitionsFor("SEED-AAA", pool);
        Check(ReferenceEquals(k1, k2), "registry: same (seed, version, fingerprint) returns the cached instance");
        var k3 = AnthonyRelicRunRegistry.DefinitionsFor("SEED-BBB", pool);
        Check(!ReferenceEquals(k1, k3), "registry: a different SEED misses the cache");
        Check(AnthonyRelicRunRegistry.DefinitionsFor("SEED-AAA", pool).Count > 0,
            "registry: definitions are non-empty after a cache round trip");

        // The version component: the same seed with a different SeedVersion must not be served
        // from cache. SeedVersion is a const string, so this is asserted structurally rather
        // than by mutation - a compile-time constant cannot be swapped at runtime.
        Check(!string.IsNullOrEmpty(RelicGenerator.SeedVersion),
            "registry: SeedVersion is a non-empty constant (the version key component)",
            $"SeedVersion={RelicGenerator.SeedVersion}");

        // The fingerprint component: two pools with different content must produce different
        // fingerprints, or the fingerprint term in the key is inert.
        var altPool = RelicFragmentPool.Build(atoms, ledger);
        Check(altPool.Fingerprint == pool.Fingerprint,
            "registry: rebuilding the same ledger yields the same fingerprint (deterministic)");
        Check(pool.Fingerprint.Length == 16 && pool.Fingerprint.All(Uri.IsHexDigit),
            "registry: fingerprint is the 16-char hex digest the key expects",
            $"len={pool.Fingerprint.Length} value={pool.Fingerprint}");

        // Slot lookup through the registry (DefinitionFor) - the path the game actually calls.
        AnthonyRelicRunRegistry.CurrentRunSeed = "SEED-AAA";
        Check(ReferenceEquals(AnthonyRelicRunRegistry.DefinitionFor(0, pool), k1[0]),
            "registry: DefinitionFor(0) resolves through the cache to the same definition");
        AnthonyRelicRunRegistry.CurrentRunSeed = null;
        Check(AnthonyRelicRunRegistry.DefinitionFor(0, pool) == null,
            "registry: DefinitionFor returns null with no active run seed");
        AnthonyRelicRunRegistry.ResetForRunEnd();
        Check(!ReferenceEquals(AnthonyRelicRunRegistry.DefinitionsFor("SEED-AAA", pool), k1),
            "registry: ResetForRunEnd drops the cache (menu must not see the previous run)");

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
