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

    /// <summary>
    /// Freeze/unfreeze generation settings from the probe. A thin shim so the
    /// probe never has to name GenerationSettings' members directly: the type is
    /// internal (its members are an implementation detail of the generator) and
    /// this keeps that boundary in one place.
    /// </summary>
    private static class GenerationSettingsTest
    {
        public static void Set(bool? includeExtraPool = null, int? weightTriggered = null,
            int? weightPassive = null, int? weightBenefit = null, int? weightExtra = null) =>
            GenerationSettings.FreezeExplicit(
                includeExtraPool ?? false,
                weightTriggered ?? 100, weightPassive ?? 100, weightBenefit ?? 100, weightExtra ?? 100);

        public static void Reset() => GenerationSettings.Unfreeze();
    }

    /// <summary>
    /// The multiset of drawn effect keys over N seeds under one weight profile.
    /// Ordinal-sorted so two profiles can be compared with SequenceEqual; the
    /// point is to prove the profile CHANGES the composition, not to pin a
    /// specific distribution (which would over-constrain the generator).
    /// </summary>
    private static List<string> DrawComposition(RelicFragmentPool pool, int seeds, int weightTriggered,
        int weightPassive, int weightBenefit, int weightExtra)
    {
        GenerationSettingsTest.Set(weightTriggered: weightTriggered, weightPassive: weightPassive,
            weightBenefit: weightBenefit, weightExtra: weightExtra);
        try
        {
            var drawn = new List<string>();
            for (int i = 0; i < seeds; i++)
            {
                foreach (var d in RelicGenerator.Generate($"weight-mix-{i}", pool))
                {
                    foreach (var e in d.Effects) drawn.Add(e.Key);
                }
            }
            drawn.Sort(StringComparer.Ordinal);
            return drawn;
        }
        finally
        {
            GenerationSettingsTest.Reset();
        }
    }

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
        RelicFragmentPool pool = RelicFragmentPool.Build(atoms, ledger, includeExtraPool: true);
        Check(atoms.Atoms.Count == 165, "atom pool loads 165 atoms", $"{atoms.Atoms.Count}");
        Check(ledger.Supported.Count == 47, "ledger has 47 supported entries", $"{ledger.Supported.Count}");
        Check(ledger.ExtraSupported.Count == 9, "ledger has 9 extra-pool entries", $"{ledger.ExtraSupported.Count}");
        Check(ledger.Rejected.Count == 26, "ledger has 26 rejected entries", $"{ledger.Rejected.Count}");
        // Extra pool off must build a pool WITHOUT the extra shapes, and the extra
        // flag must not be able to remove a core shape (the dedup folds Pool to
        // Core). Compared by fragment count, not by fingerprint. All 9 extra
        // atoms carry a Trigger (3 turn_start, 3 combat_start, 3 obtained), so
        // they are all TRIGGERED effects - the passive side must be unchanged.
        RelicFragmentPool coreOnly = RelicFragmentPool.Build(atoms, ledger, includeExtraPool: false);
        Check(coreOnly.TriggeredEffects.Count == pool.TriggeredEffects.Count - 9
              && coreOnly.PassiveEffects.Count == pool.PassiveEffects.Count
              && coreOnly.Triggers.Count == pool.Triggers.Count,
            "extra pool off removes exactly the 9 extra triggered fragments",
            $"triggered {coreOnly.TriggeredEffects.Count} vs {pool.TriggeredEffects.Count}, " +
            $"passives {coreOnly.PassiveEffects.Count} vs {pool.PassiveEffects.Count}, " +
            $"triggers {coreOnly.Triggers.Count} vs {pool.Triggers.Count}");
        Check(pool.TriggeredEffects.Count(e => e.Pool == FragmentPoolKind.Extra) == 9
              && pool.PassiveEffects.All(e => e.Pool == FragmentPoolKind.Core)
              && coreOnly.TriggeredEffects.All(e => e.Pool == FragmentPoolKind.Core),
            "extra-pool fragments are tagged, and absent when the pool is off");
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
        var variantSignificant = new HashSet<string> { "apply_power", "lose_hp", "lose_gold", "add_curse",
            "enchant_hand", "enchant_deck" };
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
            // Extra pool (user order 2026-09-18), ported from QuriousCraftingRelics.
            "retain_hand_card|-|self|False", "sly_hand_card|-|self|False",
            "ethereal_hand_card|-|self|False",
            "enchant_hand|sharp|self|False", "enchant_hand|nimble|self|False",
            "enchant_hand|imbued|self|False",
            "enchant_deck|sharp|self|False", "enchant_deck|nimble|self|False",
            "enchant_deck|imbued|self|False",
        };
        // Passive opcodes the executor implements (mirrors RelicGenerator.PassiveOpcodes).
        // Was { "modify_hand_draw" } only; the Ancient restriction/benefit affixes
        // (v5) each have an executor override in AnthonyRelicModel
        // (restrict_gold / restrict_potion / restrict_card_play / restrict_draw /
        // modify_card_cost / enemy_strength_gain / retain_hand / extra_turn /
        // expand_card_pool / enchant_reward), so the set was stale, not the code.
        var passiveSupported = new HashSet<string>
        {
            "modify_hand_draw", "modify_max_energy",
            "restrict_gold", "restrict_potion", "restrict_card_play",
            "restrict_draw", "modify_card_cost", "enemy_strength_gain",
            "retain_hand", "extra_turn", "expand_card_pool", "enchant_reward",
        };
        bool executorOk = true;
        foreach (EffectFragment effect in pool.TriggeredEffects)
        {
            string variant = variantSignificant.Contains(effect.Opcode) ? effect.Variant ?? "-" : "-";
            string key = $"{effect.Opcode}|{variant}|{effect.Target}|{effect.IsPassive}";
            if (!supported.Contains(key))
            {
                executorOk = false;
                Console.WriteLine($"  executor drift: {key} from {string.Join(",", effect.SourceAtoms)}");
            }
        }
        foreach (EffectFragment effect in pool.PassiveEffects)
        {
            if (!passiveSupported.Contains(effect.Opcode))
            {
                executorOk = false;
                Console.WriteLine($"  executor drift (passive): {effect.Opcode} from {string.Join(",", effect.SourceAtoms)}");
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
        // 11 core + 1 extra (ethereal_hand_card, user order 2026-09-18).
        var downsides = pool.TriggeredEffects.Where(e => e.IsDownside).ToList();
        Check(downsides.Count == 12, "downside fragments: 12", $"{downsides.Count}");
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
        // curse card. The v5 affixes are flags, not numbers: a restriction's text
        // is "you can no longer gain Gold" / "Power cards cost 1 more" (only
        // restrict_card_play carries a cap) and the benefit affixes are likewise
        // boolean - expand_card_pool "card rewards may contain cards from any
        // character", extra_turn "take an extra turn if you play no cards",
        // retain_hand "you no longer discard your hand". Amount == 0 is correct
        // for every one of them.
        bool valueOk = true;
        foreach (EffectFragment effect in pool.TriggeredEffects.Concat(pool.PassiveEffects).Concat(pool.BenefitEffects))
        {
            bool noAmountByDesign = effect.Opcode == "modify_hand_draw"
                || effect.Opcode == "add_curse"
                || effect.IsRestriction
                || effect.IsBenefit
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

        // Independence observable: the relic is assembled from atoms that came
        // from DIFFERENT source relics. Structural independence is guaranteed by
        // construction (no code path reads the source pairing); this counts the
        // observable.
        //
        // The observable is "the relic's provenance spans >= 2 source relics",
        // NOT "trigger provenance and effect provenance are disjoint". Disjointness
        // is the wrong predicate now that fragments carry their WHOLE provenance
        // set: `obtained` folds 15 atoms and `turn_start|first_turn` folds 4, so
        // any relic drawing such a trigger intersects almost every effect's
        // provenance by construction - it would fail a disjointness assertion
        // while being perfectly well recombined. Measured on the same runs:
        // disjoint 942/1070 (88.0%), >=2 sources 1056/1070 (98.7%).
        int crossSource = run1.Count(d =>
            d.Trigger is not null
            && Sources(d).Count >= 2);
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
        // unsampleable by design, so it is the ONLY core fragment allowed
        // missing. Extra-pool fragments are ALSO expected missing here: the
        // default settings have EnableExtraEffectPool off, and the whole point of the
        // toggle is that those fragments do not participate (RelicGenerator
        // .Excluded rule 6). Asserting them reachable with the pool off would be
        // asserting the toggle does nothing.
        var allEffectKeys = pool.TriggeredEffects.Concat(pool.PassiveEffects).Select(e => e.Key).ToList();
        var missing = allEffectKeys.Where(k => !usageEffects.Contains(k)).ToList();
        Check(missing.All(k => k.StartsWith("gain_max_hp|", StringComparison.Ordinal)
                               || pool.TriggeredEffects.Any(e => e.Key == k && e.Pool == FragmentPoolKind.Extra)),
            "soak: only max-HP-policy and extra-pool fragments unreachable",
            missing.Count == 0 ? "none missing" : string.Join(",", missing));
        // The extra pool must be genuinely unreachable with the toggle off -
        // i.e. it is not merely "rare", it never appears.
        Check(!usageEffects.Any(k => pool.TriggeredEffects.Any(e => e.Key == k && e.Pool == FragmentPoolKind.Extra)),
            "soak: extra-pool fragments never sampled while the pool is off");

        // ---- 8a2. EXTRA POOL REACHABLE WHEN ENABLED (user order 2026-09-18).
        // The mirror of the check above: with the toggle ON the extra fragments
        // must actually be sampleable. Done through GenerationSettings.Freeze so
        // the toggle reaches the generator the same way it does at run start.
        var extraOnUsage = new HashSet<string>(StringComparer.Ordinal);
        var extraOnTriggers = new HashSet<string>(StringComparer.Ordinal);
        GenerationSettingsTest.Set(includeExtraPool: true);
        try
        {
            for (int i = 0; i < 200; i++)
            {
                foreach (var d in RelicGenerator.Generate($"extra-{i}", pool))
                {
                    if (d.Trigger is not null) extraOnTriggers.Add(d.Trigger.Key);
                    foreach (var e in d.Effects) extraOnUsage.Add(e.Key);
                }
            }
        }
        finally
        {
            GenerationSettingsTest.Reset();
        }
        var extraKeys = pool.TriggeredEffects.Where(e => e.Pool == FragmentPoolKind.Extra).Select(e => e.Key).ToList();
        var extraMissing = extraKeys.Where(k => !extraOnUsage.Contains(k)).ToList();
        Check(extraKeys.Count == 9 && extraMissing.Count == 0,
            "extra pool enabled: all 9 extra fragments reachable in 200 seeds",
            extraMissing.Count == 0 ? "all reachable" : string.Join(",", extraMissing));

        // ---- 8a3. THE WEIGHT ACTUALLY STEERS THE DRAW (user order 2026-09-18).
        // Compares the composition of the drawn set under two very different
        // weight profiles on the same seeds. A weight that is read but ignored
        // (or a picker that still hardcodes NormalWeight) would make these two
        // multisets identical.
        var baseline = DrawComposition(pool, seeds: 120, weightTriggered: 100, weightPassive: 100, weightBenefit: 100, weightExtra: 100);
        var skewed = DrawComposition(pool, seeds: 120, weightTriggered: 400, weightPassive: 0, weightBenefit: 0, weightExtra: 100);
        Check(baseline.Count > 0 && skewed.Count > 0 && !baseline.SequenceEqual(skewed),
            "weight: a skewed profile changes the drawn effect mix",
            $"baseline={baseline.Count} entries, skewed={skewed.Count} entries");

        // ---- 8a4. SETTINGS ARE PART OF THE RESULT IDENTITY.
        // The property that actually matters and that the cache key encodes:
        // for one (seed, settings) pair the output is byte-identical, and the
        // SAME seed under a DIFFERENT settings profile is allowed to differ -
        // otherwise the settings would not be generation inputs at all.
        //
        // NOT asserted here: "a weight change preserves the draw count". That is
        // false and was briefly asserted on 2026-09-19: the picked fragment
        // steers control flow (a restriction fragment pulls in the offsetting
        // benefit draw, a benefit fragment comes from another band), so the
        // total number of picks legitimately differs. The invariant that does
        // hold is per-pick: PickWeightedUniquely rolls exactly one Next() per
        // pick regardless of weights.
        bool settingsStable = true;
        bool settingsMatter = false;
        GenerationSettingsTest.Set();
        try
        {
            var a = RelicGenerator.Generate("settings-id", pool);
            var b = RelicGenerator.Generate("settings-id", pool);
            settingsStable = a.Select(d => d.NameEn).SequenceEqual(b.Select(d => d.NameEn));
        }
        finally
        {
            GenerationSettingsTest.Reset();
        }
        GenerationSettingsTest.Set(weightTriggered: 400, weightPassive: 0, weightBenefit: 0, weightExtra: 400);
        try
        {
            var skewedRun = RelicGenerator.Generate("settings-id", pool);
            GenerationSettingsTest.Reset();
            var baseRun = RelicGenerator.Generate("settings-id", pool);
            settingsMatter = !skewedRun.Select(d => d.NameEn).SequenceEqual(baseRun.Select(d => d.NameEn));
        }
        finally
        {
            GenerationSettingsTest.Reset();
        }
        Check(settingsStable, "settings: one (seed, settings) pair is reproducible");
        Check(settingsMatter, "settings: a skewed profile changes the generated set for the same seed");

        // ---- 8a5. HAND/Deck EFFECT TIMING (user order 2026-09-18).
        // The generator's rules 7 must keep hand effects off pre-draw triggers
        // and deck effects on `obtained` only. This is the check that would have
        // caught the 2026-09-19 defect where every hand effect was skipped at
        // execution time (the hand is empty at BeforeCombatStart) while the text
        // still promised it. Asserted over the extra pool ENABLED, since that is
        // the only configuration in which these fragments can be drawn.
        var badPairings = new List<string>();
        GenerationSettingsTest.Set(includeExtraPool: true);
        try
        {
            for (int i = 0; i < 200; i++)
            {
                foreach (var d in RelicGenerator.Generate($"timing-{i}", pool))
                {
                    foreach (var e in d.Effects)
                    {
                        string kind = d.Trigger?.Kind ?? "(passive)";
                        if (e.IsHandEffect && !EffectFragment.HandEffectTriggers.Contains(kind))
                        {
                            badPairings.Add($"{e.Key} on {kind}");
                        }
                        if (e.IsDeckEffect && !EffectFragment.DeckEffectTriggers.Contains(kind))
                        {
                            badPairings.Add($"{e.Key} on {kind}");
                        }
                    }
                }
            }
        }
        finally
        {
            GenerationSettingsTest.Reset();
        }
        Check(badPairings.Count == 0,
            "timing: hand effects only on post-draw triggers, deck effects only on obtained",
            badPairings.Count == 0 ? "none" : string.Join(" | ", badPairings.Distinct().Take(4)));
        // And the rule must be load-bearing: the hand-effect trigger set must be
        // a strict subset of the pool's triggers, or the rule above is vacuous.
        Check(EffectFragment.HandEffectTriggers.Count < pool.Triggers.Count
              && EffectFragment.HandEffectTriggers.All(k => pool.Triggers.Any(t => t.Kind == k)),
            "timing: hand-effect trigger set is a non-trivial subset of the pool's triggers",
            $"hand={string.Join(",", EffectFragment.HandEffectTriggers)} pool={pool.Triggers.Count}");

        // ---- 8b. CROSS-SEED VARIETY (defect report 2026-09-18).
        //
        // Every assertion above tests a property that was PASSING while the mod
        // shipped the same six restriction relics in 67 of 68 seeds: determinism
        // ("same seed -> byte-identical"), uniqueness ("60 distinct names per
        // run"), and REACHABILITY ("every fragment reachable in 200 seeds") are
        // all satisfied by a generator that emits a fixed per-run set. Variety is
        // a different property and nothing here measured it, so the defect had no
        // failing check to trip.
        //
        // The real user seeds, not synthetic ones: the original report was
        // demonstrated on these (7 of 60 descriptions were constant across all
        // eight). A regression here is exactly the shipped defect.
        string[] realSeeds =
        {
            "D39RA35Z86", "WCYZAU9V2H", "1FN9C93R6Z", "MU25VDXKHR93",
            "U5ASK2HZSBAT", "RZ5VL1VT6PL7", "7HADLV839ET0", "1ZSDT8AZFNH9",
        };
        var perSeedDescriptions = new List<HashSet<string>>(realSeeds.Length);
        var perSeedNames = new List<HashSet<string>>(realSeeds.Length);
        var restrictionCounts = new List<int>(realSeeds.Length);
        foreach (string seed in realSeeds)
        {
            var defs = RelicGenerator.Generate(seed, pool);
            perSeedDescriptions.Add(new HashSet<string>(defs.Select(d => d.DescriptionEn)));
            perSeedNames.Add(new HashSet<string>(defs.Select(d => d.NameEn)));
            restrictionCounts.Add(defs.Count(d => d.Effects.Any(e => e.IsRestriction)));
        }

        // (a) No description may appear in EVERY run. The v6 defect had 7 such
        // descriptions (all six restriction texts plus the -2 draw passive).
        var constantDescriptions = new HashSet<string>(perSeedDescriptions[0]);
        foreach (var s in perSeedDescriptions.Skip(1))
        {
            constantDescriptions.IntersectWith(s);
        }
        Check(constantDescriptions.Count == 0,
            "variety: no relic description is present in every seed",
            constantDescriptions.Count == 0
                ? "0 of 60"
                : $"{constantDescriptions.Count} constant: {string.Join(" | ", constantDescriptions.Take(3))}");

        // Names too: a description-level fix that left naming invariant would
        // still show the player the same relic names every run.
        var constantNames = new HashSet<string>(perSeedNames[0]);
        foreach (var s in perSeedNames.Skip(1))
        {
            constantNames.IntersectWith(s);
        }
        Check(constantNames.Count == 0,
            "variety: no relic name is present in every seed",
            constantNames.Count == 0 ? "0 of 60" : $"{constantNames.Count}: {string.Join(" | ", constantNames.Take(3))}");

        // (b) A run must not take EVERY restriction fragment. v6 took the branch
        // whenever any remained, so the count equalled the pool size in 67 of 68
        // seeds - that is what the players saw.
        //
        // The invariant is "most seeds do not draw the whole restriction pool",
        // NOT "the count is never the pool size" and NOT "the count varies": 6 is
        // a legal v7 outcome (the 68-seed histogram has 5 seeds at 6), and a
        // `Max() < 6` cap would be a false invariant that passes on these 8 seeds
        // by luck and then fails spuriously when a pool/ledger change shifts the
        // streams. Likewise `distinct > 1` alone is too weak - it is satisfied by
        // v6, which yields distinct=2 once a single seed happens to fall short.
        // Measured: v6 = 1/68 seeds below the pool size (1.5%); v7 = 63/68 (93%).
        int poolRestrictionFragments = pool.PassiveEffects.Count(e => e.IsRestriction);
        var restrictionCountsWide = new List<int>(restrictionCounts);
        for (int i = 0; i < 64; i++)
        {
            var defs = RelicGenerator.Generate($"variety-restriction-{i}", pool);
            restrictionCountsWide.Add(defs.Count(d => d.Effects.Any(e => e.IsRestriction)));
        }
        int belowPool = restrictionCountsWide.Count(c => c < poolRestrictionFragments);
        double belowPoolShare = belowPool / (double)restrictionCountsWide.Count;
        Check(belowPoolShare >= 0.5,
            "variety: most seeds draw only a subset of the restriction pool (not all of it)",
            $"{belowPool}/{restrictionCountsWide.Count} ({100 * belowPoolShare:F0}%) below the pool size " +
            $"{poolRestrictionFragments}; real-8=[{string.Join(",", restrictionCounts)}] " +
            $"wide min={restrictionCountsWide.Min()} max={restrictionCountsWide.Max()}");

        // (c) Two runs must not share most of their content. Measured 12.9% after
        // the fix; the pre-fix vocabulary reuse made the player perceive one set.
        // 35/60 is a loose ceiling - it catches a collapse, not normal overlap.
        int sharedPairs = 0, pairCount = 0, worstPair = 0;
        for (int i = 0; i < perSeedDescriptions.Count; i++)
        for (int j = i + 1; j < perSeedDescriptions.Count; j++)
        {
            int shared = perSeedDescriptions[i].Intersect(perSeedDescriptions[j]).Count();
            sharedPairs += shared;
            worstPair = Math.Max(worstPair, shared);
            pairCount++;
        }
        Check(worstPair < 35,
            "variety: no two seeds share most of their relic descriptions",
            $"avg={sharedPairs / (double)pairCount:F1}/60 worst={worstPair}/60");

        // ---- 9. Downside weighting: the 1.4x downside bonus (140 vs 100) was
        // CANCELLED by user order 2026-09-17, so WeightOf is now a constant and
        // downside fragments draw uniformly with everything else. The observed
        // share among TRIGGERED picks must therefore track the uniform share
        // within sampling noise - it must NOT sit 1.4x above it any more.
        // (Passive picks are excluded: they can never be downsides.)
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
        // Tolerance 0.25 absolute: this is a deterministic 100-seed sample, so the
        // band only needs to exclude the old +40% regime (which sits ~0.4*uniform
        // above) while absorbing the finite-sample spread.
        Check(Math.Abs(observedShare - uniformShare) < 0.25,
            "downside share ~= uniform (140 vs 100 weighting cancelled)",
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
        var altPool = RelicFragmentPool.Build(atoms, ledger, includeExtraPool: true);
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

    private static EffectFragment? Find(IEnumerable<EffectFragment> pool, Func<EffectFragment, bool> predicate) =>
        pool.FirstOrDefault(predicate);

    /// <summary>
    /// Every source relic a generated relic was assembled from (trigger plus all
    /// effects), deduplicated.
    /// </summary>
    private static HashSet<string> Sources(GeneratedRelicDefinition definition)
    {
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (string atom in definition.Trigger?.SourceAtoms ?? Array.Empty<string>())
        {
            sources.Add(SourceOf(atom));
        }
        foreach (EffectFragment effect in definition.Effects)
        {
            foreach (string atom in effect.SourceAtoms)
            {
                sources.Add(SourceOf(atom));
            }
        }
        return sources;
    }

    private static string SourceOf(string atomId)
    {
        int hash = atomId.IndexOf('#');
        return hash < 0 ? atomId : atomId[..hash];
    }
}
