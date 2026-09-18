using System.Text;
using AutoAnthonyRelics.Data;
using AutoAnthonyRelics.Generation;

namespace RelicEligibilityProbe;

/// <summary>
/// Isolated probe for WS-0916-05: generation-time execution-context
/// eligibility of relic trigger/effect pairs.
///
/// WHAT IT COMPILES: the mod's pure source layer (Data + Generation) is
/// compiled INTO this probe, so the assertions bind to the current source
/// rather than to a possibly stale mod build, and no Godot/BaseLib/Godot-static
/// access is involved. It is deliberately not a second copy of the generator:
/// the exclusion TABLE is asserted equal to the executor contract transcribed
/// below, so the generator and the executor cannot drift apart silently.
///
/// WHAT IT PROVES (source-level, not a game run):
/// 1. the pool shape and the pair space,
/// 2. the generator's exclusion table equals the executor's skip conditions on
///    every trigger/effect pair of the pool,
/// 3. the new context rules only ever ADD exclusions,
/// 4. for a fixed seed, the partition of the pair space into
///    forbidden / emitted / eligible-but-unused,
/// 5. no generated relic over a 200-seed sweep contains a pair the executor
///    would skip in the context its trigger provides,
/// 6. every effect fragment still has at least one legal trigger and is still
///    reachable.
///
/// WHAT IT DOES NOT PROVE: that the engine really behaves as the decompile
/// says. That evidence is the decompile itself (sts2.decompiled.cs) plus the
/// executor source; it is cited in RelicGenerator.Excluded's doc comment. No
/// game run backs this probe.
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

    // ---------- The executor contract, as the probe's independent oracle ----------
    //
    // Transcribed from AnthonyRelicModel (the out-of-combat guard, the
    // all_enemies cases, the ModifyHandDraw passive hook) and from the engine
    // decompile (PlayerCombatState is assigned once and never nulled, so null
    // means "no combat yet this run"; HittableEnemies is empty once every enemy
    // is dead, which is guaranteed at combat end/victory).
    //
    // It is duplicated HERE on purpose: the probe is the oracle the product
    // table is compared against. If the product rule changes without this
    // contract changing, check 2 fails.

    /// <summary>Triggers that can fire with PlayerCombatState == null.</summary>
    private static readonly HashSet<string> NoCombatContextTriggers =
        new(StringComparer.Ordinal) { "obtained", "gold_gained" };

    /// <summary>Triggers that fire only after every enemy of the combat is dead.</summary>
    private static readonly HashSet<string> EnemiesDeadTriggers =
        new(StringComparer.Ordinal) { "combat_end", "combat_victory" };

    private const string EnemyTarget = "all_enemies";

    /// <summary>
    /// Opcodes a passive relic may carry (mirrors RelicGenerator.PassiveOpcodes,
    /// which is private). Every one has an executor override in
    /// AnthonyRelicModel; this copy is the probe's independent oracle.
    /// </summary>
    private static readonly HashSet<string> PassiveOpcodes = new(StringComparer.Ordinal)
    {
        "modify_hand_draw", "modify_max_energy",
        "restrict_gold", "restrict_potion", "restrict_card_play",
        "restrict_draw", "modify_card_cost", "enemy_strength_gain",
        "retain_hand", "extra_turn", "expand_card_pool", "enchant_reward",
    };

    /// <summary>Restrictions must ship with their engine offset (v5 rule 5).</summary>
    private static readonly HashSet<string> RestrictionOpcodes = new(StringComparer.Ordinal)
    {
        "restrict_gold", "restrict_potion", "restrict_card_play",
        "restrict_draw", "modify_card_cost", "enemy_strength_gain",
    };

    /// <summary>True when the executor drops this effect in this trigger's context.</summary>
    private static bool ExecutorWouldSkip(string triggerKind, EffectFragment effect) =>
        (EffectFragment.CombatScopedOpcodes.Contains(effect.Opcode)
            && NoCombatContextTriggers.Contains(triggerKind))
        || (effect.Target == EnemyTarget && EnemiesDeadTriggers.Contains(triggerKind));

    /// <summary>Pre-v4 rules (recursion boundary + max-HP policy).</summary>
    private static bool LegacyExcluded(string? triggerKind, EffectFragment effect) =>
        (triggerKind is not null && triggerKind == "gold_gained" && effect.Opcode == "gain_gold")
        || (effect.Opcode == "gain_max_hp" && (triggerKind is null || triggerKind != "obtained"));

    /// <summary>
    /// Rules 6 and 7 (user order 2026-09-18): the extra-pool toggle and the
    /// hand/deck effect timing contract. Kept separate from
    /// <see cref="ExecutorWouldSkip"/> because they are generation POLICY, not
    /// executor context - and because the monotonicity check below has to treat
    /// them as legitimate additions.
    ///
    /// Rule 6 is read through GenerationSettings.Current, which is exactly what
    /// the generator reads. In this probe nothing binds ConfigSource, so Current
    /// resolves to the shipped defaults (extra pool OFF) - the configuration the
    /// mod ships with, and therefore the right default to model here.
    /// </summary>
    private static bool PolicyExcluded(string? triggerKind, EffectFragment effect) =>
        (effect.Pool == FragmentPoolKind.Extra && !GenerationSettings.Current.IncludeExtraPool)
        || (effect.IsHandEffect
            && (triggerKind is null || !EffectFragment.HandEffectTriggers.Contains(triggerKind)))
        || (effect.IsDeckEffect
            && (triggerKind is null || !EffectFragment.DeckEffectTriggers.Contains(triggerKind)))
        // Rule 8 (user order 2026-09-19): "disable all negative effects". Note it
        // depends on IsNegative, which is the union of the downside and
        // restriction opcodes - so the oracle stays honest only as long as it
        // reads the SAME property the generator reads rather than re-listing the
        // opcodes here (a re-listed copy would silently drift).
        || (effect.IsNegative && GenerationSettings.Current.DisableNegativeEffects);

    /// <summary>The full exclusion contract the generator must implement.</summary>
    private static bool OracleExcluded(string? triggerKind, EffectFragment effect) =>
        LegacyExcluded(triggerKind, effect)
        || (triggerKind is null && !PassiveOpcodes.Contains(effect.Opcode))
        || (triggerKind is not null && ExecutorWouldSkip(triggerKind, effect))
        || PolicyExcluded(triggerKind, effect);
        // NOTE rule 5 (restriction must have an engine offset) is deliberately
        // NOT an exclusion here: every restriction in RestrictionOpcodes has an
        // offset registered, so the generator's clause is dead for the current
        // pool. It is asserted positively below instead (a restriction relic
        // must carry its offset), which is the property that actually matters.

    public static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        RelicAtomPool atoms = RelicAtomData.LoadAtoms();
        RelicLedger ledger = RelicAtomData.LoadLedger();
        // includeExtraPool: true - the pool is built with the extra atoms ALWAYS
        // present in the mod (MainFile), and the toggle is applied at generation
        // time by rule 6. Building with the extra pool off here would shrink the
        // pool and make the oracle disagree with the generator about rule 6.
        RelicFragmentPool pool = RelicFragmentPool.Build(atoms, ledger, includeExtraPool: true);

        // ---- 1. Pool shape.
        var kinds = pool.Triggers.Select(t => t.Kind).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var opcodes = pool.TriggeredEffects.Select(e => e.Opcode).Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal).ToList();
        var shapes = pool.TriggeredEffects.Select(e => e.ShapeKey).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var combatShapes = pool.TriggeredEffects.Where(e => EffectFragment.CombatScopedOpcodes.Contains(e.Opcode))
            .Select(e => e.ShapeKey).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var enemyShapes = pool.TriggeredEffects.Where(e => e.Target == EnemyTarget)
            .Select(e => e.ShapeKey).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Console.WriteLine("---- pool (ledger-filtered) ----");
        Console.WriteLine($"trigger fragments (Kind|Condition) : {pool.Triggers.Count}");
        Console.WriteLine($"distinct trigger Kinds            : {kinds.Count}  {string.Join(", ", kinds)}");
        Console.WriteLine($"triggered effect fragments        : {pool.TriggeredEffects.Count}");
        Console.WriteLine($"distinct effect opcodes           : {opcodes.Count}");
        Console.WriteLine($"distinct effect shapes            : {shapes.Count}");
        Console.WriteLine($"passive fragments                 : {pool.PassiveEffects.Count}");
        Console.WriteLine($"benefit fragments                 : {pool.BenefitEffects.Count}");
        Console.WriteLine($"restriction fragments             : {pool.PassiveEffects.Count(e => e.IsRestriction)}");
        Console.WriteLine($"combat-scoped shapes              : {combatShapes.Count}");
        foreach (string shape in combatShapes)
        {
            Console.WriteLine($"    {shape}");
        }
        Console.WriteLine($"all_enemies shapes                : {enemyShapes.Count}");
        foreach (string shape in enemyShapes)
        {
            Console.WriteLine($"    {shape}");
        }
        Console.WriteLine();
        Console.WriteLine("---- pair space ----");
        Console.WriteLine($"Kinds x opcodes   : {kinds.Count} x {opcodes.Count} = {kinds.Count * opcodes.Count}");
        Console.WriteLine($"Kinds x shapes    : {kinds.Count} x {shapes.Count} = {kinds.Count * shapes.Count}");
        Console.WriteLine($"trigger keys x shapes : {pool.Triggers.Count} x {shapes.Count} = {pool.Triggers.Count * shapes.Count}");

        // ---- 2. The exclusion table equals the executor contract, on every pair.
        bool tableOk = true;
        bool monotoneOk = true;
        var forbidden = new List<string>();
        foreach (string kind in kinds)
        {
            TriggerFragment representative = pool.Triggers.First(t => t.Kind == kind);
            foreach (EffectFragment effect in pool.TriggeredEffects)
            {
                bool actual = RelicGenerator.Excluded(representative, effect);
                bool expected = OracleExcluded(kind, effect);
                if (actual != expected)
                {
                    tableOk = false;
                    Console.WriteLine($"  table mismatch: {kind} x {effect.ShapeKey}: generator={actual} oracle={expected}");
                }
                // The context rules must only ADD exclusions. Policy rules (6/7)
                // are legitimate additions too - they are not context-derived, so
                // they are excluded from this check rather than treated as
                // violations.
                if (!LegacyExcluded(kind, effect) && actual && !ExecutorWouldSkip(kind, effect)
                    && !PolicyExcluded(kind, effect))
                {
                    monotoneOk = false;
                    Console.WriteLine($"  non-context exclusion added: {kind} x {effect.ShapeKey}");
                }
                if (expected)
                {
                    forbidden.Add($"{kind} x {effect.ShapeKey}");
                }
            }
        }
        foreach (EffectFragment passive in pool.PassiveEffects)
        {
            bool actual = RelicGenerator.Excluded(null, passive);
            bool expected = OracleExcluded(null, passive);
            if (actual != expected)
            {
                tableOk = false;
                Console.WriteLine($"  table mismatch (passive): {passive.ShapeKey}: generator={actual} oracle={expected}");
            }
            if (expected)
            {
                forbidden.Add($"(passive) x {passive.ShapeKey}");
            }
        }
        Check(tableOk, "exclusion table equals the executor contract on every pool pair");
        Check(monotoneOk, "context rules only add exclusions (no legacy-eligible pair becomes ineligible for another reason)");

        // ---- 3. Soundness of each rule, stated separately.
        var nullCtxViolations = (from kind in kinds
                                 from e in pool.TriggeredEffects
                                 where NoCombatContextTriggers.Contains(kind)
                                    && EffectFragment.CombatScopedOpcodes.Contains(e.Opcode)
                                 where !RelicGenerator.Excluded(pool.Triggers.First(t => t.Kind == kind), e)
                                 select $"{kind} x {e.ShapeKey}").ToList();
        Check(nullCtxViolations.Count == 0,
            "rule (a): every no-combat trigger x combat-scoped effect pair is excluded",
            string.Join(", ", nullCtxViolations));

        var deadEnemyViolations = (from kind in kinds
                                   from e in pool.TriggeredEffects
                                   where EnemiesDeadTriggers.Contains(kind) && e.Target == EnemyTarget
                                   where !RelicGenerator.Excluded(pool.Triggers.First(t => t.Kind == kind), e)
                                   select $"{kind} x {e.ShapeKey}").ToList();
        Check(deadEnemyViolations.Count == 0,
            "rule (b): every dead-enemy trigger x all_enemies pair is excluded",
            string.Join(", ", deadEnemyViolations));

        // Coarser (Kind x opcode) view of the same table: 12 x 13 = 156 pairs,
        // the space the fixed-seed partition below is reported against.
        var forbiddenKindOpcode = new HashSet<string>(StringComparer.Ordinal);
        foreach (string kind in kinds)
        {
            foreach (EffectFragment effect in pool.TriggeredEffects)
            {
                if (OracleExcluded(kind, effect))
                {
                    forbiddenKindOpcode.Add($"{kind}|{effect.Opcode}");
                }
            }
        }

        int ruleA = NoCombatContextTriggers.Count * combatShapes.Count;
        int ruleBNew = (from kind in EnemiesDeadTriggers
                        from shape in enemyShapes
                        where !(NoCombatContextTriggers.Contains(kind)
                            && EffectFragment.CombatScopedOpcodes.Contains(shape.Split('|')[0]))
                        select shape).Count();
        Console.WriteLine();
        Console.WriteLine("---- rule (a)/(b) reach over the pool ----");
        Console.WriteLine($"rule (a) rejects : {NoCombatContextTriggers.Count} no-combat triggers x {combatShapes.Count} combat-scoped shapes = {ruleA} (Kind x shape)");
        Console.WriteLine($"rule (b) rejects : {EnemiesDeadTriggers.Count} dead-enemy triggers x {enemyShapes.Count} all_enemies shapes = {EnemiesDeadTriggers.Count * enemyShapes.Count}, of which {ruleBNew} are not already rejected by (a)");
        Console.WriteLine($"context rejections (union, Kind x shape) : {ruleA + ruleBNew} of {kinds.Count * shapes.Count}");
        Console.WriteLine($"all exclusions      (union, Kind x shape) : {forbidden.Count} of {kinds.Count * shapes.Count}");
        Console.WriteLine($"all exclusions      (Kind x opcode)      : {forbiddenKindOpcode.Count} of {kinds.Count * opcodes.Count}");
        Console.WriteLine("  NOTE opcode granularity is coarser than the rule: apply_power's all_enemies");
        Console.WriteLine("  variant is forbidden under the dead-enemy triggers while its self variants are");
        Console.WriteLine("  legal there, so a Kind x opcode count cannot express the rule exactly.");

        // ---- 4. Fixed seed: partition the (Kind x opcode) space.
        const string fixedSeed = "eligibility-fixed-2026-09-16";
        IReadOnlyList<GeneratedRelicDefinition> run = RelicGenerator.Generate(fixedSeed, pool);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (GeneratedRelicDefinition definition in run)
        {
            if (definition.Trigger is null)
            {
                continue;
            }
            foreach (EffectFragment effect in definition.Effects)
            {
                emitted.Add($"{definition.Trigger.Kind}|{effect.Opcode}");
            }
        }
        int emittedCount = emitted.Count;
        int unusedEligible = kinds.Count * opcodes.Count - emittedCount - forbiddenKindOpcode.Count;
        Console.WriteLine();
        Console.WriteLine($"---- fixed seed '{fixedSeed}' over the {kinds.Count * opcodes.Count}-pair (Kind x opcode) space ----");
        Console.WriteLine($"forbidden by eligibility : {forbiddenKindOpcode.Count}");
        Console.WriteLine($"emitted by this seed     : {emittedCount}");
        Console.WriteLine($"eligible but not drawn   : {unusedEligible}");
        Console.WriteLine("forbidden pairs:");
        foreach (string pair in forbiddenKindOpcode.OrderBy(p => p, StringComparer.Ordinal))
        {
            Console.WriteLine($"    {pair}");
        }

        // ---- 5. No generated relic contains a pair the executor would skip.
        var skipped = new List<string>();
        var passiveDead = new List<string>();
        for (int i = 0; i < 200; i++)
        {
            IReadOnlyList<GeneratedRelicDefinition> sweep = RelicGenerator.Generate($"eligibility-soak-{i}", pool);
            foreach (GeneratedRelicDefinition definition in sweep)
            {
                if (definition.Trigger is null)
                {
                    foreach (EffectFragment effect in definition.Effects)
                    {
                        if (!PassiveOpcodes.Contains(effect.Opcode))
                        {
                            passiveDead.Add($"seed {i} slot {definition.Slot}: {effect.ShapeKey}");
                        }
                    }
                    // v5 rule 5, asserted positively: a restriction never ships
                    // without its offsetting benefit in the same relic.
                    var restriction = definition.Effects.FirstOrDefault(e => RestrictionOpcodes.Contains(e.Opcode));
                    if (restriction is not null
                        && !definition.Effects.Any(e => !RestrictionOpcodes.Contains(e.Opcode)))
                    {
                        passiveDead.Add($"seed {i} slot {definition.Slot}: bare restriction {restriction.ShapeKey}");
                    }
                    continue;
                }
                foreach (EffectFragment effect in definition.Effects)
                {
                    if (ExecutorWouldSkip(definition.Trigger.Kind, effect))
                    {
                        skipped.Add($"seed {i} slot {definition.Slot}: {definition.Trigger.Kind} x {effect.ShapeKey}");
                    }
                }
            }
        }
        Check(skipped.Count == 0, "200-seed sweep: no relic pairs an effect with a trigger that would skip it",
            skipped.Count == 0 ? "" : string.Join(" | ", skipped.Take(5)));
        Check(passiveDead.Count == 0, "200-seed sweep: every passive relic carries the passive opcode",
            passiveDead.Count == 0 ? "" : string.Join(" | ", passiveDead.Take(5)));

        // ---- 6. Every fragment keeps a legal trigger, and stays reachable.
        // Extra-pool fragments are exempt while the toggle is off: with the pool
        // disabled they have NO legal trigger by design (rule 6), and asserting
        // otherwise would assert the toggle does nothing. They are exercised
        // separately, with the toggle forced on, below.
        bool everyEffectHasLegalTrigger = true;
        foreach (EffectFragment effect in pool.TriggeredEffects)
        {
            if (effect.Pool == FragmentPoolKind.Extra && !GenerationSettings.Current.IncludeExtraPool)
            {
                continue;
            }
            int legal = kinds.Count(k => !OracleExcluded(k, effect));
            if (legal == 0)
            {
                everyEffectHasLegalTrigger = false;
                Console.WriteLine($"  no legal trigger left for {effect.ShapeKey}");
            }
        }
        Check(everyEffectHasLegalTrigger, "every triggered effect fragment still has >= 1 legal trigger");

        // ---- 6b. The extra pool, forced ON, must be fully reachable AND fully
        // legal. This is the mirror of the exemption above: it proves the
        // toggle's OFF state is a policy gate and not a permanently dead pool.
        var extraOnUnreachable = new List<string>();
        var extraOnIllegal = new List<string>();
        GenerationSettings.FreezeExplicit(includeExtraPool: true, weightTriggeredCore: 100,
            weightPassiveCore: 100, weightBenefitCore: 100, weightExtra: 100);
        try
        {
            var extraKeys = pool.TriggeredEffects.Where(e => e.Pool == FragmentPoolKind.Extra)
                .Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
            foreach (string key in extraKeys)
            {
                var effect = pool.TriggeredEffects.First(e => e.Key == key);
                if (kinds.All(k => OracleExcluded(k, effect)))
                {
                    extraOnIllegal.Add(effect.ShapeKey);
                }
            }
            var seenExtra = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 200; i++)
            {
                foreach (GeneratedRelicDefinition definition in RelicGenerator.Generate($"eligibility-extra-{i}", pool))
                {
                    foreach (EffectFragment effect in definition.Effects)
                    {
                        if (effect.Pool == FragmentPoolKind.Extra)
                        {
                            seenExtra.Add(effect.Key);
                        }
                    }
                }
            }
            extraOnUnreachable.AddRange(extraKeys.Where(k => !seenExtra.Contains(k)));
        }
        finally
        {
            GenerationSettings.Unfreeze();
        }
        Check(extraOnIllegal.Count == 0, "extra pool ON: every extra fragment has >= 1 legal trigger",
            extraOnIllegal.Count == 0 ? "" : string.Join(", ", extraOnIllegal));
        Check(extraOnUnreachable.Count == 0, "extra pool ON: every extra fragment reachable in 200 seeds",
            extraOnUnreachable.Count == 0 ? "" : string.Join(",", extraOnUnreachable));

        // ---- 6c. Timing contract (rule 7), independent of the toggle.
        //
        // MUST run with the extra pool ON: with it off, rule 6 already excludes
        // every hand/deck effect, so a check written against the default settings
        // is VACUOUS - it passed even with rule 7 disabled entirely (verified
        // 2026-09-19). Only with the pool enabled does rule 7 carry the load.
        var timingViolations = new List<string>();
        GenerationSettings.FreezeExplicit(includeExtraPool: true, weightTriggeredCore: 100,
            weightPassiveCore: 100, weightBenefitCore: 100, weightExtra: 100);
        try
        {
            timingViolations.AddRange(from kind in kinds
                                      from e in pool.TriggeredEffects
                                      where e.IsHandEffect && !EffectFragment.HandEffectTriggers.Contains(kind)
                                      where !RelicGenerator.Excluded(pool.Triggers.First(t => t.Kind == kind), e)
                                      select $"{kind} x {e.ShapeKey}");
            timingViolations.AddRange(from kind in kinds
                                      from e in pool.TriggeredEffects
                                      where e.IsDeckEffect && !EffectFragment.DeckEffectTriggers.Contains(kind)
                                      where !RelicGenerator.Excluded(pool.Triggers.First(t => t.Kind == kind), e)
                                      select $"{kind} x {e.ShapeKey}");
        }
        finally
        {
            GenerationSettings.Unfreeze();
        }
        // Non-vacuity guard: rule 7 only carries load if the pool actually
        // contains hand/deck effects paired with triggers rule 7 forbids. Count
        // the pairs rule 7 must REJECT - with rule 7 disabled these become
        // eligible and the assertion above fails, which is what makes the pair
        // load-bearing (the first version of this check counted every
        // hand/deck pair, which is trivially non-zero and proved nothing).
        int rule7MustReject = (from kind in kinds
                               from e in pool.TriggeredEffects
                               where (e.IsHandEffect && !EffectFragment.HandEffectTriggers.Contains(kind))
                                  || (e.IsDeckEffect && !EffectFragment.DeckEffectTriggers.Contains(kind))
                               select $"{kind} x {e.ShapeKey}").Count();
        Check(rule7MustReject > 0,
            "rule 7: the pool contains pairs rule 7 must reject (the timing check is not vacuous)",
            $"{rule7MustReject} pairs");
        Check(timingViolations.Count == 0, "rule 7: no hand effect on a pre-draw trigger, no deck effect off `obtained`",
            timingViolations.Count == 0 ? "" : string.Join(", ", timingViolations.Take(5)));

        var usedEffects = new HashSet<string>(StringComparer.Ordinal);
        var usedTriggers = new HashSet<string>(StringComparer.Ordinal);
        int benefitSlots = 0;
        int passiveSlots = 0;
        int triggeredSlots = 0;
        var usedRestrictions = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 200; i++)
        {
            foreach (GeneratedRelicDefinition definition in RelicGenerator.Generate($"eligibility-soak-{i}", pool))
            {
                if (definition.Trigger is not null)
                {
                    usedTriggers.Add(definition.Trigger.Key);
                    triggeredSlots++;
                }
                else
                {
                    bool hasRestriction = definition.Effects.Any(e => RestrictionOpcodes.Contains(e.Opcode));
                    bool hasBenefit = definition.Effects.Any(e => e.IsBenefit);
                    if (hasRestriction)
                    {
                        foreach (EffectFragment e in definition.Effects.Where(x => RestrictionOpcodes.Contains(x.Opcode)))
                        {
                            usedRestrictions.Add(e.Opcode);
                        }
                    }
                    // Bands are disjoint by construction (one roll): a
                    // restriction relic is a passive slot, a strict-benefit
                    // relic is a benefit slot.
                    if (hasBenefit && !hasRestriction)
                    {
                        benefitSlots++;
                    }
                    else
                    {
                        passiveSlots++;
                    }
                }
                foreach (EffectFragment effect in definition.Effects)
                {
                    usedEffects.Add(effect.Key);
                }
            }
        }
        var missingEffects = pool.TriggeredEffects.Concat(pool.PassiveEffects).Concat(pool.BenefitEffects)
            .Select(e => e.Key).Distinct(StringComparer.Ordinal).Where(k => !usedEffects.Contains(k))
            // Extra-pool fragments are exempt with the toggle off (rule 6); they
            // are asserted reachable separately with the toggle forced on.
            .Where(k => !pool.TriggeredEffects.Any(e => e.Key == k
                && e.Pool == FragmentPoolKind.Extra && !GenerationSettings.Current.IncludeExtraPool))
            .ToList();
        Check(missingEffects.Count == 0, "200-seed sweep: every effect fragment reachable",
            missingEffects.Count == 0 ? "" : string.Join(",", missingEffects));
        Check(usedTriggers.Count == pool.Triggers.Count, "200-seed sweep: every trigger fragment reachable",
            $"{usedTriggers.Count}/{pool.Triggers.Count}");
        // The point of the exercise: all six Ancient restrictions must be
        // reachable, not just the alphabetically first one.
        var allRestrictions = pool.PassiveEffects.Where(e => e.IsRestriction).Select(e => e.Opcode)
            .Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal).ToList();
        var unreached = allRestrictions.Where(o => !usedRestrictions.Contains(o)).ToList();
        Check(unreached.Count == 0, "200-seed sweep: every restriction affix reachable",
            unreached.Count == 0 ? "" : string.Join(",", unreached));

        // Measured band rates. The band is decided by ONE roll against the
        // generator's own constants, so the nominal rates are 5 / 15 / 80.
        //
        // The BENEFIT band tracks its nominal rate closely, so it is asserted
        // against it. The PASSIVE band does NOT, and the assertion that used to
        // demand 15% +- 3 was simply wrong (it failed at HEAD, before this
        // change - measured 10.97% on f1c34cc, 11.23% here). Reason, verified
        // from the pool: of the 8 passive fragments, 6 are restrictions, leaving
        // only TWO plain passives. A 60-slot run expects ~9 passive-band slots
        // but can only fill ~8 shapes before both plain passives are consumed,
        // and the restriction branch fires only 30% of the time - so the slot
        // falls through to the triggered path and the EFFECTIVE rate is
        // structurally below nominal.
        //
        // So the honest invariants are: the effective rate can never EXCEED the
        // nominal one (exhaustion only removes slots), and it must stay high
        // enough that the band is genuinely populated. A regression that
        // inverted the bands (passive jumping to ~80%) fails the upper bound.
        int totalSlots = benefitSlots + passiveSlots + triggeredSlots;
        Console.WriteLine();
        Console.WriteLine("---- measured slot bands over 200 seeds ----");
        Console.WriteLine($"benefit   : {benefitSlots,6} / {totalSlots} = {100.0 * benefitSlots / totalSlots:F2}%  (nominal 5%)");
        Console.WriteLine($"passive   : {passiveSlots,6} / {totalSlots} = {100.0 * passiveSlots / totalSlots:F2}%  (nominal 15%, content-limited)");
        Console.WriteLine($"triggered : {triggeredSlots,6} / {totalSlots} = {100.0 * triggeredSlots / totalSlots:F2}%  (nominal 80%)");
        Console.WriteLine($"restrictions seen: {string.Join(", ", usedRestrictions.OrderBy(o => o, StringComparer.Ordinal))}");
        Check(Math.Abs(100.0 * benefitSlots / totalSlots - 5.0) < 2.0,
            "measured benefit band is within 2 points of 5%",
            $"{100.0 * benefitSlots / totalSlots:F2}%");
        Check(100.0 * passiveSlots / totalSlots <= 15.0 + 0.5,
            "measured passive band never exceeds its nominal 15%",
            $"{100.0 * passiveSlots / totalSlots:F2}%");
        Check(100.0 * passiveSlots / totalSlots >= 8.0,
            "measured passive band stays populated (>= 8%)",
            $"{100.0 * passiveSlots / totalSlots:F2}%");

        // ---- 7. Determinism is untouched by the new rules.
        var again = RelicGenerator.Generate(fixedSeed, pool);
        Check(again.Select(d => d.Fingerprint).SequenceEqual(run.Select(d => d.Fingerprint)),
            "same seed -> byte-identical relic set after the eligibility change");

        Console.WriteLine();
        Console.WriteLine("NOTE  rule (a) and rule (b) are derived from the engine decompile");
        Console.WriteLine("      (PlayerCombatState assigned once and never nulled; HittableEnemies empty");
        Console.WriteLine("      once every enemy is dead, which combat end/victory guarantee) plus the");
        Console.WriteLine("      executor source in AnthonyRelicModel. No game run backs these assertions.");
        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "PROBE OK" : $"PROBE FAILED: {_failures} check(s)");
        return _failures == 0 ? 0 : 1;
    }
}
