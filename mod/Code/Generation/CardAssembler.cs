using AutoAnthonyRelics.Data;

namespace AutoAnthonyRelics.Generation;

/// <summary>What happened during one assembly attempt, for diagnostics.</summary>
public sealed record AssemblyReport(
    int Attempts,
    string? LastRejectReason,
    int LastSlotCount,
    int PlannedSlotCount);

/// <summary>
/// The generator core: pick a shell, then fill its slots by sampling the
/// character's whole atom pool once per slot.
///
/// The shape of the loop is the mechanism contract's §4.1-4.3:
///
///   1. every slot re-filters the ENTIRE pool from scratch - there is no
///      "the rest of the original card" anywhere in this loop, which is what
///      makes a condition from one vanilla card able to gate an effect from
///      another;
///   2. the numeric slots are instantiated per draw (here: carried over);
///   3. the trigger link is decided LAST, by TriggerBinder, and written into
///      the operation.
///
/// What is deliberately NOT here (user decision, see research/fidelity-ledger.md):
///   * weighted selection (uniform instead of the original's two-stage family
///     weighting),
///   * the balance model (numeric values are carried over, not re-derived),
///   * the card-acceptance / uniqueness rejection loop, which is why
///     duplicateFailures is always 0 here.
/// </summary>
public sealed class CardAssembler
{
    /// <summary>The original's AssemblyAttemptsPerShell.</summary>
    public const int AssemblyAttemptsPerShell = 64;

    /// <summary>The original's MaximumAdaptiveEffectCount.</summary>
    public const int MaximumAdaptiveEffectCount = 8;

    private readonly GenerationPool _pool;
    private readonly DeterministicRandom _random;

    /// <summary>Binding-decision accounting for every assembly this instance performs.</summary>
    public BindingStats BindingStats { get; } = new();

    public string Character => _pool.Character;

    public CardAssembler(GenerationPool pool, DeterministicRandom random)
    {
        _pool = pool;
        _random = random;
    }

    /// <summary>
    /// Build one card of the requested rarity. Returns null only when every
    /// attempt was rejected - the caller can retry with a different seed.
    /// </summary>
    public GeneratedCard? Assemble(GeneratedRarity rarity, out AssemblyReport report)
    {
        string? lastReason = "no-attempt";
        int lastSlotCount = 0;
        int planned = 0;

        for (int attempt = 0; attempt < AssemblyAttemptsPerShell; attempt++)
        {
            AtomRecipe? shell = _random.PickUniform(_pool.ShellsOf(rarity));
            if (shell == null)
            {
                break;
            }
            GeneratedCard? card = TryAssembleShell(shell, out string? reason, out int slots, out planned);
            if (card != null)
            {
                report = new AssemblyReport(attempt + 1, null, slots, planned);
                return card;
            }
            lastReason = reason;
            lastSlotCount = slots;
        }

        report = new AssemblyReport(AssemblyAttemptsPerShell, lastReason, lastSlotCount, planned);
        return null;
    }

    private GeneratedCard? TryAssembleShell(AtomRecipe shell, out string? reason, out int slotCount, out int plannedSlotCount)
    {
        var context = new ShellContext(
            GenerationEnumParsing.ParseCardType(shell.Type),
            GenerationEnumParsing.ParseTargetMode(shell.Target),
            shell.Cost,
            shell.HasStarCostX);

        plannedSlotCount = _pool.PickComponentCount(_random);
        (int windowMinimum, int windowMaximum) = _pool.AdaptiveEffectCountWindow(0);
        plannedSlotCount = Math.Clamp(plannedSlotCount, windowMinimum, Math.Min(windowMaximum, MaximumAdaptiveEffectCount));

        var operations = new List<GeneratedOperation>();
        bool alreadyExtendedForDifficultCondition = false;

        for (int slot = 0; slot < plannedSlotCount; slot++)
        {
            var candidates = new List<JoinedFragment>();
            bool awaitingPayoff = GenerationRules.DifficultConditionAwaitsPayoff(operations);
            bool powerHasFoundation = operations.Any(GenerationRules.IsPersistentPowerFoundation);

            foreach (JoinedFragment fragment in _pool.Fragments)
            {
                var candidate = new GeneratedOperation(fragment, -1, ResolveValues(fragment));

                if (CompatibilityFilter.RejectReason(context, operations, candidate) != null)
                {
                    continue;
                }

                // A difficult condition must be answered before anything else.
                if (awaitingPayoff && !candidate.IsTriggerScope && !GenerationRules.IsDependencyPrefix(candidate))
                {
                    continue;
                }

                // The last slot cannot be a prefix - nothing would follow it.
                if (slot == plannedSlotCount - 1
                    && (GenerationRules.IsDependencyPrefix(candidate)
                        || candidate.Template == "D:ShuffleAllUnexhaustedIntoDraw"))
                {
                    continue;
                }

                // A next-attack grant needs room for its payoff.
                if (GenerationRules.IsNextAttackGrantTrigger(candidate)
                    && !(context.Type != GeneratedCardType.Power && slot == plannedSlotCount - 2))
                {
                    continue;
                }

                // The final slot must leave a card that looks like the shell.
                if (slot == plannedSlotCount - 1)
                {
                    var withCandidate = new List<GeneratedOperation>(operations) { candidate };
                    if (CompatibilityFilter.FinalSlotRejectReason(context, withCandidate) != null)
                    {
                        continue;
                    }
                }

                // A Power must open with a foundation.
                if (context.Type == GeneratedCardType.Power && slot == 0
                    && !GenerationRules.IsPersistentPowerFoundation(candidate)
                    && !GenerationRules.CanAnchorRestrictedPowerFoundation(candidate, _pool.Fragments))
                {
                    continue;
                }

                // ...and every later Power slot must keep it a Power.
                if (context.Type == GeneratedCardType.Power && slot != 0
                    && !powerHasFoundation
                    && !GenerationRules.IsRestrictedEffect(candidate))
                {
                    continue;
                }

                candidates.Add(fragment);
            }

            if (candidates.Count == 0)
            {
                break;
            }

            JoinedFragment chosen = _random.PickUniform(candidates)!;
            var values = ResolveValues(chosen);
            int triggerIndex = TriggerBinder.LinkedTriggerIndex(
                operations,
                new GeneratedOperation(chosen, -1, values),
                _random,
                BindingStats);
            var operation = new GeneratedOperation(chosen, triggerIndex, values);
            operations.Add(operation);

            // The original widens the plan when a trigger still owes a payoff;
            // without this a trigger landing in the last slot would always force
            // a full retry. This is a coherence rule, not a balance rule.
            if (GenerationRules.TriggerNeedsLinkedEffect(operation)
                && operations.Count >= plannedSlotCount
                && plannedSlotCount < 5)
            {
                plannedSlotCount = operations.Count + 1;
            }
            else if (!alreadyExtendedForDifficultCondition
                && GenerationRules.DifficultConditionTier(operation) >= 2
                && GenerationRules.TriggerNeedsLinkedEffect(operation)
                && plannedSlotCount < 5)
            {
                plannedSlotCount = Math.Min(5, Math.Max(plannedSlotCount + 1, operations.Count + 2));
                alreadyExtendedForDifficultCondition = true;
            }
        }

        slotCount = operations.Count;

        // The original's empty-assembly fallback: a bare 5-block Defend.
        if (operations.Count == 0)
        {
            JoinedFragment? fallback = FindFallbackBlockFragment();
            if (fallback == null)
            {
                reason = "no-fallback-fragment";
                return null;
            }
            operations.Add(new GeneratedOperation(fallback, -1, ResolveValues(fallback)));
        }

        if (GenerationRules.IsDependencyPrefix(operations[^1]))
        {
            reason = "trailing-dependency-prefix";
            return null;
        }
        if (GenerationRules.TriggerNeedsLinkedEffect(operations[^1]))
        {
            reason = "trailing-unfulfilled-trigger";
            return null;
        }
        string? assemblyReason = CompatibilityFilter.AssemblyRejectReason(operations);
        if (assemblyReason != null)
        {
            reason = assemblyReason;
            return null;
        }
        string? finalReason = CompatibilityFilter.FinalSlotRejectReason(context, operations);
        if (finalReason != null)
        {
            reason = finalReason;
            return null;
        }

        reason = null;
        return new GeneratedCard(
            Character,
            GenerationEnumParsing.ParseRarity(shell.OriginalRarity),
            shell.Id,
            shell.Cost,
            shell.StarCost,
            shell.HasStarCostX,
            context.Type,
            context.Target,
            shell.Tags.ToArray(),
            operations);
    }

    /// <summary>
    /// Numeric slots. POLICY: carry the atom's own value (BaseValue + Offset).
    /// The original re-derived these from EffectBalanceModel /
    /// NumericGenerationTuning / PercentageValueTuning, none of which exist in
    /// the reused data files; re-deriving would mean inventing a balance table.
    /// Every slot is kept - including the energy_x / star_x sources - because the
    /// interpreter needs to see them even though the value is not re-scaled.
    /// </summary>
    private static IReadOnlyList<ResolvedValue> ResolveValues(JoinedFragment fragment)
    {
        RuntimeSpec? spec = fragment.Spec;
        if (spec == null || spec.Values.Count == 0)
        {
            return Array.Empty<ResolvedValue>();
        }
        var values = new List<ResolvedValue>(spec.Values.Count);
        foreach (NumericSlot slot in spec.Values)
        {
            values.Add(new ResolvedValue(slot.Id, slot.BaseValue + slot.Offset, slot.Upgradable));
        }
        return values;
    }

    private JoinedFragment? FindFallbackBlockFragment()
    {
        foreach (JoinedFragment fragment in _pool.Fragments)
        {
            if (fragment.Template == "N:B"
                && fragment.Spec?.ParsedOpcode == SpecOpcode.GainBlock)
            {
                return fragment;
            }
        }
        return null;
    }
}
