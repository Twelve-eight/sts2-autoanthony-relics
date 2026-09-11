using System;
using System.Collections.Generic;

namespace AutoAnthonyRelics.Data;

/// <summary>
/// Where a fragment sits in the assembly. The mechanism contract's key split:
/// scopes 0-2 are EFFECT fragments (they do something), scopes 3-5 are
/// TRIGGER/rule fragments (they gate something), 6 is a standalone action.
///
/// The original's OperationScope enum:
///   0 SingleEnemyOnly / 1 NonTargeted / 2 Modifier /
///   3 AbilityTrigger / 4 ConditionalTrigger / 5 AbilityRule / 6 Independent
/// </summary>
public enum FragmentScope
{
    SingleEnemyOnly,
    NonTargeted,
    Modifier,
    AbilityTrigger,
    ConditionalTrigger,
    AbilityRule,
    Independent,
    Unknown,
}

/// <summary>The 33 opcodes actually used by the pool, in descending instance count.</summary>
public enum SpecOpcode
{
    DealDamage,
    TemplateSelfAction,
    Trigger,
    GainBlock,
    ApplyPower,
    TemplateIndependentAction,
    DrawCards,
    TemplateModifier,
    TemplateTargetAction,
    GainEnergy,
    Condition,
    CombatRule,
    GainStars,
    LoseHp,
    ExhaustCard,
    DiscardCard,
    CreateCard,
    MoveCard,
    ModifyDamage,
    ModifyCost,
    CreateCopy,
    ModifyHits,
    ChooseGeneratedCard,
    UpgradeCard,
    ModifyBlock,
    GainMaxHp,
    Heal,
    ModifyOrbSlots,
    DrawAndDiscard,
    ModifyPower,
    ModifyX,
    EndTurn,
    RestrictBlockFromCards,
    Unknown,
}

public enum SpecTarget
{
    Self,
    SelectedEnemy,
    AllEnemies,
    SelectedCard,
    GeneratedCard,
    RandomEnemy,
    AllCards,
    ReferencedCard,
    Other,
    Unknown,
}

public enum Zone
{
    None,
    Hand,
    Discard,
    Draw,
    ColorlessPool,
    CurrentCharacterPool,
    OtherCharacterPools,
    Unknown,
}

public enum CardFilterKind
{
    Any,
    Attack,
    NonAttack,
    CostZero,
    CostNonZero,
    Unknown,
}

/// <summary>
/// String-to-enum parsing for the data files' snake_case / PascalCase mix.
///
/// Every parser is total: an unrecognised value yields the enum's Unknown member
/// rather than throwing. That is a deliberate data-layer guarantee - a future
/// pool revision with a new opcode must degrade to "unhandled fragment", never
/// abort mod init or a run.
/// </summary>
internal static class EnumParsing
{
    private static readonly Dictionary<string, FragmentScope> Scopes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SingleEnemyOnly"] = FragmentScope.SingleEnemyOnly,
            ["NonTargeted"] = FragmentScope.NonTargeted,
            ["Modifier"] = FragmentScope.Modifier,
            ["AbilityTrigger"] = FragmentScope.AbilityTrigger,
            ["ConditionalTrigger"] = FragmentScope.ConditionalTrigger,
            ["AbilityRule"] = FragmentScope.AbilityRule,
            ["Independent"] = FragmentScope.Independent,
        };

    private static readonly Dictionary<string, SpecOpcode> Opcodes =
        new(StringComparer.Ordinal)
        {
            ["deal_damage"] = SpecOpcode.DealDamage,
            ["template_self_action"] = SpecOpcode.TemplateSelfAction,
            ["trigger"] = SpecOpcode.Trigger,
            ["gain_block"] = SpecOpcode.GainBlock,
            ["apply_power"] = SpecOpcode.ApplyPower,
            ["template_independent_action"] = SpecOpcode.TemplateIndependentAction,
            ["draw_cards"] = SpecOpcode.DrawCards,
            ["template_modifier"] = SpecOpcode.TemplateModifier,
            ["template_target_action"] = SpecOpcode.TemplateTargetAction,
            ["gain_energy"] = SpecOpcode.GainEnergy,
            ["condition"] = SpecOpcode.Condition,
            ["combat_rule"] = SpecOpcode.CombatRule,
            ["gain_stars"] = SpecOpcode.GainStars,
            ["lose_hp"] = SpecOpcode.LoseHp,
            ["exhaust_card"] = SpecOpcode.ExhaustCard,
            ["discard_card"] = SpecOpcode.DiscardCard,
            ["create_card"] = SpecOpcode.CreateCard,
            ["move_card"] = SpecOpcode.MoveCard,
            ["modify_damage"] = SpecOpcode.ModifyDamage,
            ["modify_cost"] = SpecOpcode.ModifyCost,
            ["create_copy"] = SpecOpcode.CreateCopy,
            ["modify_hits"] = SpecOpcode.ModifyHits,
            ["choose_generated_card"] = SpecOpcode.ChooseGeneratedCard,
            ["upgrade_card"] = SpecOpcode.UpgradeCard,
            ["modify_block"] = SpecOpcode.ModifyBlock,
            ["gain_max_hp"] = SpecOpcode.GainMaxHp,
            ["heal"] = SpecOpcode.Heal,
            ["modify_orb_slots"] = SpecOpcode.ModifyOrbSlots,
            ["draw_and_discard"] = SpecOpcode.DrawAndDiscard,
            ["modify_power"] = SpecOpcode.ModifyPower,
            ["modify_x"] = SpecOpcode.ModifyX,
            ["end_turn"] = SpecOpcode.EndTurn,
            ["restrict_block_from_cards"] = SpecOpcode.RestrictBlockFromCards,
        };

    private static readonly Dictionary<string, SpecTarget> Targets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["self"] = SpecTarget.Self,
            ["selected_enemy"] = SpecTarget.SelectedEnemy,
            ["all_enemies"] = SpecTarget.AllEnemies,
            ["selected_card"] = SpecTarget.SelectedCard,
            ["generated_card"] = SpecTarget.GeneratedCard,
            ["random_enemy"] = SpecTarget.RandomEnemy,
            ["all_cards"] = SpecTarget.AllCards,
            ["referenced_card"] = SpecTarget.ReferencedCard,
            ["other"] = SpecTarget.Other,
        };

    private static readonly Dictionary<string, Zone> Zones =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["none"] = Zone.None,
            ["hand"] = Zone.Hand,
            ["discard"] = Zone.Discard,
            ["draw"] = Zone.Draw,
            ["colorless_pool"] = Zone.ColorlessPool,
            ["current_character_pool"] = Zone.CurrentCharacterPool,
            ["other_character_pools"] = Zone.OtherCharacterPools,
        };

    private static readonly Dictionary<string, CardFilterKind> CardFilters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["any"] = CardFilterKind.Any,
            ["attack"] = CardFilterKind.Attack,
            ["non_attack"] = CardFilterKind.NonAttack,
            ["cost_zero"] = CardFilterKind.CostZero,
            ["cost_nonzero"] = CardFilterKind.CostNonZero,
        };

    public static FragmentScope ParseScope(string? value) =>
        value != null && Scopes.TryGetValue(value, out var parsed) ? parsed : FragmentScope.Unknown;

    public static SpecOpcode ParseOpcode(string? value) =>
        value != null && Opcodes.TryGetValue(value, out var parsed) ? parsed : SpecOpcode.Unknown;

    public static SpecTarget ParseTarget(string? value) =>
        value != null && Targets.TryGetValue(value, out var parsed) ? parsed : SpecTarget.Unknown;

    public static Zone ParseZone(string? value) =>
        value != null && Zones.TryGetValue(value, out var parsed) ? parsed : Zone.Unknown;

    public static CardFilterKind ParseCardFilter(string? value) =>
        value != null && CardFilters.TryGetValue(value, out var parsed) ? parsed : CardFilterKind.Unknown;
}

/// <summary>
/// Flag names the interpreter and generator actually branch on. The pool uses 86
/// distinct flags; only the ones with behavioural meaning are named here so that
/// a typo in a lookup is a compile error instead of a silent no-op.
/// </summary>
internal static class SpecFlags
{
    public const string HasNumericLiteral = "has_numeric_literal";
    public const string CountUnitReference = "count_unit_reference";
    public const string ScalableRewardWording = "scalable_reward_wording";
    public const string PrintedDamageValue = "printed_damage_value";
    public const string PrintedBlockValue = "printed_block_value";
    public const string RepeatedOrMultiplicative = "repeated_or_multiplicative";
    public const string RequiresPlayerChoice = "requires_player_choice";
    public const string RequiresSingleTarget = "requires_single_target";
    public const string RequiresSelectedEnemy = "requires_selected_enemy";
    public const string RequiresHandCards = "requires_hand_cards";
    public const string ThisTurnReference = "this_turn_reference";
    public const string DelayedEffect = "delayed_effect";
    public const string DamageBudgetEffect = "damage_budget_effect";
    public const string ImmediateBlockGain = "immediate_block_gain";
    public const string ReplenishesHand = "replenishes_hand";
}
