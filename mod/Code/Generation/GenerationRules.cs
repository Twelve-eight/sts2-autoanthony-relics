using AutoAnthonyRelics.Data;

namespace AutoAnthonyRelics.Generation;

/// <summary>
/// The coherence and mechanism predicates the generator branches on.
///
/// PROVENANCE AND FIDELITY
///   Every template set and every decision order below was read out of the
///   original's CardEffectRules / ComponentAssemblyGenerator / EffectSelectionTuning
///   and re-expressed against the data fields WE actually carry. No code was
///   copied. The predicates that could not be reproduced because their inputs
///   are not in the reused data files are listed in research/fidelity-ledger.md
///   and marked `APPROXIMATED` or `OMITTED` in the comments here.
///
///   The important asymmetry: the MECHANISM predicates (trigger needs payoff,
///   dependency prefix needs payoff, trigger scopes are never re-bound, the
///   ~50% binding coin) are reproduced exactly, because contract conditions 1-7
///   depend on them. The TASTE predicates (weights, balance, valuation) are not,
///   because the user chose the uniform / carry-over-value policy.
/// </summary>
internal static class GenerationRules
{
    // ---------------------------------------------------------------------
    // Template sets (verbatim from the original)
    // ---------------------------------------------------------------------

    private static readonly Dictionary<string, string[]> DependencyPayoffs = new(StringComparer.Ordinal)
    {
        ["D:ForEachOrb"] = ["D:RepeatPerOrb"],
        ["D:ForEachEnergySpentThisTurn"] = ["D:RepeatPerEnergySpentThisTurn"],
        ["D:WheneverStatusGenerated"] = ["D:CostDownWhenStatusGenerated"],
        ["NCR:ForEachEtherealPlayedCombat"] = ["NCR:CostDownPerVoidPlayed", "NCR:RepeatPerVoidPlayedCombat"],
        ["NCR:ForEachCardDrawnThisTurn"] = ["NCR:DamagePerCardDrawnThisTurn"],
        ["NCR:ForEachDoomThreshold"] = ["NCR:DoomPerDoomThreshold"],
        ["NCR:ForEachOstyAttackThisTurn"] = ["NCR:RepeatPerOstyAttackThisTurn"],
        ["NCR:ForEachExhaustedSoul"] = ["NCR:DamagePerExhaustedSoul"],
        ["NCR:ForEachOstyAttackCard"] = ["NCR:DamagePerOstyAttackCard"],
        ["NCR:WheneverCreatureDies"] = ["NCR:CostDownWhenCreatureDies"],
        ["NCR:WheneverHighCostCardPlayed"] = ["NCR:ReturnFromDiscardOnHighCostPlay"],
        ["NCR:IfOstyAttackedThisTurn"] = ["NCR:SetCostZeroIfOstyAttacked"],
        ["NCR:WheneverCardPlayedThisTurn"] = ["NCR:ApplyPower_OblivionPower"],
        ["NCR:WheneverOstyAttacksTargetThisTurn"] = ["NCR:ApplyPower_SicEmPower"],
        ["R:ForEachPriorAttackHitOnTarget"] = ["R:ForgePerPriorHit"],
        ["R:ForEachStarCostCard"] = ["R:BonusPerStarCostCardInHand"],
        ["R:ForEachSkillPlayedThisTurn"] = ["R:RepeatPerSkillPlayedThisTurn"],
        ["R:ForEachStarGainedThisTurn"] = ["R:RepeatPerStarGainedThisTurn"],
        ["R:ForEachGeneratedCardCombat"] = ["R:BonusPerGeneratedCardThisCombat"],
        ["R:WheneverDrawn"] = ["R:CostDownWhenDrawn", "R:DamageUpWhenDrawn"],
        ["R:IfEnergyXAtLeast"] = ["R:DoubleEnergyX"],
        ["R:AtTurnEndWhenTopOfDraw"] = ["R:PlayAtTurnEndIfTopOfDraw"],
        ["CL:ForEachCardPlayedCombat"] = ["T:D"],
        ["CL:ForEachDrawPileCard"] = ["T:D"],
    };

    private static readonly HashSet<string> GenericDependencyPrefixes = new(StringComparer.Ordinal)
    {
        "D:ForEachOrb", "D:ForEachEnemy", "D:ForEachUniqueOrb", "D:IfHasFrost",
        "NCR:ForEachEtherealPlayedCombat", "NCR:ForEachCardDrawnThisTurn", "NCR:ForEachDoomThreshold",
        "NCR:ForEachOstyAttackThisTurn", "NCR:ForEachExhaustedSoul", "NCR:ForEachOstyAttackCard",
        "R:ForEachPriorAttackHitOnTarget", "R:ForEachStarCostCard", "R:ForEachSkillPlayedThisTurn",
        "R:ForEachStarGainedThisTurn", "R:ForEachGeneratedCardCombat", "R:IfEnergyXAtLeast",
        "R:IfCardsPlayedAtLeastThisTurn", "CL:ForEachCardPlayedCombat", "CL:ForEachDrawPileCard",
    };

    /// <summary>Generic prefixes minus the three that are not multiplicative.</summary>
    private static readonly HashSet<string> MultiplicativeDependencyPrefixes = new(
        GenericDependencyPrefixes.Where(t => t is not ("D:IfHasFrost" or "R:IfEnergyXAtLeast" or "R:IfCardsPlayedAtLeastThisTurn")),
        StringComparer.Ordinal);

    private static readonly HashSet<string> DependencyOnlyPayoffs = new(StringComparer.Ordinal)
    {
        "D:RepeatPerOrb", "D:CostDownWhenStatusGenerated", "NCR:CostDownPerVoidPlayed",
        "NCR:RepeatPerVoidPlayedCombat", "NCR:DamagePerCardDrawnThisTurn", "NCR:DoomPerDoomThreshold",
        "NCR:RepeatPerOstyAttackThisTurn", "NCR:DamagePerExhaustedSoul", "NCR:DamagePerOstyAttackCard",
        "NCR:CostDownWhenCreatureDies", "NCR:ReturnFromDiscardOnHighCostPlay", "NCR:SetCostZeroIfOstyAttacked",
        "NCR:ApplyPower_OblivionPower", "NCR:ApplyPower_SicEmPower", "R:ForgePerPriorHit",
        "R:BonusPerStarCostCardInHand", "R:RepeatPerSkillPlayedThisTurn", "R:RepeatPerStarGainedThisTurn",
        "R:BonusPerGeneratedCardThisCombat", "R:CostDownWhenDrawn", "R:DamageUpWhenDrawn",
        "R:DoubleEnergyX", "R:PlayAtTurnEndIfTopOfDraw",
    };

    private static readonly HashSet<string> StandaloneEventDependencyPrefixes = new(StringComparer.Ordinal)
    {
        "D:WheneverStatusGenerated", "NCR:WheneverCreatureDies", "NCR:WheneverHighCostCardPlayed",
        "NCR:WheneverCardPlayedThisTurn", "NCR:WheneverOstyAttacksTargetThisTurn",
        "R:WheneverDrawn", "R:AtTurnEndWhenTopOfDraw",
    };

    private static readonly HashSet<string> RestrictedEffectTemplates = new(StringComparer.Ordinal)
    {
        "N:Heal", "N_HEAL", "CL:GainGold", "I:GainMaxHp", "CL:ProxyAtomic_Alchemize",
        "A:ProxyAtomic_Royalties", "I:AddCardReward", "D:IncreaseThisCardBlockRun",
        "NCR:IncreaseThisCardDamageRun",
    };

    private static readonly HashSet<string> SelfCostReductionTemplates = new(StringComparer.Ordinal)
    {
        "D:CostDownWhenStatusGenerated", "NCR:SetCostZeroIfOstyAttacked", "I:ReduceThisCardCostCombat",
        "D:SetThisCardCostZero", "NCR:CostDownPerVoidPlayed", "NCR:CostDownWhenCreatureDies",
        "R:CostDownWhenDrawn",
    };

    private static readonly HashSet<string> ExtremeLifecycleDownsideTemplates = new(StringComparer.Ordinal)
    {
        "CL:DieOnUnblockedAttack", "CL:NoBlockFromCards", "R:FillHandWithDebris",
    };

    private static readonly HashSet<string> SelfManagedStateEffectTemplates = new(StringComparer.Ordinal)
    {
        "CL:ReturnThisToHand", "R:ReturnThisToHand", "R:PutThisOnDraw",
        "R:ReturnAfterSkillsPlayed", "C:whileInCombat", "C:whileInCombatSkillCostReduction",
    };

    private static readonly HashSet<string> RepeatableDependencyModifierTemplates = new(StringComparer.Ordinal)
    {
        "M:DamagePerCardDrawnCombat", "M:DamagePerDiscardThisTurn", "NCR:DamagePerExhaustedSoul",
        "M:DamagePerExhaustCard", "NCR:DamagePerCardDrawnThisTurn", "NCR:DamagePerOstyAttackCard",
        "R:BonusPerStarCostCardInHand", "R:BonusPerGeneratedCardThisCombat", "CL:BonusPerUniqueDebuff",
    };

    private static readonly HashSet<string> NonRepeatableDependencyModifierTemplates = new(StringComparer.Ordinal)
    {
        "M:RepeatAreaOnKill", "CL:IncreaseRollingDamage", "R:DoubleEnergyX",
        "R:DoubleEitherXAtThreshold", "M:TriggeredAttackDamagePercent",
    };

    private static readonly HashSet<string> AtomicChoiceProxyTemplates = new(StringComparer.Ordinal)
    {
        "I:ProxyAtomic_Begone", "I:ProxyAtomic_Charge", "I:ProxyAtomic_Guards",
        "I:ProxyAtomic_Seance", "I:ProxyAtomic_Dredge", "I:ProxyAtomic_Transfigure",
    };

    /// <summary>The original's 24-template RequiresPlayerChoice list.</summary>
    private static readonly HashSet<string> PlayerChoiceTemplates = new(StringComparer.Ordinal)
    {
        "CL:TransformSelectedHandCards", "I:GrantSlyToHandSkillThisTurn", "I:CopySelectedCardNextTurn",
        "R:MoveDiscardCardToDrawTop", "I:PlayTopCardAndExhaust", "N:MoveDiscardCardToHand",
        "CL:ExhaustUpToHandCards", "CL:ChooseDrawCardToHand", "D:MoveDiscardCardToHand",
        "CL:MoveSelectedSkillDrawToHand", "D:AutoPlayRandomAttackFromDraw", "I:AutoPlayRandomAttackFromHand",
        "CL:ChooseFromRandomDrawCards", "R:PutSelectedHandCardsOnDraw", "R:CopySelectedColorlessCard",
        "R:PutSelectedHandCardOnDraw", "NCR:ExhaustSelectedDrawCard", "N:Discard",
        "CL:MoveSelectedAttackDrawToHand", "R:PlaySelectedSkillMultipleTimes", "NCR:MoveDiscardCardToHand",
        "I:PlayTopXCards", "CL:PlayTopDrawCard", "I:PlayAtRandomEnemy",
    };

    /// <summary>Triggers whose choice context cannot carry a player-choice payoff.</summary>
    private static readonly HashSet<string> ChoiceUnsupportedTriggerTemplates = new(StringComparer.Ordinal)
    {
        "C:NextTurnsStart", "D:NextTurnsStart", "A:whenEnergySpent", "A:whenOstyLosesHp",
        "C:NextTurnStart", "NCR:NextTurn", "R:NextTurn", "A:whenEnergyCostAtLeast",
        "A:whenOneStarSpent", "D:ForEachEnergySpentThisTurn",
    };

    private static readonly HashSet<string> ChoiceUnsupportedTriggerKinds = new(StringComparer.Ordinal)
    {
        "block_gained", "owner_hp_lost_during_turn", "card_generated",
        "stars_spent_or_gained", "status_generated",
    };

    private static readonly HashSet<string> TriggerSuppliesEnemyTargetTemplates = new(StringComparer.Ordinal)
    {
        "A:whenLightningEvoked", "A:whenAttackDealsDamage", "A:whenAttackDealsUnblockedDamage",
        "A:whenAttackDamagesEnemy", "A:whenDebuffApplied", "A:whenDoomApplied",
        "NCR:FirstAttackPlayedEachTurn",
    };

    private static readonly HashSet<string> SuppliesEventAttackTriggerKinds = new(StringComparer.Ordinal)
    {
        "attack_played", "first_attack_played_each_turn", "first_zero_cost_attack_played_each_turn",
        "nth_attack_played_this_turn", "next_attack", "next_attacks_this_turn",
    };

    private static readonly HashSet<string> SuppliesReferencedCardTriggerKinds = new(StringComparer.Ordinal)
    {
        "card_exhausted", "card_generated", "self_exhausted", "derivative_played", "strike_card_drawn",
        "card_drawn_during_turn", "energy_spent_threshold", "next_attacks_this_turn",
        "first_status_drawn_each_turn", "turn_end_if_self_on_draw_top", "card_played", "next_attack",
        "first_card_played_each_turn", "nth_attack_played_this_turn", "turn_end_if_self_in_exhaust",
        "first_attack_played_each_turn", "for_each_discarded_card", "for_each_exhausted_card",
        "card_drawn", "ethereal_card_drawn", "attack_played", "first_zero_cost_attack_played_each_turn",
        "ethereal_card_played", "first_attack_or_skill_each_turn", "energy_cost_at_least_card_played",
        "status_generated", "for_each_exhausted_status",
    };

    private static readonly HashSet<string> UniqueRuleVariants = new(StringComparer.Ordinal)
    {
        "die_on_unblocked_attack", "retain_hand_at_turn_end", "retain_block_between_turns",
        "skills_cost_zero", "derivative_hits_all", "derivative_retain",
        "played_skills_gain_sly", "kings_sword_hits_all",
    };

    private static readonly Dictionary<string, string> TemplateUniqueKeys = new(StringComparer.Ordinal)
    {
        ["CL:ReturnThisToHand"] = "self:destination",
        ["R:ReturnThisToHand"] = "self:destination",
        ["R:ReturnAfterSkillsPlayed"] = "self:destination",
        ["NCR:ReturnFromDiscardOnHighCostPlay"] = "self:destination",
        ["R:PutThisOnDraw"] = "self:destination",
        ["I:PreventDrawThisTurn"] = "turn:no_draw",
        ["I:FreeHandThisTurn"] = "turn:free_hand",
        ["CL:RetainHandThisTurn"] = "turn:retain_hand",
        ["N:RetainHandThisTurn"] = "turn:retain_hand",
        ["R:RetainHandThisTurn"] = "turn:retain_hand",
        ["N:DiscardAll"] = "clear:all_hand_discard",
        ["D:ExhaustAllStatuses"] = "clear:all_statuses_exhaust",
        ["R:FillHandWithDebris"] = "fill:hand",
        ["R:EndTurn"] = "turn:end",
        ["R:DoubleEitherXAtThreshold"] = "x:double_at_threshold",
        ["I:Transform"] = "transform:all_hand_attacks",
        ["D:TransformStatusesToFuel"] = "transform:all_hand_statuses",
        ["T:RemoveBlockAndArtifact"] = "target:remove_all_block_and_artifact",
        ["CL:DieOnUnblockedAttack"] = "rule:die_on_unblocked_attack",
        ["CL:NoBlockFromCards"] = "rule:no_block_from_cards",
        ["R:KingsSwordHitsAllEnemies"] = "rule:kings_sword_hits_all",
    };

    /// <summary>
    /// APPROXIMATED. The original asks ComponentValuationApi / IsIntrinsicNegative /
    /// DerivativeSlotCatalog, none of which are in the reused data. We fall back to
    /// the two signals we do have - the atom's opcode/variant and the sign of its
    /// numeric slots - which is conservative: it can under-report a negative
    /// effect but never over-reports one. Only one rule consumes this
    /// (self-exhaust trigger + negative payoff), so the blast radius is small.
    /// </summary>
    private static readonly HashSet<SpecOpcode> NegativeOpcodes = new()
    {
        SpecOpcode.LoseHp, SpecOpcode.DiscardCard, SpecOpcode.EndTurn, SpecOpcode.RestrictBlockFromCards,
    };

    // ---------------------------------------------------------------------
    // Mechanism predicates (exact)
    // ---------------------------------------------------------------------

    public static bool TriggerNeedsLinkedEffect(GeneratedOperation operation)
    {
        // The original: (uint)(scope - 3) <= 1u, minus the two whileInCombat
        // conditions, minus vulnerable_enemy_damage_reduction.
        if (!operation.IsTriggerOrCondition)
        {
            return false;
        }
        if (operation.Template is "C:whileInCombat" or "C:whileInCombatSkillCostReduction")
        {
            return false;
        }
        return operation.Spec.Trigger?.Kind != "vulnerable_enemy_damage_reduction";
    }

    /// <summary>
    /// The original's EffectBalanceModel.LinkedTrigger: the operation the last
    /// one is currently attached to, resolving one hop. Note the scope test is
    /// 3..4 only - AbilityRule is covered by the dependency-prefix branch.
    /// </summary>
    public static GeneratedOperation? LinkedTrigger(IReadOnlyList<GeneratedOperation> operations)
    {
        if (operations.Count == 0)
        {
            return null;
        }
        GeneratedOperation last = operations[^1];
        if (last.IsTriggerOrCondition || IsDependencyPrefix(last))
        {
            return last;
        }
        int index = last.TriggerIndex;
        return index >= 0 && index < operations.Count ? operations[index] : null;
    }

    public static bool IsDependencyPrefix(GeneratedOperation operation)
    {
        string template = operation.Template;
        if (!DependencyPayoffs.ContainsKey(template) && !GenericDependencyPrefixes.Contains(template))
        {
            return false;
        }
        return template != "D:ForEachEnergySpentThisTurn" || operation.Scope == FragmentScope.Modifier;
    }

    public static bool IsMultiplicativeDependencyPrefix(GeneratedOperation operation) =>
        MultiplicativeDependencyPrefixes.Contains(operation.Template);

    public static bool IsStandaloneEventDependencyPrefix(GeneratedOperation operation) =>
        StandaloneEventDependencyPrefixes.Contains(operation.Template);

    public static bool RequiresDependencyPrefix(GeneratedOperation operation) =>
        DependencyOnlyPayoffs.Contains(operation.Template) || IsConditionalDamageVariant(operation);

    public static bool IsConditionalDamageVariant(GeneratedOperation operation) =>
        HasFlag(operation, "conditional_damage_payoff");

    public static bool IsLegalDependencyPayoff(GeneratedOperation prefix, GeneratedOperation payoff)
    {
        if (DependencyPayoffs.TryGetValue(prefix.Template, out string[]? legal))
        {
            foreach (string candidate in legal)
            {
                if (string.Equals(candidate, payoff.Template, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
        return GenericDependencyPrefixes.Contains(prefix.Template) && IsGenericDependencyPayoff(prefix, payoff);
    }

    private static bool IsGenericDependencyPayoff(GeneratedOperation prefix, GeneratedOperation payoff)
    {
        if (payoff.Scope is not (FragmentScope.SingleEnemyOnly or FragmentScope.NonTargeted
            or FragmentScope.Modifier or FragmentScope.Independent))
        {
            return false;
        }
        if (DependencyOnlyPayoffs.Contains(payoff.Template)
            || IsConditionalDamageVariant(payoff)
            || IsSelfManagedStateEffect(payoff))
        {
            return false;
        }
        if (payoff.Scope == FragmentScope.Modifier && !IsRepeatableDependencyModifier(payoff))
        {
            return false;
        }
        if (!IsMultiplicativeDependencyPrefix(prefix))
        {
            return true;
        }
        if (UsesX(payoff))
        {
            return true;
        }
        foreach (NumericSlot slot in payoff.Spec.Values)
        {
            if (slot.Source is "fixed" or "energy_x" or "star_x" or "special_x")
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsRepeatableDependencyModifier(GeneratedOperation operation)
    {
        if (NonRepeatableDependencyModifierTemplates.Contains(operation.Template))
        {
            return false;
        }
        RuntimeSpec spec = operation.Spec;
        if (spec.ParsedOpcode == SpecOpcode.ModifyHits)
        {
            return true;
        }
        if (spec.ParsedOpcode == SpecOpcode.ModifyBlock)
        {
            return spec.Variant == "strength_scaled";
        }
        if (spec.ParsedOpcode == SpecOpcode.ModifyDamage)
        {
            return spec.Variant is not ("current_block" or "triggered_attack_percentage");
        }
        return RepeatableDependencyModifierTemplates.Contains(operation.Template);
    }

    public static bool IsSelfManagedStateEffect(GeneratedOperation operation) =>
        SelfManagedStateEffectTemplates.Contains(operation.Template);

    public static bool IsRestrictedEffect(GeneratedOperation operation)
    {
        // APPROXIMATED: the original also ORs in the compiled "api_restricted"
        // flag and IsHealingOrMaxHp. That flag is absent from every atom in the
        // reused pool (verified: 0 occurrences), so the template list - which
        // does cover the pool's Heal / GainMaxHp atoms - carries the rule.
        if (HasFlag(operation, "api_restricted"))
        {
            return true;
        }
        if (RestrictedEffectTemplates.Contains(operation.Template))
        {
            return true;
        }
        // IsHealingOrMaxHp, from the original's GeneratorOperation overload.
        return operation.Spec.ParsedOpcode is SpecOpcode.Heal or SpecOpcode.GainMaxHp;
    }

    public static bool IsRepeatedTriggerOrCondition(GeneratedOperation operation)
    {
        if (!operation.IsTriggerOrCondition && !IsDependencyPrefix(operation))
        {
            return false;
        }
        // APPROXIMATED: the original calls EffectBalanceModel.HasRepeatedOrMultiplicativePayoff.
        // The pool carries the compiled outcome of that analysis as a flag, which
        // is exactly the same bit for the same spec.
        return HasFlag(operation, "repeated_or_multiplicative");
    }

    public static bool IsNextAttackGrantTrigger(GeneratedOperation operation)
    {
        string? kind = operation.Spec.Trigger?.Kind;
        return kind is "next_attack" or "next_attacks_this_turn";
    }

    public static bool IsNextAttackGrantPayoff(GeneratedOperation operation)
    {
        if (operation.Template is "I:ReplayAttack" or "I:SetCostZero")
        {
            return true;
        }
        if (operation.Scope != FragmentScope.Modifier)
        {
            return false;
        }
        if (operation.Template == "M:DamagePerExhaustCard")
        {
            return true;
        }
        RuntimeSpec spec = operation.Spec;
        return spec.ParsedOpcode == SpecOpcode.ModifyDamage
            && spec.Variant is "strike_count_scaled" or "vulnerable_scaled";
    }

    public static bool IsSelfExhaustEventTrigger(GeneratedOperation operation) =>
        operation.Source.Atom.CardReference == "ThisCard"
        && operation.Spec.Trigger?.Kind == "self_exhausted";

    public static bool IsSelfZoneStateCondition(GeneratedOperation operation)
    {
        if (operation.Template == "R:AtTurnEndWhenTopOfDraw")
        {
            return true;
        }
        RuntimeSpec spec = operation.Spec;
        string? kind = spec.Trigger?.Kind ?? spec.Condition?.Kind;
        return kind is "turn_start_if_self_in_exhaust" or "turn_end_if_self_in_exhaust"
            or "turn_end_if_self_on_draw_top";
    }

    public static bool IsPersistentPowerFoundation(GeneratedOperation operation)
    {
        // APPROXIMATED: the original also ORs IsPersistentStat (N:Dex / N:Thorns /
        // N:Intangible ...), IsRestrictedEffect and IsCopyThisCardToDiscard.
        // api_power_foundation is absent from the pool (0 occurrences), so we are
        // left with the scope test, which is narrower than the original. Effect:
        // a Power shell's first slot must be a trigger/rule fragment. That is the
        // original's dominant case, and it never produces an incoherent card -
        // it only excludes a few power-foundation atoms we cannot identify.
        if (HasFlag(operation, "api_power_foundation"))
        {
            return true;
        }
        return operation.Scope is FragmentScope.AbilityTrigger or FragmentScope.AbilityRule;
    }

    public static bool IsExtremeLifecycleDownside(GeneratedOperation operation) =>
        ExtremeLifecycleDownsideTemplates.Contains(operation.Template);

    public static bool IsSelfCostChange(GeneratedOperation operation) =>
        operation.Template == "D:IncreaseThisCardCost" || IsSelfCostReduction(operation);

    public static bool IsSelfCostReduction(GeneratedOperation operation)
    {
        if (SelfCostReductionTemplates.Contains(operation.Template))
        {
            return true;
        }
        string? kind = operation.Spec.Trigger?.Kind;
        return kind is "attack_played_cost_reduction" or "skill_played_cost_reduction";
    }

    public static bool IsCurrentBlockDamageModifier(GeneratedOperation operation)
    {
        RuntimeSpec spec = operation.Spec;
        return spec.ParsedOpcode == SpecOpcode.ModifyDamage && spec.Variant == "current_block";
    }

    public static bool IsEnemyDamage(GeneratedOperation operation)
    {
        if (HasFlag(operation, "api_enemy_damage"))
        {
            return true;
        }
        string template = operation.Template;
        return template.StartsWith("T:D", StringComparison.Ordinal)
            || template.StartsWith("N:AllD", StringComparison.Ordinal)
            || template.StartsWith("N:RandomD", StringComparison.Ordinal)
            || template.StartsWith("T:ProxyDamage_", StringComparison.Ordinal)
            || template.StartsWith("N:ProxyDamage_", StringComparison.Ordinal)
            || template is "CL:RollingAllDamage" or "D:RepeatPerEnergySpentThisTurn"
                or "NCR:OstyDamage" or "NCR:OstyAllDamage" or "NCR:DoomScaledDamage" or "NCR:UnpoweredDamage";
    }

    public static bool IsIntrinsicMultiHitDamage(GeneratedOperation operation)
    {
        if (!IsEnemyDamage(operation))
        {
            return false;
        }
        foreach (ResolvedValue value in operation.Values)
        {
            if (string.Equals(value.Id, "hits", StringComparison.Ordinal))
            {
                return value.Value > 1;
            }
        }
        return false;
    }

    public static bool IsHitEnemyDamageVariant(GeneratedOperation operation) =>
        HasFlag(operation, "hit_enemy_reference");

    public static bool HasLightningEvokeTriggerContext(IReadOnlyList<GeneratedOperation> previous)
    {
        if (previous.Count == 0)
        {
            return false;
        }
        GeneratedOperation last = previous[^1];
        if (last.Template == "A:whenLightningEvoked")
        {
            return true;
        }
        int index = last.TriggerIndex;
        return index >= 0 && index < previous.Count && previous[index].Template == "A:whenLightningEvoked";
    }

    public static bool RequiresSpecificTriggerPayload(GeneratedOperation operation)
    {
        if (HasFlag(operation, "requires_event_card_payload")
            || HasFlag(operation, "requires_referenced_card_payload")
            || HasFlag(operation, "requires_event_amount_payload")
            || HasFlag(operation, "referenced_non_attack_exhaust"))
        {
            return true;
        }
        RuntimeSpec spec = operation.Spec;
        if (spec.ParsedOpcode == SpecOpcode.ExhaustCard && spec.Variant == "referenced")
        {
            return true;
        }
        if (spec.ParsedOpcode == SpecOpcode.UpgradeCard && spec.Variant == "referenced")
        {
            return true;
        }
        return spec.ParsedOpcode == SpecOpcode.CreateCopy
            && spec.Variant is "referenced_attack" or "referenced_card";
    }

    public static bool CanSupplySpecificTriggerPayload(GeneratedOperation trigger, GeneratedOperation effect)
    {
        string? kind = trigger.Spec.Trigger?.Kind;
        RuntimeSpec spec = effect.Spec;
        switch (effect.Template)
        {
            case "D:ReplayEventCard":
                return kind == "first_card_played_each_turn";
            case "D:ReturnEventCardToHand":
            case "CL:PutEventCardOnDrawTop":
            case "I:UpgradeThatCard":
                return SuppliesReferencedCard(trigger);
            case "I:PlayAtRandomEnemy":
                return kind == "strike_card_drawn";
            case "NCR:ApplyEventDamageAsDoom":
                return kind is "attack_damaged_enemy" or "attack_dealt_damage";
            case "NCR:AllEnemiesLoseEventHp":
                return kind == "osty_hp_lost";
        }
        if (spec.ParsedOpcode == SpecOpcode.ExhaustCard && spec.Variant == "referenced")
        {
            return kind == "skill_played";
        }
        if (HasFlag(effect, "referenced_non_attack_exhaust"))
        {
            return kind == "for_each_exhausted_non_attack";
        }
        if (spec.ParsedOpcode == SpecOpcode.CreateCopy && spec.Variant == "referenced_card")
        {
            return SuppliesReferencedCard(trigger);
        }
        if (spec.ParsedOpcode == SpecOpcode.CreateCopy && spec.Variant == "referenced_attack")
        {
            return kind == "nth_attack_played_this_turn" && trigger.ValueOf("threshold", 1) == 3;
        }
        return false;
    }

    private static bool SuppliesReferencedCard(GeneratedOperation trigger)
    {
        RuntimeSpec spec = trigger.Spec;
        if (spec.Trigger?.Kind is { } kind && SuppliesReferencedCardTriggerKinds.Contains(kind))
        {
            return true;
        }
        return spec.ParsedOpcode == SpecOpcode.MoveCard && spec.Variant is "random" or "selected";
    }

    public static bool RequiresSingleEnemyTarget(GeneratedOperation operation)
    {
        if (operation.Scope != FragmentScope.SingleEnemyOnly && !operation.Source.Atom.RequiresSingleTarget)
        {
            return HasFlag(operation, "requires_selected_enemy");
        }
        return true;
    }

    public static bool UsesExplicitRandomEnemyTarget(GeneratedOperation operation) =>
        HasFlag(operation, "random_enemy_reference");

    public static bool CanResolveTriggeredEnemyTarget(GeneratedOperation trigger, GeneratedOperation effect)
    {
        if (UsesExplicitRandomEnemyTarget(effect))
        {
            return true;
        }
        if (!TriggerSuppliesEnemyTarget(trigger))
        {
            return false;
        }
        if (!HasFlag(effect, "event_enemy_reference"))
        {
            return TriggerImplicitlyTargetsAttacker(trigger);
        }
        return true;
    }

    private static bool TriggerSuppliesEnemyTarget(GeneratedOperation trigger) =>
        TriggerSuppliesEnemyTargetTemplates.Contains(trigger.Template) || TriggerImplicitlyTargetsAttacker(trigger);

    public static bool TriggerImplicitlyTargetsAttacker(GeneratedOperation trigger) =>
        trigger.Spec.Trigger?.Kind == "attack_received";

    public static bool TriggerLosesOriginalEnemyTarget(GeneratedOperation trigger)
    {
        string? kind = trigger.Spec.Trigger?.Kind;
        if (kind is not ("next_turn_start" or "next_turns_start"))
        {
            return trigger.Template == "CL:AfterTurns";
        }
        return true;
    }

    public static bool IsFatalCondition(GeneratedOperation operation) =>
        operation.Spec.Condition?.Kind == "fatal";

    public static bool IsDoubleTargetVulnerable(GeneratedOperation operation) =>
        operation.Template == "T:Apply" && operation.Spec.Variant == "vulnerable_double";

    public static bool IsDrawPileEmptyCondition(GeneratedOperation operation) =>
        operation.Template == "C:playableIfDrawPileEmpty";

    /// <summary>
    /// The original's IsFailableOneShotCondition: a condition that can fail and
    /// is consumed on use. A card whose only payoff hangs off such a condition
    /// would do nothing when the condition fails, so the assembly rules require
    /// at least one unconditional beneficial effect alongside it.
    /// </summary>
    public static bool IsFailableOneShotCondition(GeneratedOperation operation) =>
        operation.Template is "C:ifFatal" or "D:IfFatal" or "R:IfFatal" or "C:ifLastDrawnSkill"
            or "C:ifTargetPoisoned" or "CL:IfNoAttacksInHand" or "D:IfCardsPlayedBelow" or "C:if"
            or "CL:IfFatal" or "CL:IfHandEmpty" or "D:IfEnemyIntendsAttack"
            or "NCR:IfDoomAppliedThisTurn" or "NCR:IfFirstPlayThisTurn" or "NCR:IfOstyAlive"
            or "NCR:IfOstyAttackedThisTurn";

    /// <summary>
    /// The original's CanAnchorRestrictedPowerFoundation: a Power's first slot may
    /// be a plain block/damage atom only if the pool also contains the matching
    /// run-growth atom that makes it a legitimate power foundation.
    /// </summary>
    public static bool CanAnchorRestrictedPowerFoundation(GeneratedOperation operation, IReadOnlyList<JoinedFragment> pool)
    {
        if (operation.Template == "N:B")
        {
            return ContainsTemplate(pool, "D:IncreaseThisCardBlockRun");
        }
        if (IsEnemyDamage(operation))
        {
            return ContainsTemplate(pool, "NCR:IncreaseThisCardDamageRun");
        }
        return false;
    }

    private static bool ContainsTemplate(IReadOnlyList<JoinedFragment> pool, string template)
    {
        foreach (JoinedFragment fragment in pool)
        {
            if (string.Equals(fragment.Template, template, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsGrandFinalePayoff(GeneratedOperation operation) =>
        operation.Template == "N:AllD" && operation.ValueOf("damage") == 60;

    public static bool RequiresPlayerChoice(GeneratedOperation operation)
    {
        string reference = operation.Source.Atom.CardReference;
        if (reference is "HandCard" or "HandAttack")
        {
            return true;
        }
        if (AtomicChoiceProxyTemplates.Contains(operation.Template))
        {
            return true;
        }
        if (PlayerChoiceTemplates.Contains(operation.Template))
        {
            return true;
        }
        return HasFlag(operation, "requires_player_choice");
    }

    public static bool TriggerSupportsPlayerChoice(GeneratedOperation trigger)
    {
        if (trigger.Template == "A:turnStart")
        {
            return true;
        }
        if (ChoiceUnsupportedTriggerTemplates.Contains(trigger.Template))
        {
            return false;
        }
        string? kind = trigger.Spec.Trigger?.Kind;
        return kind == null || !ChoiceUnsupportedTriggerKinds.Contains(kind);
    }

    /// <summary>
    /// The original's DifficultConditionTier. Note the branch ORDER matters:
    /// the threshold>=3 test for the two high-cost triggers runs before the
    /// flag tests, and the tier-3 template group is checked before the flag.
    /// </summary>
    public static int DifficultConditionTier(GeneratedOperation operation)
    {
        string template = operation.Template;
        if (template is "A:whenEnergyCostAtLeast" or "NCR:WheneverHighCostCardPlayed"
            && operation.ValueOf("threshold") >= 3)
        {
            return 2;
        }
        if (template == "C:playableIfDrawPileEmpty")
        {
            return 4;
        }
        if (template is "CL:IfHandEmpty" or "R:IfCardsPlayedAtLeastThisTurn" or "R:AtTurnEndWhenTopOfDraw")
        {
            return 3;
        }
        if (HasFlag(operation, "difficult_condition_tier_3"))
        {
            return 3;
        }
        if (template is "C:ifTargetPoisoned" or "C:ifLastDrawnSkill" or "CL:IfNoAttacksInHand"
            or "D:IfHasFrost" or "R:IfEnergyXAtLeast")
        {
            return 2;
        }
        if (HasFlag(operation, "difficult_condition_tier_2"))
        {
            return 2;
        }
        return template is "D:IfEnemyIntendsAttack" or "D:IfCardsPlayedBelow" ? 1 : 0;
    }

    public static bool DifficultConditionAwaitsPayoff(IReadOnlyList<GeneratedOperation> operations)
    {
        if (operations.Count == 0)
        {
            return false;
        }
        GeneratedOperation last = operations[^1];
        if (TriggerNeedsLinkedEffect(last) && DifficultConditionTier(last) >= 2)
        {
            return true;
        }
        int index = last.TriggerIndex;
        if (index < 0 || index >= operations.Count || DifficultConditionTier(operations[index]) < 2)
        {
            return false;
        }
        return CountOperationsBoundTo(operations, index) < 2;
    }

    public static int PayoffBudgetBonus(IReadOnlyList<GeneratedOperation> operations)
    {
        if (operations.Count == 0)
        {
            return 0;
        }
        GeneratedOperation last = operations[^1];
        int tier = DifficultConditionTier(last);
        if (tier > 0 && (TriggerNeedsLinkedEffect(last) || IsDependencyPrefix(last)))
        {
            return tier >= 4 ? 2 : tier >= 3 ? 1 : 0;
        }
        int index = last.TriggerIndex;
        if (index < 0 || index >= operations.Count)
        {
            return 0;
        }
        if (CountOperationsBoundTo(operations, index) >= 2)
        {
            return 0;
        }
        int ownerTier = DifficultConditionTier(operations[index]);
        return ownerTier >= 4 ? 2 : ownerTier >= 3 ? 1 : 0;
    }

    public static int CountOperationsBoundTo(IReadOnlyList<GeneratedOperation> operations, int triggerIndex)
    {
        int count = 0;
        foreach (GeneratedOperation operation in operations)
        {
            if (operation.TriggerIndex == triggerIndex)
            {
                count++;
            }
        }
        return count;
    }

    // ---------------------------------------------------------------------
    // Duplicate / field-count rules
    // ---------------------------------------------------------------------

    public static int FieldOccurrenceCount(IReadOnlyList<GeneratedOperation> operations, GeneratedOperation candidate)
    {
        string key = candidate.StructuralFieldKey;
        int count = 0;
        foreach (GeneratedOperation operation in operations)
        {
            if (string.Equals(operation.StructuralFieldKey, key, StringComparison.Ordinal))
            {
                count++;
            }
        }
        return count;
    }

    public static string? CardUniqueEffectKey(GeneratedOperation operation)
    {
        if (IsExhaustAllHand(operation))
        {
            return "clear:all_hand_exhaust";
        }
        RuntimeSpec spec = operation.Spec;
        if (spec.ParsedOpcode == SpecOpcode.CombatRule && UniqueRuleVariants.Contains(spec.Variant))
        {
            return "rule:" + spec.Variant;
        }
        return TemplateUniqueKeys.TryGetValue(operation.Template, out string? key) ? key : null;
    }

    public static bool WouldDuplicateCardUniqueEffect(IReadOnlyList<GeneratedOperation> operations, GeneratedOperation candidate)
    {
        string? key = CardUniqueEffectKey(candidate);
        if (key == null)
        {
            return false;
        }
        foreach (GeneratedOperation operation in operations)
        {
            if (string.Equals(CardUniqueEffectKey(operation), key, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsExhaustAllHand(GeneratedOperation operation)
    {
        RuntimeSpec spec = operation.Spec;
        return operation.Template == "N:Exhaust"
            && spec.ParsedOpcode == SpecOpcode.ExhaustCard
            && spec.Variant == "all"
            && spec.SourceZone == "hand"
            && spec.CardFilter == "any";
    }

    // ---------------------------------------------------------------------
    // X-resource requirement
    // ---------------------------------------------------------------------

    public enum XRequirement
    {
        None,
        Energy,
        Star,
        Either,
    }

    public static XRequirement XRequirementOf(GeneratedOperation operation)
    {
        bool energy = false;
        bool star = false;
        // The pool's only numeric-slot sources are "fixed", "energy_x" and
        // "star_x" (no "special_x"), so reading the spec's slot sources is the
        // whole story. The compiled flags are folded in as well because the
        // original's UsesX consults both.
        foreach (NumericSlot slot in operation.Spec.Values)
        {
            if (slot.Source == "energy_x")
            {
                energy = true;
            }
            else if (slot.Source == "star_x")
            {
                star = true;
            }
        }
        foreach (string flag in operation.Spec.Flags)
        {
            if (flag == "uses_energy_x")
            {
                energy = true;
            }
            else if (flag == "uses_star_x")
            {
                star = true;
            }
        }
        return (energy, star) switch
        {
            (true, true) => XRequirement.Either,
            (true, false) => XRequirement.Energy,
            (false, true) => XRequirement.Star,
            _ => XRequirement.None,
        };
    }

    public static bool UsesX(GeneratedOperation operation) => XRequirementOf(operation) != XRequirement.None;

    /// <summary>
    /// APPROXIMATED. The original compares a compiled "X effect kind" key. We use
    /// the X requirement plus the opcode, which is coarser: two different X
    /// payoffs on the same opcode are treated as the same kind. Effect is that we
    /// occasionally reject a legal second X effect. Never the other way round.
    /// </summary>
    public static bool HasSameXEffectKind(GeneratedOperation a, GeneratedOperation b) =>
        XRequirementOf(a) == XRequirementOf(b) && a.Spec.Opcode == b.Spec.Opcode;

    // ---------------------------------------------------------------------
    // Negative-effect approximation
    // ---------------------------------------------------------------------

    public static bool IsNegativeEffect(GeneratedOperation operation)
    {
        if (HasFlag(operation, "api_negative"))
        {
            return true;
        }
        if (operation.Spec.ParsedOpcode is { } opcode && NegativeOpcodes.Contains(opcode))
        {
            return true;
        }
        if (ExtremeLifecycleDownsideTemplates.Contains(operation.Template))
        {
            return true;
        }
        if (operation.Template == "D:IncreaseThisCardCost")
        {
            return true;
        }
        foreach (ResolvedValue value in operation.Values)
        {
            if (value.Value < 0)
            {
                return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------------

    private static bool HasFlag(GeneratedOperation operation, string flag)
    {
        foreach (string candidate in operation.Spec.Flags)
        {
            if (string.Equals(candidate, flag, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
