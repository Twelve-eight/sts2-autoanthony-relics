namespace AutoAnthonyRelics.Generation;

/// <summary>
/// Accounting for every binding decision the generator makes.
///
/// This exists so the mechanism contract's "plain conditions bind about 50% of
/// the time" claim can be measured directly instead of inferred from card
/// output. CoinFlipBinds / (CoinFlipBinds + CoinFlipRejects) is that statistic;
/// TriggerSelfRejects proves the "a trigger is never re-bound" rule actually
/// fires rather than passing vacuously.
/// </summary>
public sealed class BindingStats
{
    /// <summary>Rule 5: an unfulfilled trigger always takes its payoff.</summary>
    public int NeedsPayoffBinds { get; internal set; }

    /// <summary>Rule 5: payoff refused because the trigger cannot carry a player choice.</summary>
    public int NeedsPayoffChoiceRejects { get; internal set; }

    /// <summary>Rule 6: a dependency prefix propagated its own link.</summary>
    public int PrefixPropagations { get; internal set; }

    /// <summary>Rule 7: a trigger/rule fragment was refused a link. Contract condition 5.</summary>
    public int TriggerSelfRejects { get; internal set; }

    /// <summary>Rule 8: the previous operation had no link to offer.</summary>
    public int NoLinkAvailable { get; internal set; }

    /// <summary>Rule 9: the trigger already carries two payoffs.</summary>
    public int TriggerSaturated { get; internal set; }

    /// <summary>Rule 10: fatal condition + double vulnerable.</summary>
    public int FatalDoubleVulnerableRejects { get; internal set; }

    /// <summary>Rule 11: retaliate without an attack-received trigger.</summary>
    public int RetaliateRejects { get; internal set; }

    /// <summary>Rule 12: player-choice payoff vs unsupported trigger context.</summary>
    public int PlayerChoiceContextRejects { get; internal set; }

    /// <summary>Rule 13: a difficult condition always binds.</summary>
    public int DifficultConditionBinds { get; internal set; }

    /// <summary>Rule 14: the coin flip came up bind.</summary>
    public int CoinFlipBinds { get; internal set; }

    /// <summary>Rule 14: the coin flip came up no-bind.</summary>
    public int CoinFlipRejects { get; internal set; }

    /// <summary>Rule 0: nothing to bind to yet.</summary>
    public int EmptyHistory { get; internal set; }

    /// <summary>Rule 1 / rule 3: the template never binds.</summary>
    public int TemplateNeverBinds { get; internal set; }

    /// <summary>Rule 2: a restricted effect refused a repeated trigger.</summary>
    public int RestrictedOnRepeatedRejects { get; internal set; }

    /// <summary>Rule 4: shuffle-then-draw inherited the previous link.</summary>
    public int ShuffleInherits { get; internal set; }

    public int TotalDecisions { get; internal set; }

    public int CoinFlipTrials => CoinFlipBinds + CoinFlipRejects;

    public double CoinFlipBindRate => CoinFlipTrials == 0 ? 0 : (double)CoinFlipBinds / CoinFlipTrials;

    public void Merge(BindingStats other)
    {
        NeedsPayoffBinds += other.NeedsPayoffBinds;
        NeedsPayoffChoiceRejects += other.NeedsPayoffChoiceRejects;
        PrefixPropagations += other.PrefixPropagations;
        TriggerSelfRejects += other.TriggerSelfRejects;
        NoLinkAvailable += other.NoLinkAvailable;
        TriggerSaturated += other.TriggerSaturated;
        FatalDoubleVulnerableRejects += other.FatalDoubleVulnerableRejects;
        RetaliateRejects += other.RetaliateRejects;
        PlayerChoiceContextRejects += other.PlayerChoiceContextRejects;
        DifficultConditionBinds += other.DifficultConditionBinds;
        CoinFlipBinds += other.CoinFlipBinds;
        CoinFlipRejects += other.CoinFlipRejects;
        EmptyHistory += other.EmptyHistory;
        TemplateNeverBinds += other.TemplateNeverBinds;
        RestrictedOnRepeatedRejects += other.RestrictedOnRepeatedRejects;
        ShuffleInherits += other.ShuffleInherits;
        TotalDecisions += other.TotalDecisions;
    }

    public override string ToString() =>
        $"decisions={TotalDecisions} coinFlip={CoinFlipBinds}/{CoinFlipTrials} ({CoinFlipBindRate:P1}) " +
        $"needsPayoff={NeedsPayoffBinds} difficult={DifficultConditionBinds} prefix={PrefixPropagations} " +
        $"triggerSelfReject={TriggerSelfRejects} noLink={NoLinkAvailable} saturated={TriggerSaturated}";
}

/// <summary>
/// The mechanism contract's core step: decide, at generation time, whether a
/// freshly drawn operation hangs off an earlier one, and if so which one.
///
/// The result is written into the operation's TriggerIndex, which is what the
/// runtime interpreter later uses to dispatch "when X happens, do this". The
/// decision is NEVER read from the source recipe's TriggerOwner - that field is
/// reference data from the original decomposition and is deliberately ignored,
/// which is precisely how a condition from one vanilla card ends up gating an
/// effect from another.
///
/// Decision order below is the original's LinkedTriggerIndex, in order. The
/// last three lines are the ones the acceptance criteria name explicitly:
///   * a trigger/rule fragment is never re-bound  (contract condition 5)
///   * a difficult condition (tier >= 2) always binds
///   * otherwise a plain coin flip decides, giving the ~50% binding rate
///     (contract condition 4)
/// </summary>
internal static class TriggerBinder
{
    /// <summary>
    /// Returns the index this operation should hang off, or -1 for standalone.
    /// </summary>
    public static int LinkedTriggerIndex(
        IReadOnlyList<GeneratedOperation> operations,
        GeneratedOperation candidate,
        DeterministicRandom random,
        BindingStats? stats = null)
    {
        if (stats != null)
        {
            stats.TotalDecisions++;
        }

        if (operations.Count == 0)
        {
            if (stats != null)
            {
                stats.EmptyHistory++;
            }
            return -1;
        }

        // 1. This template is a standalone cost-increase anchor.
        if (candidate.Template == "D:IncreaseThisCardCost")
        {
            if (stats != null)
            {
                stats.TemplateNeverBinds++;
            }
            return -1;
        }

        // 2. A restricted effect must not attach to a repeated trigger.
        if (GenerationRules.IsRestrictedEffect(candidate))
        {
            GeneratedOperation? linked = GenerationRules.LinkedTrigger(operations);
            if (linked != null && GenerationRules.IsRepeatedTriggerOrCondition(linked))
            {
                if (stats != null)
                {
                    stats.RestrictedOnRepeatedRejects++;
                }
                return -1;
            }
        }

        // 3. Standalone event prefixes never bind.
        if (GenerationRules.IsStandaloneEventDependencyPrefix(candidate))
        {
            if (stats != null)
            {
                stats.TemplateNeverBinds++;
            }
            return -1;
        }

        // 4. Shuffle-then-draw inherits the previous operation's link verbatim.
        if (operations[^1].Template == "D:ShuffleAllUnexhaustedIntoDraw")
        {
            if (stats != null)
            {
                stats.ShuffleInherits++;
            }
            return operations[^1].TriggerIndex;
        }

        // 5. If the previous operation is an unfulfilled trigger, this operation
        //    IS its payoff - unless the payoff needs a player choice the trigger
        //    cannot supply.
        if (GenerationRules.TriggerNeedsLinkedEffect(operations[^1]))
        {
            if (GenerationRules.RequiresPlayerChoice(candidate)
                && !GenerationRules.TriggerSupportsPlayerChoice(operations[^1]))
            {
                if (stats != null)
                {
                    stats.NeedsPayoffChoiceRejects++;
                }
                return -1;
            }
            if (stats != null)
            {
                stats.NeedsPayoffBinds++;
            }
            return operations.Count - 1;
        }

        // 6. A dependency prefix propagates its own link to the payoff.
        if (GenerationRules.IsDependencyPrefix(operations[^1]))
        {
            if (stats != null)
            {
                stats.PrefixPropagations++;
            }
            return operations[^1].TriggerIndex;
        }

        // 7. CONTRACT CONDITION 5: a trigger/rule fragment is never re-bound.
        //    (uint)(scope - 3) <= 2u covers AbilityTrigger, ConditionalTrigger
        //    and AbilityRule.
        if (candidate.IsTriggerScope)
        {
            if (stats != null)
            {
                stats.TriggerSelfRejects++;
            }
            return -1;
        }

        // 8. Nothing to hang off.
        int triggerIndex = operations[^1].TriggerIndex;
        if (triggerIndex < 0)
        {
            if (stats != null)
            {
                stats.NoLinkAvailable++;
            }
            return -1;
        }

        // 9. A trigger already carrying two payoffs cannot take a third.
        if (GenerationRules.CountOperationsBoundTo(operations, triggerIndex) >= 2)
        {
            if (stats != null)
            {
                stats.TriggerSaturated++;
            }
            return -1;
        }

        GeneratedOperation trigger = operations[triggerIndex];

        // 10. Fatal-condition gating of a double vulnerable is incoherent.
        if (GenerationRules.IsDoubleTargetVulnerable(candidate) && GenerationRules.IsFatalCondition(trigger))
        {
            if (stats != null)
            {
                stats.FatalDoubleVulnerableRejects++;
            }
            return -1;
        }

        // 11. Retaliate needs an attack-received trigger.
        if (candidate.Template == "N:RetaliateDamage"
            && trigger.Spec.Trigger?.Kind != "attack_received")
        {
            if (stats != null)
            {
                stats.RetaliateRejects++;
            }
            return -1;
        }

        // 12. Player choice must fit the trigger's context.
        if (GenerationRules.RequiresPlayerChoice(candidate)
            && !GenerationRules.TriggerSupportsPlayerChoice(trigger))
        {
            if (stats != null)
            {
                stats.PlayerChoiceContextRejects++;
            }
            return -1;
        }

        // 13. A difficult condition always gets its payoff.
        if (GenerationRules.DifficultConditionTier(trigger) >= 2)
        {
            if (stats != null)
            {
                stats.DifficultConditionBinds++;
            }
            return triggerIndex;
        }

        // 14. CONTRACT CONDITION 4: otherwise it is a coin flip. UpgradeThatCard
        //     is exempt and always binds.
        if (candidate.Template != "I:UpgradeThatCard" && !random.CoinFlip())
        {
            if (stats != null)
            {
                stats.CoinFlipRejects++;
            }
            return -1;
        }
        if (stats != null)
        {
            stats.CoinFlipBinds++;
        }
        return triggerIndex;
    }
}
