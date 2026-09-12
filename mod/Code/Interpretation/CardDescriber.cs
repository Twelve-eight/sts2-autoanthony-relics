using System.Text;
using AutoAnthonyRelics.Generation;

namespace AutoAnthonyRelics.Interpretation;

/// <summary>
/// Stage D: natural-language card description from a <see cref="CardPlan"/>.
///
/// This is NOT a general localisation system - it is a deterministic
/// renderer that turns the planned operations into player-readable text.
/// Every line must be reconstructible from the plan alone (no engine state
/// lookups), so the probe can assert on it without touching Godot.
///
/// Two languages are supported: "zhs" (simplified Chinese) and "eng".
/// Unimplemented / delegated variants degrade to their template id in
/// brackets so the reader knows something is present but not yet described.
/// </summary>
internal static class CardDescriber
{
    public static string Describe(GeneratedCard card, string lang = "zhs")
    {
        CardPlan plan = OperationPlanner.PlanCard(card, InterpreterContext.OnPlay(
            (MegaCrit.Sts2.Core.Entities.Cards.CardType)card.Type, TargetAvailability.SelectedEnemy));
        var sb = new StringBuilder();
        sb.AppendLine(Header(card, lang));

        for (int i = 0; i < plan.Operations.Count; i++)
        {
            OperationPlan op = plan.Operations[i];
            switch (op.Disposition)
            {
                case PlanDisposition.CarriedHost:
                    sb.AppendLine(HostPrefix(op, lang));
                    foreach (int linked in plan.LinkedEffectIndices(i))
                        sb.AppendLine("  " + EffectLine(plan.Operations[linked], lang));
                    break;

                case PlanDisposition.InlineGate:
                    sb.AppendLine(GateLine(op, lang));
                    foreach (int linked in plan.LinkedEffectIndices(i))
                        sb.AppendLine("  " + EffectLine(plan.Operations[linked], lang));
                    break;

                case PlanDisposition.OnPlay:
                    sb.AppendLine(EffectLine(op, lang));
                    break;

                case PlanDisposition.Modifier:
                case PlanDisposition.LinkedEffect:
                    // Modifiers are invisible; linked effects are rendered under their host.
                    break;
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string Header(GeneratedCard card, string lang)
    {
        string cost = card.Cost.ToString();
        if (card.HasStarCostX) cost += "X";
        else if (card.StarCost >= 0) cost += $"({card.StarCost}*)";

        string type = TypeName(card.Type, lang);
        string target = TargetName(card.Target, lang);
        string rarity = RarityName(card.Rarity, lang);

        return lang == "zhs"
            ? $"[费用: {cost}] [{rarity}] {type} {target}"
            : $"[Cost: {cost}] [{rarity}] {type} {target}";
    }

    private static string HostPrefix(OperationPlan op, string lang)
    {
        string? kind = op.TriggerKind;
        string? lifetime = op.TriggerLifetime;

        if (string.IsNullOrEmpty(kind))
            return lang == "zhs" ? "每当触发时，" : "Whenever triggered, ";

        // Lifetime prefix
        string lifetimePrefix = lifetime switch
        {
            "combat" => lang == "zhs" ? "每场战斗中，" : "During combat, ",
            "next_turn" => lang == "zhs" ? "下回合，" : "Next turn, ",
            "this_turn" => lang == "zhs" ? "本回合内，" : "This turn, ",
            _ => "",
        };

        string kindText = kind switch
        {
            "turn_start" => lang == "zhs" ? "每回合开始时，" : "at the start of your turn, ",
            "next_turn_start" => lang == "zhs" ? "下回合开始时，" : "at the start of your next turn, ",
            "turn_end" => lang == "zhs" ? "每回合结束时，" : "at the end of your turn, ",
            "card_played" => lang == "zhs" ? "每当你打出一张牌时，" : "whenever you play a card, ",
            "attack_played" => lang == "zhs" ? "每当你打出一张攻击牌时，" : "whenever you play an Attack, ",
            "attack_received" => lang == "zhs" ? "每当你受到攻击时，" : "whenever you are attacked, ",
            "owner_hp_lost_during_turn" => lang == "zhs" ? "每当你在回合内失去生命时，" : "whenever you lose HP during your turn, ",
            "card_exhausted" => lang == "zhs" ? "每当你消耗一张牌时，" : "whenever you Exhaust a card, ",
            "power_played" => lang == "zhs" ? "每当你打出一张能力牌时，" : "whenever you play a Power, ",
            "status_generated" => lang == "zhs" ? "每当你生成一张状态时，" : "whenever you generate a Status, ",
            _ => $"[{kind}]",
        };

        if (lang == "zhs")
            return lifetimePrefix + kindText;

        // lifetimePrefix already ends with a space when present; kindText is
        // lower-case after the prefix. If there is no lifetime prefix, the
        // first letter of the sentence must be capitalised.
        string body = lifetimePrefix.Length > 0 ? kindText : char.ToUpper(kindText[0]) + kindText.Substring(1);
        return lifetimePrefix + body;
    }

    private static string GateLine(OperationPlan op, string lang)
    {
        string? kind = op.ConditionKind;
        if (string.IsNullOrEmpty(kind))
            return lang == "zhs" ? "如果条件满足，则：" : "If condition met:";

        string text = kind switch
        {
            "hand_empty" => lang == "zhs" ? "如果手牌为空，则：" : "If your hand is empty:",
            "no_attacks_in_hand" => lang == "zhs" ? "如果手牌中没有攻击牌，则：" : "If there are no Attacks in your hand:",
            "enemy_intends_attack" => lang == "zhs" ? "如果敌人意图攻击，则：" : "If the enemy intends to attack:",
            "target_has_vulnerable" => lang == "zhs" ? "如果目标拥有易伤，则：" : "If the target has Vulnerable:",
            "target_has_poison" => lang == "zhs" ? "如果目标拥有中毒，则：" : "If the target has Poison:",
            "has_frost_orb" => lang == "zhs" ? "如果你拥有冰球，则：" : "If you have a Frost Orb:",
            "first_play_of_this_card_this_turn" => lang == "zhs" ? "如果本回合是首次打出此牌，则：" : "If this is the first time you play this card this turn:",
            "draw_pile_empty" => lang == "zhs" ? "如果抽牌堆为空，则：" : "If your draw pile is empty:",
            "fatal" => lang == "zhs" ? "如果目标即将死亡，则：" : "If the target would die:",
            "cards_played_this_turn_below" => lang == "zhs" ? "如果本回合打出的牌数少于阈值，则：" : "If cards played this turn is below threshold:",
            "cards_played_this_turn_at_least" => lang == "zhs" ? "如果本回合打出的牌数达到阈值，则：" : "If cards played this turn reaches threshold:",
            "owner_lost_hp_this_turn" => lang == "zhs" ? "如果本回合失去过生命，则：" : "If you lost HP this turn:",
            "exhaust_pile_minimum" => lang == "zhs" ? "如果消耗堆牌数达到阈值，则：" : "If exhaust pile reaches threshold:",
            "card_exhausted_this_turn" => lang == "zhs" ? "如果本回合消耗过牌，则：" : "If you Exhausted a card this turn:",
            "osty_alive" => lang == "zhs" ? "如果奥斯提存活，则：" : "If Osty is alive:",
            "osty_attacked_this_turn" => lang == "zhs" ? "如果奥斯提本回合攻击过，则：" : "If Osty attacked this turn:",
            "doom_applied_this_turn" => lang == "zhs" ? "如果本回合施加过毁灭，则：" : "If Doom was applied this turn:",
            "last_drawn_card_is_skill" => lang == "zhs" ? "如果最后抽到的牌是技能牌，则：" : "If your last drawn card is a Skill:",
            _ => $"[{kind}]",
        };
        return text;
    }

    private static string EffectLine(OperationPlan op, string lang)
    {
        if (op.Outcome == PlanOutcome.DelegatedToNative)
            return lang == "zhs" ? $"（引擎效果: {op.Variant}）" : $"(engine effect: {op.Variant})";

        if (op.Outcome == PlanOutcome.Unsupported)
            return lang == "zhs" ? $"（未实现: {op.Opcode} {op.Variant}）" : $"(not yet: {op.Opcode} {op.Variant})";

        string amount = op.Amount.ToString();
        var target = op.Target;
        var action = op.Action;

        return action switch
        {
            PlannedActionKind.DealDamage => DealDamageLine(amount, target, lang),
            PlannedActionKind.GainBlock => lang == "zhs" ? $"获得 {amount} 点格挡。" : $"Gain {amount} Block.",
            PlannedActionKind.DrawCards => lang == "zhs"
                ? $"抽 {amount} 张牌。"
                : $"Draw {amount} card{(amount == "1" ? "" : "s")}.",
            PlannedActionKind.GainEnergy => lang == "zhs" ? $"获得 {amount} 点能量。" : $"Gain {amount} Energy.",
            PlannedActionKind.GainStars => lang == "zhs" ? $"获得 {amount} 点星尘。" : $"Gain {amount} Stars.",
            PlannedActionKind.LoseHp => LoseHpLine(amount, target, lang),
            PlannedActionKind.Heal => lang == "zhs" ? $"回复 {amount} 点生命。" : $"Heal {amount} HP.",
            PlannedActionKind.ApplyPower => ApplyPowerLine(op.Variant, amount, target, lang),
            PlannedActionKind.ApplySelfPower => ApplyPowerLine(op.Variant, amount, PlannedTargetSelector.Self, lang),
            _ => lang == "zhs" ? $"（{action}）" : $"({action})",
        };
    }

    private static string DealDamageLine(string amount, PlannedTargetSelector target, string lang)
    {
        if (lang == "zhs")
        {
            return target switch
            {
                PlannedTargetSelector.Self => $"对自身造成 {amount} 点伤害。",
                PlannedTargetSelector.SelectedEnemy => $"对选中的敌人造成 {amount} 点伤害。",
                PlannedTargetSelector.AllEnemies => $"对所有敌人造成 {amount} 点伤害。",
                PlannedTargetSelector.RandomEnemy => $"对随机敌人造成 {amount} 点伤害。",
                _ => $"造成 {amount} 点伤害。",
            };
        }
        return target switch
        {
            PlannedTargetSelector.Self => $"Deal {amount} damage to yourself.",
            PlannedTargetSelector.SelectedEnemy => $"Deal {amount} damage to the selected enemy.",
            PlannedTargetSelector.AllEnemies => $"Deal {amount} damage to ALL enemies.",
            PlannedTargetSelector.RandomEnemy => $"Deal {amount} damage to a random enemy.",
            _ => $"Deal {amount} damage.",
        };
    }

    private static string LoseHpLine(string amount, PlannedTargetSelector target, string lang)
    {
        if (lang == "zhs")
        {
            return target switch
            {
                PlannedTargetSelector.Self => $"失去 {amount} 点生命。",
                PlannedTargetSelector.SelectedEnemy => $"使选中的敌人失去 {amount} 点生命。",
                PlannedTargetSelector.AllEnemies => $"使所有敌人失去 {amount} 点生命。",
                _ => $"失去 {amount} 点生命。",
            };
        }
        return target switch
        {
            PlannedTargetSelector.Self => $"Lose {amount} HP.",
            PlannedTargetSelector.SelectedEnemy => $"The selected enemy loses {amount} HP.",
            PlannedTargetSelector.AllEnemies => $"ALL enemies lose {amount} HP.",
            _ => $"Lose {amount} HP.",
        };
    }

    private static string ApplyPowerLine(string variant, string amount, PlannedTargetSelector target, string lang)
    {
        string powerName = variant switch
        {
            "vulnerable" => lang == "zhs" ? "易伤" : "Vulnerable",
            "weak" => lang == "zhs" ? "虚弱" : "Weak",
            "strength_gain" => lang == "zhs" ? "力量" : "Strength",
            "strength_loss" => lang == "zhs" ? "力量（负）" : "Strength (loss)",
            "strength_loss_this_turn" => lang == "zhs" ? "力量（本回合）" : "Strength (this turn)",
            "vulnerable_double" => lang == "zhs" ? "易伤（翻倍）" : "Vulnerable (double)",
            "retain_hand_this_turn" => lang == "zhs" ? "保留手牌" : "Retain Hand",
            _ => variant,
        };

        if (lang == "zhs")
        {
            string targetName = target switch
            {
                PlannedTargetSelector.Self => "自身",
                PlannedTargetSelector.SelectedEnemy => "选中的敌人",
                PlannedTargetSelector.AllEnemies => "所有敌人",
                _ => "目标",
            };
            return $"给予{targetName} {amount} 层{powerName}。";
        }

        string targetNameEng = target switch
        {
            PlannedTargetSelector.Self => "yourself",
            PlannedTargetSelector.SelectedEnemy => "the selected enemy",
            PlannedTargetSelector.AllEnemies => "ALL enemies",
            _ => "the target",
        };
        return $"Apply {amount} {powerName} to {targetNameEng}.";
    }

    private static string TypeName(GeneratedCardType type, string lang) => type switch
    {
        GeneratedCardType.Attack => lang == "zhs" ? "攻击" : "Attack",
        GeneratedCardType.Skill => lang == "zhs" ? "技能" : "Skill",
        GeneratedCardType.Power => lang == "zhs" ? "能力" : "Power",
        _ => type.ToString(),
    };

    private static string TargetName(GeneratedTargetMode target, string lang) => target switch
    {
        GeneratedTargetMode.SingleEnemy => lang == "zhs" ? "选中敌人" : "Single Enemy",
        GeneratedTargetMode.Other => lang == "zhs" ? "其他" : "Other",
        _ => target.ToString(),
    };

    private static string RarityName(GeneratedRarity rarity, string lang) => rarity switch
    {
        GeneratedRarity.Common => lang == "zhs" ? "普通" : "Common",
        GeneratedRarity.Uncommon => lang == "zhs" ? "罕见" : "Uncommon",
        GeneratedRarity.Rare => lang == "zhs" ? "稀有" : "Rare",
        GeneratedRarity.Basic => lang == "zhs" ? "基础" : "Basic",
        GeneratedRarity.Ancient => lang == "zhs" ? "远古" : "Ancient",
        _ => rarity.ToString(),
    };
}
