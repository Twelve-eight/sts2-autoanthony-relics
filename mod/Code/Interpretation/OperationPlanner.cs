using AutoAnthonyRelics.Data;
using AutoAnthonyRelics.Generation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace AutoAnthonyRelics.Interpretation;

/// <summary>
/// Stage C, slices 1+2: the resolution half of the PURE planning layer.
///
/// Nothing here touches Godot, an engine command, or async - see
/// InterpreterTypes.cs for why that is a hard requirement rather than a style
/// preference. The engine-facing half is EffectExecutor.
///
/// Slice 1 effects: deal_damage / gain_block / apply_power / draw_cards /
/// gain_energy / gain_stars / lose_hp / heal.
/// Slice 2 dispatch: trigger hosts and condition gates, i.e. what
/// <c>Parameters["triggerIndex"]</c> means at run time.
///
/// Faithfulness rules applied throughout:
///   * values come from the generator's ResolvedValue, never re-derived
///     (research/fidelity-ledger.md section 0: the balance tables are not in the
///     reused data, so re-deriving would mean inventing them);
///   * the two ValueProp derivations are transcribed from
///     ChaosOperationExecutor.cs:1463 (damage) and :1478 (block);
///   * variant dispatch order follows the original's, including the fact that
///     apply_power is tried through the self route FIRST
///     (ChaosOperationExecutor.cs:912) and only then through the common switch.
/// </summary>
internal static class OperationPlanner
{
    /// <summary>lose_hp with Target "self": the original's literal 14 (ChaosOperationExecutor.cs:1015).</summary>
    private const ValueProp SelfHpLossProps =
        ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move; // == 14

    /// <summary>lose_hp against another creature: the original's literal 6 (ChaosOperationExecutor.cs:1022).</summary>
    private const ValueProp OtherHpLossProps =
        ValueProp.Unblockable | ValueProp.Unpowered; // == 6

    /// <summary>
    /// The original's TriggerNeedsLinkedEffect-ish "carry me for the whole fight"
    /// set: ChaosOperationExecutor.cs:5155-5187 (RequiresCompositePower). Mirrored
    /// exactly, including the fact that only six AbilityRule templates qualify.
    /// </summary>
    private static readonly HashSet<string> CompositePowerRuleTemplates = new(StringComparer.Ordinal)
    {
        "A:rule",
        "A:ruleShivsRetain",
        "A:ruleFirstShivBonusDamage",
        "A:ruleWeakEnemiesTakeMoreAttackDamage",
        "A:ruleRetainHand",
        "CL:DieOnUnblockedAttack",
    };

    /// <summary>
    /// The original's IsLingeringTrigger set (ChaosOperationExecutor.cs:5083-5119):
    /// ConditionalTriggers whose effect must NOT run at their own slot but be
    /// replayed when the event arrives.
    /// </summary>
    private static readonly HashSet<string> LingeringTriggerTemplates = new(StringComparer.Ordinal)
    {
        "C:untilTurnEndCardDrawn",
        "C:whenThisCardExhausted",
        "C:grantNextAttacksThisTurn",
        "C:untilTurnEndAttackPlayed",
        "C:untilTurnEndCardPlayed",
        "R:AtTurnStartIfInExhaust",
        "C:NextTurnsStart",
        "D:NextTurnsStart",
        "C:untilTurnEnd",
        "C:after",
        "C:for",
        "C:grantNextAttack",
        "C:untilTurnEndAttackReceived",
        "C:VulnerableEnemyDamageReductionThisTurn",
        "C:AtTurnEndIfInExhaust",
        "C:NextTurnStart",
        "NCR:NextTurn",
        "R:NextTurn",
        "CL:AtNextTurnStart",
    };

    /// <summary>
    /// The original's for-each family (ChaosOperationExecutor.cs:161-172 and
    /// :127-151). These drive their linked effects once per iterated card, so the
    /// linked effects are still "not run at their own slot".
    /// </summary>
    private static readonly HashSet<string> ForEachHostTemplates = new(StringComparer.Ordinal)
    {
        "C:forEach",
        "C:forEachExhaustedCard",
        "C:forEachExhaustedNonAttack",
        "C:forEachDiscarded",
        "D:ForEachExhaustedStatus",
        "D:ForEachEnergySpentThisTurn",
    };

    /// <summary>
    /// The original's StructuredSelfPowerRoute variant set
    /// (ChaosOperationExecutor.cs:1279-1302). It only applies when
    /// spec.Target == "self".
    /// </summary>
    private static readonly HashSet<string> SelfPowerRouteVariants = new(StringComparer.Ordinal)
    {
        "dexterity_gain",
        "dexterity_loss",
        "dexterity_gain_this_turn",
        "doom",
        "focus_loss",
        "focus_loss_this_turn",
        "blur",
        "intangible",
        "thorns",
        "plating",
        "strength",
        "strength_this_turn",
        "vigor",
        "strength_loss",
        "strength_loss_this_turn",
        "strength_per_target_vulnerable",
    };

    /// <summary>
    /// The original's damage ValueProp derivation, transcribed from
    /// ChaosOperationExecutor.cs:1463-1476. The three numeric answers are
    /// 4 (Power), 8 (normal card damage), 12 (triggered damage from a card whose
    /// powered-attack path was disabled).
    /// </summary>
    public static ValueProp DamagePropsForCardEffect(
        CardType sourceType,
        bool isTriggered = false,
        bool usePoweredCardDamage = true)
    {
        if (sourceType == CardType.Power)
        {
            return ValueProp.Unpowered; // 4
        }
        if (isTriggered && !usePoweredCardDamage)
        {
            return ValueProp.Unpowered | ValueProp.Move; // 12
        }
        return ValueProp.Move; // 8
    }

    /// <summary>
    /// The original's block ValueProp derivation, transcribed from
    /// ChaosOperationExecutor.cs:1478-1487: 4 for a Power source or any triggered
    /// block, 8 otherwise.
    /// </summary>
    public static ValueProp BlockPropsForCardEffect(CardType sourceType, bool isTriggered = false)
    {
        if (sourceType == CardType.Power || isTriggered)
        {
            return ValueProp.Unpowered; // 4
        }
        return ValueProp.Move; // 8
    }

    /// <summary>
    /// ChaosOperationExecutor.cs:1456-1461: a triggered effect uses the powered
    /// attack path only when the source is in a combat pile and is not a Power.
    /// </summary>
    public static bool TriggeredDamageUsesPoweredAttack(
        bool sourceIsInCombatPile,
        CardType sourceType = CardType.Attack) =>
        sourceType != CardType.Power && sourceIsInCombatPile;

    /// <summary>Mirrors ChaosOperationExecutor.cs:5083-5119.</summary>
    public static bool IsLingeringTrigger(GeneratedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.Scope == FragmentScope.ConditionalTrigger
            && LingeringTriggerTemplates.Contains(operation.Template);
    }

    /// <summary>Mirrors the original's for-each host family.</summary>
    public static bool IsForEachHost(GeneratedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.Scope == FragmentScope.ConditionalTrigger
            && ForEachHostTemplates.Contains(operation.Template);
    }

    /// <summary>Mirrors ChaosOperationExecutor.cs:5155-5187.</summary>
    public static bool RequiresCarriedHost(GeneratedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Scope == FragmentScope.AbilityTrigger)
        {
            return true;
        }
        if (operation.Scope == FragmentScope.AbilityRule)
        {
            return CompositePowerRuleTemplates.Contains(operation.Template);
        }
        return operation.Scope == FragmentScope.ConditionalTrigger && LingeringTriggerTemplates.Contains(operation.Template);
    }

    /// <summary>
    /// Plan a whole card. The card-level pass is where "does this card need a
    /// carried trigger host at all" lives, mirroring
    /// ChaosOperationExecutor.cs:199 <c>operations.Any(RequiresCompositePower)</c>.
    /// </summary>
    public static CardPlan PlanCard(GeneratedCard card, InterpreterContext context)
    {
        ArgumentNullException.ThrowIfNull(card);
        var operations = new List<OperationPlan>(card.Operations.Count);
        for (int i = 0; i < card.Operations.Count; i++)
        {
            operations.Add(Plan(card, i, context));
        }
        bool armsCarriedHosts = card.Operations.Any(RequiresCarriedHost);
        return new CardPlan(card.Character, card.ShellId, card.Type, card.Rarity, operations, armsCarriedHosts);
    }

    /// <summary>
    /// Resolve one operation. Total over the pool: every pair VariantTable calls
    /// Implemented gets a fully resolved plan; Delegated and Pending get a plan
    /// that says so explicitly rather than an empty default.
    /// </summary>
    public static OperationPlan Plan(GeneratedCard card, int operationIndex, InterpreterContext context)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(context);
        GeneratedOperation operation = card.Operations[operationIndex];
        RuntimeSpec spec = operation.Spec;

        VariantClassification classification = VariantTable.Classify(spec.Opcode, spec.Variant);
        (PlanDisposition disposition, bool isForEach) = ResolveDisposition(card, operationIndex, operation);
        PlannedTargetSelector target = ResolveTarget(spec, context, out bool targetUnavailable);

        var plan = new OperationPlan
        {
            OperationIndex = operationIndex,
            Opcode = spec.Opcode,
            Variant = spec.Variant,
            DeclaredTarget = spec.Target,
            Disposition = disposition,
            HostIndex = operation.TriggerIndex,
            Target = target,
            TargetUnavailable = targetUnavailable,
            SourceZone = spec.ParsedSourceZone,
            DestinationZone = spec.ParsedDestinationZone,
            Values = operation.Values,
            Amount = PrimaryAmount(operation.Values),
            Hits = Math.Max(0, operation.ValueOf("hits", 1)),
            ConditionKind = spec.Condition?.Kind,
            TriggerKind = spec.Trigger?.Kind,
            TriggerLifetime = spec.Trigger?.Lifetime,
            Threshold = operation.ValueOf("threshold", 0),
            IteratesLinkedEffects = isForEach,
        };

        switch (classification)
        {
            case VariantClassification.DelegatedToNative:
                return plan with
                {
                    Outcome = PlanOutcome.DelegatedToNative,
                    Action = PlannedActionKind.None,
                    DelegatedTemplate = operation.Template,
                };
            case VariantClassification.Pending:
                return plan with
                {
                    Outcome = PlanOutcome.Unsupported,
                    Action = PlannedActionKind.None,
                    PendingReason = VariantTable.PendingNote(spec.Opcode, spec.Variant),
                };
            default:
                return ResolveImplemented(plan, operation, spec, context);
        }
    }

    /// <summary>
    /// The runtime meaning of <c>triggerIndex</c>. This mirrors the observable
    /// control flow of the original's play loop (ChaosOperationExecutor.cs:109-179)
    /// rather than paraphrasing it:
    ///
    ///   * scope Modifier and AbilityTrigger are skipped outright (:117-121);
    ///   * a ConditionalTrigger host evaluates its own condition once (:152) and
    ///     is not executed;
    ///   * an operation with a triggerIndex is SKIPPED - i.e. deferred to its
    ///     host - when the host is a for-each, an AbilityTrigger, or a lingering
    ///     trigger, or when the host's condition is false (:173);
    ///   * otherwise it runs at its own slot, gated by the host's condition.
    /// </summary>
    private static (PlanDisposition Disposition, bool IsForEach) ResolveDisposition(
        GeneratedCard card,
        int operationIndex,
        GeneratedOperation operation)
    {
        // Scope Modifier is skipped before anything else: ChaosOperationExecutor
        // .cs:117-121 skips scope 2 and 3 outright, and :360 skips Modifier-scope
        // linked operations again inside ExecuteTriggered. A Modifier is never
        // executed, so it can never be a LinkedEffect either - even when a later
        // operation hangs off it.
        if (operation.Scope == FragmentScope.Modifier)
        {
            return (PlanDisposition.Modifier, false);
        }

        if (operation.TriggerIndex >= 0 && operation.TriggerIndex < card.Operations.Count)
        {
            GeneratedOperation host = card.Operations[operation.TriggerIndex];
            if (RequiresCarriedHost(host) || IsForEachHost(host))
            {
                return (PlanDisposition.LinkedEffect, IsForEachHost(host));
            }
            // A plain ConditionalTrigger host: the effect stays on the play
            // sequence and is gated inline (original :173-177).
            return (PlanDisposition.OnPlay, false);
        }

        if (RequiresCarriedHost(operation))
        {
            return (PlanDisposition.CarriedHost, IsForEachHost(operation));
        }
        if (IsForEachHost(operation))
        {
            return (PlanDisposition.InlineGate, true);
        }
        if (operation.Scope == FragmentScope.ConditionalTrigger)
        {
            return (PlanDisposition.InlineGate, false);
        }
        if (operation.Spec.ParsedOpcode == SpecOpcode.Condition)
        {
            return (PlanDisposition.InlineGate, false);
        }
        return (PlanDisposition.OnPlay, false);
    }

    /// <summary>
    /// Which creature(s) the effect points at. The original's creature-targeted
    /// branches all read <c>state.Target ?? cardPlay.Target</c>, where a triggered
    /// context has already put the event creature into state.Target
    /// (ChaosOperationExecutor.cs:339). So a "selected_enemy" spec resolves to the
    /// event creature when one is supplied - which is exactly what makes a
    /// re-bound condition able to point a single-target effect at the right enemy.
    /// </summary>
    private static PlannedTargetSelector ResolveTarget(
        RuntimeSpec spec,
        InterpreterContext context,
        out bool unavailable)
    {
        unavailable = false;
        switch (spec.ParsedTarget)
        {
            case SpecTarget.Self:
                return PlannedTargetSelector.Self;
            case SpecTarget.AllEnemies:
                return PlannedTargetSelector.AllEnemies;
            case SpecTarget.RandomEnemy:
                return PlannedTargetSelector.RandomEnemy;
            case SpecTarget.SelectedEnemy:
                return ResolveCreatureTarget(context, out unavailable);
            default:
                // Card-targeted / event-targeted / unrecognised: slice 3+. The
                // plan says "other" rather than guessing a creature.
                return PlannedTargetSelector.Other;
        }
    }

    private static PlannedTargetSelector ResolveCreatureTarget(InterpreterContext context, out bool unavailable)
    {
        switch (context.Target)
        {
            case TargetAvailability.EventEnemy:
                unavailable = false;
                return PlannedTargetSelector.EventEnemy;
            case TargetAvailability.SelectedEnemy:
                unavailable = false;
                return PlannedTargetSelector.SelectedEnemy;
            default:
                // Original: `if (target == null) return true;`
                unavailable = true;
                return PlannedTargetSelector.SelectedEnemy;
        }
    }

    private static OperationPlan ResolveImplemented(
        OperationPlan plan,
        GeneratedOperation operation,
        RuntimeSpec spec,
        InterpreterContext context)
    {
        switch (spec.ParsedOpcode)
        {
            case SpecOpcode.DealDamage:
                return ResolveDamage(plan, operation, context);
            case SpecOpcode.GainBlock:
                return plan with
                {
                    Outcome = PlanOutcome.Resolved,
                    Action = PlannedActionKind.GainBlock,
                    Amount = operation.ValueOf("block", plan.Amount),
                    Props = BlockPropsForCardEffect(context.CardType, context.IsTriggered),
                };
            case SpecOpcode.DrawCards:
                return plan with
                {
                    Outcome = PlanOutcome.Resolved,
                    Action = PlannedActionKind.DrawCards,
                    Amount = operation.ValueOf("draw", plan.Amount),
                };
            case SpecOpcode.GainEnergy:
                return plan with
                {
                    Outcome = PlanOutcome.Resolved,
                    Action = PlannedActionKind.GainEnergy,
                    Amount = operation.ValueOf("energy", plan.Amount),
                };
            case SpecOpcode.GainStars:
                return plan with
                {
                    Outcome = PlanOutcome.Resolved,
                    Action = PlannedActionKind.GainStars,
                    Amount = operation.ValueOf("stars", plan.Amount),
                };
            case SpecOpcode.LoseHp:
                return ResolveLoseHp(plan, operation, spec);
            case SpecOpcode.Heal:
                return plan with
                {
                    Outcome = PlanOutcome.Resolved,
                    Action = PlannedActionKind.Heal,
                    Amount = operation.ValueOf("amount", plan.Amount),
                };
            case SpecOpcode.ApplyPower:
                return ResolveApplyPower(plan, operation, spec);
            case SpecOpcode.Trigger:
                return plan with
                {
                    Outcome = PlanOutcome.Resolved,
                    Action = PlannedActionKind.TriggerEvent,
                };
            case SpecOpcode.Condition:
                return plan with
                {
                    Outcome = PlanOutcome.Resolved,
                    Action = PlannedActionKind.ConditionGate,
                };
            default:
                // VariantTable says Implemented but there is no handler. That is
                // a bug in this file, not in the data, so it must not degrade
                // quietly into Unsupported.
                throw new InvalidOperationException(
                    $"VariantTable classifies '{spec.Opcode}|{spec.Variant}' as Implemented " +
                    "but OperationPlanner has no handler for that opcode.");
        }
    }

    private static OperationPlan ResolveDamage(
        OperationPlan plan,
        GeneratedOperation operation,
        InterpreterContext context)
    {
        // ChaosOperationExecutor.cs:1361: the direct-damage flag.
        bool direct = context.CardType == CardType.Power
            || (context.IsTriggered && !context.UsePoweredCardDamage);

        return plan with
        {
            Outcome = PlanOutcome.Resolved,
            Action = PlannedActionKind.DealDamage,
            Amount = operation.ValueOf("damage", plan.Amount),
            Hits = Math.Max(0, operation.ValueOf("hits", 1)),
            Props = DamagePropsForCardEffect(context.CardType, context.IsTriggered, context.UsePoweredCardDamage),
            DirectDamage = direct,
        };
    }

    private static OperationPlan ResolveLoseHp(OperationPlan plan, GeneratedOperation operation, RuntimeSpec spec)
    {
        bool self = spec.ParsedTarget == SpecTarget.Self;
        int amount = operation.ValueOf("hp_loss", operation.ValueOf("amount", plan.Amount));
        return plan with
        {
            Outcome = PlanOutcome.Resolved,
            Action = PlannedActionKind.LoseHp,
            Amount = amount,
            Hits = 1,
            // ChaosOperationExecutor.cs:1013-1024.
            Props = self ? SelfHpLossProps : OtherHpLossProps,
            Target = self ? PlannedTargetSelector.Self : plan.Target,
        };
    }

    private static OperationPlan ResolveApplyPower(OperationPlan plan, GeneratedOperation operation, RuntimeSpec spec)
    {
        int amount = operation.ValueOf("amount", plan.Amount);

        // Route 1 - the self route is tried FIRST (ChaosOperationExecutor.cs:912).
        if (spec.ParsedTarget == SpecTarget.Self && SelfPowerRouteVariants.Contains(spec.Variant))
        {
            return plan with
            {
                Outcome = PlanOutcome.Resolved,
                Action = PlannedActionKind.ApplySelfPower,
                Amount = amount,
                PowerKind = MapSelfPowerVariant(spec.Variant),
            };
        }

        // Route 2 - the common switch.
        PlannedPowerKind kind = spec.Variant switch
        {
            "weak" => PlannedPowerKind.Weak,
            "vulnerable" => PlannedPowerKind.Vulnerable,
            "vulnerable_double" => PlannedPowerKind.VulnerableDouble,
            "strength_gain" => PlannedPowerKind.StrengthGain,
            "strength_loss" => PlannedPowerKind.StrengthLoss,
            "strength_loss_this_turn" => PlannedPowerKind.StrengthLossThisTurn,
            "retain_hand_this_turn" => PlannedPowerKind.RetainHandThisTurn,
            _ => throw new InvalidOperationException(
                $"VariantTable classifies 'apply_power|{spec.Variant}' as Implemented but no " +
                "structured apply_power route handles it."),
        };

        return plan with
        {
            Outcome = PlanOutcome.Resolved,
            Action = PlannedActionKind.ApplyPower,
            Amount = amount,
            PowerKind = kind,
        };
    }

    /// <summary>
    /// Maps a StructuredSelfPowerRoute variant onto a power kind. The engine types
    /// live in EffectExecutor; this mapping is the pure half of that decision.
    /// </summary>
    private static PlannedPowerKind MapSelfPowerVariant(string variant) => variant switch
    {
        "strength" => PlannedPowerKind.Strength,
        "strength_this_turn" => PlannedPowerKind.StrengthThisTurn,
        "strength_loss" => PlannedPowerKind.StrengthLoss,
        "strength_loss_this_turn" => PlannedPowerKind.StrengthLossThisTurn,
        "strength_per_target_vulnerable" => PlannedPowerKind.StrengthPerTargetVulnerable,
        "dexterity_gain" => PlannedPowerKind.DexterityGain,
        "dexterity_loss" => PlannedPowerKind.DexterityLoss,
        "doom" => PlannedPowerKind.Doom,
        "focus_loss" => PlannedPowerKind.FocusLoss,
        "plating" => PlannedPowerKind.Plating,
        "vigor" => PlannedPowerKind.Vigor,
        _ => throw new InvalidOperationException(
            $"self power route variant '{variant}' has no PlannedPowerKind mapping."),
    };

    /// <summary>
    /// The original's OperationAmount (ChaosCardModel.cs:1552-1574): the first
    /// UPGRADABLE numeric slot, else 0. It is the fallback the original threads
    /// through RuntimeSpecValue when a named slot is absent, which is why the
    /// per-action resolvers above use <c>ValueOf(name, plan.Amount)</c>.
    /// </summary>
    private static int PrimaryAmount(IReadOnlyList<ResolvedValue> values)
    {
        foreach (ResolvedValue value in values)
        {
            if (value.Upgradable)
            {
                return value.Value;
            }
        }
        return 0;
    }
}
