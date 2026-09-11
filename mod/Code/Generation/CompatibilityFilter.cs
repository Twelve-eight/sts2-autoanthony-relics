using AutoAnthonyRelics.Data;

namespace AutoAnthonyRelics.Generation;

/// <summary>The shell's metadata, which the compatibility rules branch on.</summary>
internal readonly record struct ShellContext(
    GeneratedCardType Type,
    GeneratedTargetMode Target,
    int Cost,
    bool HasStarCostX);

/// <summary>
/// Per-slot candidate filter. Every rule here is a REJECTION, so the original's
/// nested if/goto structure collapses into a flat conjunction without changing
/// the outcome; the rules are named so a probe run can report which one fired.
///
/// FIDELITY
///   Reproduced exactly: every rule the mechanism contract depends on - two
///   triggers never stack, a trigger's payoff must be legal, a dependency prefix
///   must be followed by a legal payoff, a restricted effect never attaches to a
///   repeated trigger, target resolution, X-resource availability, unique-effect
///   and field-count caps.
///
///   APPROXIMATED (see research/fidelity-ledger.md): the rules whose inputs are
///   the original's valuation/balance tables, which the reused data does not
///   carry - "is this effect beneficial", "is this effect negative",
///   "is this damage attack-classifying", and the compiled standalone_keyword /
///   api_power_foundation flags (all zero occurrences in the pool).
/// </summary>
internal static class CompatibilityFilter
{
    public static string? RejectReason(
        ShellContext shell,
        IReadOnlyList<GeneratedOperation> previous,
        GeneratedOperation candidate)
    {
        GeneratedOperation? last = previous.Count > 0 ? previous[^1] : null;

        // --- identity / multiplicity ---
        if (GenerationRules.WouldDuplicateCardUniqueEffect(previous, candidate))
        {
            return "duplicate-unique-effect";
        }
        if (GenerationRules.FieldOccurrenceCount(previous, candidate) >= 2)
        {
            return "field-occurrence-cap";
        }

        // --- X resource ---
        GenerationRules.XRequirement xRequirement = GenerationRules.XRequirementOf(candidate);
        if (xRequirement != GenerationRules.XRequirement.None)
        {
            foreach (GeneratedOperation operation in previous)
            {
                if (GenerationRules.XRequirementOf(operation) != GenerationRules.XRequirement.None
                    && GenerationRules.HasSameXEffectKind(operation, candidate))
                {
                    return "duplicate-x-effect-kind";
                }
            }
        }
        bool xAvailable = xRequirement switch
        {
            GenerationRules.XRequirement.None => shell.Cost != -1 && !shell.HasStarCostX,
            GenerationRules.XRequirement.Energy => shell.Cost == -1 && !shell.HasStarCostX,
            GenerationRules.XRequirement.Star => shell.Cost != -1 && shell.HasStarCostX,
            GenerationRules.XRequirement.Either => shell.Cost == -1 || shell.HasStarCostX,
            _ => false,
        };
        if (!xAvailable)
        {
            return "x-requirement-unavailable";
        }

        // --- self cost manipulation ---
        if ((shell.Cost == -1 || shell.HasStarCostX) && GenerationRules.IsSelfCostChange(candidate))
        {
            return "self-cost-change-on-x-shell";
        }
        if (candidate.Template == "R:DoubleEitherXAtThreshold"
            && !previous.Any(op => op.Template != "R:DoubleEitherXAtThreshold"
                && op.Spec.Values.Any(v => v.Source is "energy_x" or "star_x" or "special_x")))
        {
            return "double-either-x-without-x-source";
        }

        // --- the two cost anchors ---
        if (candidate.Template is "D:IncreaseThisCardCost" or "D:IncreaseAllClaws")
        {
            if (shell.Type == GeneratedCardType.Power
                || (last != null && (GenerationRules.TriggerNeedsLinkedEffect(last) || GenerationRules.IsDependencyPrefix(last))))
            {
                return "cost-anchor-misplaced";
            }
        }

        // --- next-attack-grant family ---
        if (GenerationRules.IsNextAttackGrantTrigger(candidate) && shell.Type == GeneratedCardType.Power)
        {
            return "next-attack-grant-on-power";
        }
        if (last != null && GenerationRules.IsNextAttackGrantTrigger(last)
            && !GenerationRules.IsNextAttackGrantPayoff(candidate))
        {
            return "next-attack-grant-needs-payoff";
        }
        if (candidate.Template is "I:ReplayAttack" or "I:SetCostZero"
            && (last == null || !GenerationRules.IsNextAttackGrantTrigger(last)))
        {
            return "replay-attack-needs-grant";
        }
        if (candidate.Template == "I:DoubleAttackDamageNextTurn" && last != null
            && GenerationRules.TriggerNeedsLinkedEffect(last)
            && GenerationRules.IsRepeatedTriggerOrCondition(last))
        {
            return "double-attack-damage-on-repeated-trigger";
        }

        // --- current-block damage modifier ---
        if (GenerationRules.IsCurrentBlockDamageModifier(candidate)
            && (previous.Any(GenerationRules.IsCurrentBlockDamageModifier)
                || !previous.Any(op => op.Template == "T:D" && !GenerationRules.IsIntrinsicMultiHitDamage(op))))
        {
            return "current-block-modifier-without-anchor";
        }

        // --- restricted effects ---
        if (GenerationRules.IsRestrictedEffect(candidate) && last != null
            && (GenerationRules.TriggerNeedsLinkedEffect(last) || GenerationRules.IsDependencyPrefix(last))
            && GenerationRules.IsRepeatedTriggerOrCondition(last))
        {
            return "restricted-effect-on-repeated-trigger";
        }

        // --- trigger stacking: the mechanism contract's shape guarantee ---
        if (candidate.IsTriggerOrCondition && last != null && last.IsTriggerOrCondition)
        {
            return "trigger-stacks-on-trigger";
        }
        if (candidate.Scope == FragmentScope.AbilityRule && last != null && last.IsTriggerOrCondition)
        {
            return "rule-stacks-on-trigger";
        }
        if (shell.Type != GeneratedCardType.Power
            && candidate.Scope is FragmentScope.AbilityTrigger or FragmentScope.AbilityRule
            && candidate.Template is not ("CL:AfterTurns" or "CL:DieOnUnblockedAttack"))
        {
            return "trigger-scope-on-non-power";
        }

        // --- dependency prefix / payoff pairing ---
        if (last != null && GenerationRules.TriggerNeedsLinkedEffect(last)
            && GenerationRules.IsStandaloneEventDependencyPrefix(candidate))
        {
            return "standalone-event-prefix-after-trigger";
        }
        if (last != null && GenerationRules.IsDependencyPrefix(last)
            && !GenerationRules.IsLegalDependencyPayoff(last, candidate))
        {
            return "illegal-dependency-payoff";
        }
        if (GenerationRules.RequiresDependencyPrefix(candidate)
            && (last == null || !GenerationRules.IsLegalDependencyPayoff(last, candidate)))
        {
            return "payoff-without-prefix";
        }
        if (candidate.Template == "D:RepeatPerEnergySpentThisTurn"
            && last?.Template != "D:ForEachEnergySpentThisTurn")
        {
            return "repeat-per-energy-without-prefix";
        }
        if (candidate.Template == "C:forEachDiscarded"
            && !previous.Any(op => op.Template is "N:Discard" or "N:DiscardAll" or "I:DiscardHandDrawSame"))
        {
            return "forEachDiscarded-without-discard";
        }
        if (candidate.Template == "I:AddExhaustedAttackDamage" && last?.Template != "I:ExhaustRandomAttack")
        {
            return "addExhaustedAttackDamage-without-exhaust";
        }

        // --- trigger payload requirements ---
        if (GenerationRules.RequiresSpecificTriggerPayload(candidate)
            && (last == null || !GenerationRules.CanSupplySpecificTriggerPayload(last, candidate)))
        {
            return "trigger-payload-unavailable";
        }
        if (GenerationRules.IsHitEnemyDamageVariant(candidate)
            && !GenerationRules.HasLightningEvokeTriggerContext(previous))
        {
            return "hit-enemy-without-lightning-context";
        }

        // --- extreme downsides ---
        if (GenerationRules.IsExtremeLifecycleDownside(candidate) && last != null
            && (GenerationRules.TriggerNeedsLinkedEffect(last) || last.HasTriggerLink))
        {
            return "extreme-downside-after-trigger";
        }

        // --- target resolution ---
        if (shell.Target == GeneratedTargetMode.Other
            && GenerationRules.RequiresSingleEnemyTarget(candidate)
            && !(last != null && GenerationRules.IsNextAttackGrantTrigger(last))
            && !GenerationRules.UsesExplicitRandomEnemyTarget(candidate)
            && (last == null || !GenerationRules.CanResolveTriggeredEnemyTarget(last, candidate)))
        {
            return "unresolvable-enemy-target";
        }
        if (last != null && GenerationRules.TriggerLosesOriginalEnemyTarget(last)
            && GenerationRules.RequiresSingleEnemyTarget(candidate)
            && !GenerationRules.CanResolveTriggeredEnemyTarget(last, candidate))
        {
            return "deferred-enemy-target-lost";
        }

        // --- misc pairings ---
        if (candidate.Template == "N:RetaliateDamage" && last != null
            && GenerationRules.TriggerNeedsLinkedEffect(last)
            && last.Spec.Trigger?.Kind != "attack_received")
        {
            return "retaliate-needs-attack-received";
        }
        if (last != null && GenerationRules.TriggerNeedsLinkedEffect(last)
            && GenerationRules.RequiresPlayerChoice(candidate)
            && !GenerationRules.TriggerSupportsPlayerChoice(last))
        {
            return "player-choice-context-unsupported";
        }
        if (last != null && GenerationRules.IsSelfExhaustEventTrigger(last)
            && GenerationRules.IsNegativeEffect(candidate))
        {
            return "negative-payoff-on-self-exhaust";
        }
        if (candidate.Scope == FragmentScope.Modifier && last != null
            && GenerationRules.IsSelfZoneStateCondition(last))
        {
            return "modifier-on-self-zone-condition";
        }
        if (candidate.Template == "R:PlayThisCard"
            && (shell.Type == GeneratedCardType.Power
                || last?.Template != "R:AtTurnStartIfInExhaust"
                || !previous.Take(previous.Count - 1).Any(IsOrdinaryOnPlayEffect)))
        {
            return "play-this-card-misplaced";
        }
        if (GenerationRules.IsGrandFinalePayoff(candidate)
            && !previous.Any(GenerationRules.IsDrawPileEmptyCondition))
        {
            return "grand-finale-without-draw-pile-empty";
        }

        return null;
    }

    /// <summary>
    /// The original's IsOrdinaryOnPlayEffect: a plain, unlinked, non-self-replay
    /// effect in the on-play zone.
    /// </summary>
    private static bool IsOrdinaryOnPlayEffect(GeneratedOperation operation) =>
        !operation.HasTriggerLink
        && operation.Template != "I:PlayThisCard"
        && operation.Scope is FragmentScope.SingleEnemyOnly or FragmentScope.NonTargeted or FragmentScope.Independent;

    /// <summary>
    /// The original's CanCompleteFinalPlannedSlot, reduced to the coherence
    /// intent: the finished card must look like the shell's type, must have a
    /// resolvable enemy target if the shell is single-target, and must be worth
    /// paying for if it costs anything.
    ///
    /// APPROXIMATED: HasAttackClassifyingDamage / IsBeneficialEffect are the
    /// valuation tables we do not have, so "deals enemy damage" and "is not a
    /// negative effect" stand in for them.
    /// </summary>
    public static string? FinalSlotRejectReason(ShellContext shell, IReadOnlyList<GeneratedOperation> operations)
    {
        if (operations.Count == 0)
        {
            return "empty-assembly";
        }
        GeneratedOperation last = operations[^1];
        if (GenerationRules.TriggerNeedsLinkedEffect(last))
        {
            return "final-slot-is-unfulfilled-trigger";
        }
        if (GenerationRules.IsDependencyPrefix(last))
        {
            return "final-slot-is-dependency-prefix";
        }

        bool hasAttackDamage = operations.Any(GenerationRules.IsEnemyDamage);
        if (shell.Type == GeneratedCardType.Attack && !hasAttackDamage)
        {
            return "attack-shell-without-damage";
        }
        if (shell.Type == GeneratedCardType.Skill && hasAttackDamage)
        {
            return "skill-shell-with-damage";
        }
        if (shell.Target == GeneratedTargetMode.SingleEnemy
            && !operations.Any(op => GenerationRules.RequiresSingleEnemyTarget(op)
                && !GenerationRules.UsesExplicitRandomEnemyTarget(op)
                && (!op.HasTriggerLink || op.TriggerIndex >= operations.Count
                    || GenerationRules.CanResolveTriggeredEnemyTarget(operations[op.TriggerIndex], op))))
        {
            return "single-enemy-shell-without-target";
        }
        if ((shell.Cost != 0 || shell.HasStarCostX)
            && !operations.Any(op => !GenerationRules.IsNegativeEffect(op)))
        {
            return "paid-shell-without-benefit";
        }
        return null;
    }

    /// <summary>
    /// Whole-assembly checks. These are the subset of the original's
    /// HasValidOperationAssembly that can fail even when every per-slot rule
    /// passed - i.e. the failures that only become visible once the list is
    /// complete. The remaining predicates in that 33-term conjunction are
    /// already enforced per-slot (see the rules above) or depend on the balance
    /// model and are listed in research/fidelity-ledger.md.
    /// </summary>
    public static string? AssemblyRejectReason(IReadOnlyList<GeneratedOperation> operations)
    {
        if (operations.Count == 0)
        {
            return "empty-assembly";
        }

        // HasValidFailableConditionAssembly
        var failable = new HashSet<int>();
        for (int i = 0; i < operations.Count; i++)
        {
            if (GenerationRules.IsFailableOneShotCondition(operations[i]))
            {
                failable.Add(i);
            }
        }
        if (failable.Count > 0 && !operations.Any(GenerationRules.IsPersistentPowerFoundation))
        {
            bool hasUnconditionalPayoff = false;
            foreach (GeneratedOperation operation in operations)
            {
                if (operation.Template.StartsWith("N_SELECT_", StringComparison.Ordinal))
                {
                    continue;
                }
                if (GenerationRules.IsFailableOneShotCondition(operation)
                    || GenerationRules.IsDependencyPrefix(operation)
                    || GenerationRules.IsNegativeEffect(operation))
                {
                    continue;
                }
                if (operation.TriggerIndex >= 0 && failable.Contains(operation.TriggerIndex))
                {
                    continue;
                }
                hasUnconditionalPayoff = true;
                break;
            }
            if (!hasUnconditionalPayoff)
            {
                return "failable-condition-without-unconditional-payoff";
            }
        }

        // HasNoFatalDoubleVulnerablePayoff / HasNoNegativeSelfExhaustPayoffs /
        // HasValidTriggerPayloadAssembly / HasNoStateConditionModifiers are all
        // enforced per-slot by the rules above, so re-checking them here would
        // only duplicate work.
        return null;
    }
}
