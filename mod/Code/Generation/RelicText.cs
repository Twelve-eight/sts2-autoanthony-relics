using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoAnthonyRelics.Generation;

/// <summary>
/// Player-facing text for generated relics, keyed by fragment semantics.
/// Both languages are authored here (English + Simplified Chinese) - the
/// workspace localization rule: author in English plus optional zhs; never
/// copy other-script assets.
/// </summary>
public static class RelicText
{
    public static string TriggerEn(TriggerFragment trigger) => (trigger.Kind, trigger.Condition) switch
    {
        ("turn_start", "first_turn") => "At the start of the first turn of each combat, ",
        ("turn_start", null) => "At the start of each of your turns, ",
        ("turn_start_early", "first_turn") => "At the start of the first turn of each combat, ",
        ("turn_end", "hand_empty") => "At the end of each of your turns, if your hand is empty, ",
        ("turn_end", "no_attack_played_this_turn") => "At the end of each of your turns, if you played no Attacks this turn, ",
        ("turn_end", null) => "At the end of each of your turns, ",
        ("combat_start", null) => "At the start of each combat, ",
        ("combat_end", null) => "At the end of each combat, ",
        ("combat_victory", "hp_below_half") => "After winning a combat, if your HP is half or less, ",
        ("combat_victory", null) => "After winning a combat, ",
        ("room_entered", null) => "Upon entering a combat, ",
        ("obtained", null) => "Upon pickup, ",
        ("card_played", "card_type_power") => "Whenever you play a Power card, ",
        ("card_played", null) => "Whenever you play a card, ",
        ("creature_died", "enemy_side") => "Whenever an enemy dies, ",
        ("gold_gained", null) => "Whenever you gain Gold, ",
        ("block_cleared", "turn_equals_2") => "When your Block is cleared on turn 2 of each combat, ",
        ("block_cleared", "turn_equals_3") => "When your Block is cleared on turn 3 of each combat, ",
        ("block_cleared", null) => "When your Block is cleared, ",
        _ => throw new InvalidOperationException($"no EN text for trigger {trigger.Kind}/{trigger.Condition}"),
    };

    public static string TriggerZhs(TriggerFragment trigger) => (trigger.Kind, trigger.Condition) switch
    {
        ("turn_start", "first_turn") => "每场战斗的第1回合开始时，",
        ("turn_start", null) => "你的每个回合开始时，",
        ("turn_start_early", "first_turn") => "每场战斗的第1回合开始时，",
        ("turn_end", "hand_empty") => "你的回合结束时，若你的手牌为空，",
        ("turn_end", "no_attack_played_this_turn") => "你的回合结束时，若本回合你没有打出过攻击牌，",
        ("turn_end", null) => "你的每个回合结束时，",
        ("combat_start", null) => "每场战斗开始时，",
        ("combat_end", null) => "每场战斗结束时，",
        ("combat_victory", "hp_below_half") => "战斗胜利后，若你的生命值不高于50%，",
        ("combat_victory", null) => "战斗胜利后，",
        ("room_entered", null) => "进入战斗时，",
        ("obtained", null) => "拾取时，",
        ("card_played", "card_type_power") => "每当你打出一张能力牌，",
        ("card_played", null) => "每当你打出一张牌，",
        ("creature_died", "enemy_side") => "每当有敌人死亡，",
        ("gold_gained", null) => "每当你获得金币，",
        ("block_cleared", "turn_equals_2") => "每场战斗的第2回合你的格挡被清空时，",
        ("block_cleared", "turn_equals_3") => "每场战斗的第3回合你的格挡被清空时，",
        ("block_cleared", null) => "你的格挡被清空时，",
        _ => throw new InvalidOperationException($"no ZHS text for trigger {trigger.Kind}/{trigger.Condition}"),
    };

    public static string EffectEn(EffectFragment effect)
    {
        // List-item form: NO trailing period. Descriptions are single
        // sentences; GeneratedRelicDefinition joins concurrent effects with
        // punctuation and conjunctions derived from the real structure and
        // adds the final period. Keeping raw sentence punctuation here
        // produced "gain 14 Block. gain 1 Strength." (user report 2026-09-15).
        int amount = effect.Amount;
        return (effect.Opcode, effect.Variant, effect.Target) switch
        {
            ("apply_power", "vigor", "self") => $"gain {amount} Vigor",
            ("apply_power", "strength", "self") => $"gain {amount} Strength",
            ("apply_power", "thorns", "self") => $"gain {amount} Thorns",
            ("apply_power", "vulnerable", "all_enemies") => $"apply {amount} Vulnerable to ALL enemies",
            ("gain_block", _, "self") => $"gain {amount} Block",
            ("heal", _, "self") => $"heal {amount} HP",
            ("gain_energy", _, "self") => $"gain {amount} Energy",
            ("draw_cards", _, "self") => $"draw {amount} card{(amount == 1 ? "" : "s")}",
            ("gain_max_hp", _, "self") => $"gain {amount} Max HP",
            ("gain_gold", _, "self") => $"gain {amount} Gold",
            ("gain_max_potion", _, "self") => $"gain {amount} potion slot{(amount == 1 ? "" : "s")}",
            ("deal_damage", _, "all_enemies") => $"deal {amount} damage to ALL enemies",
            // Downside pool (user order 2026-09-15). lose_hp "unblockable" is
            // true HP loss (engine Unblockable|Unpowered, e.g. RoyalPoison);
            // plain lose_hp is blockable self-damage (e.g. PrecariousShears).
            ("lose_hp", "unblockable", "self") => $"lose {amount} HP",
            ("lose_hp", _, "self") => $"take {amount} damage",
            ("lose_max_hp", _, "self") => $"lose {amount} Max HP",
            ("lose_gold", "all", "self") => "lose all your Gold",
            ("lose_gold", _, "self") => $"lose {amount} Gold",
            ("add_curse", _, _) => "add 1 Curse to your deck",
            ("modify_hand_draw", _, "self") when amount >= 0 =>
                $"draw {amount} additional card{(amount == 1 ? "" : "s")} on turn 1 of each combat",
            ("modify_hand_draw", _, "self") =>
                $"draw {-amount} fewer card{(amount == -1 ? "" : "s")} on turn 1 of each combat",
            // Ancient restriction / benefit affixes (user order 2026-09-17).
            // Restrictions always ship with their engine offset (see
            // RelicGenerator.RestrictionOffsets), which is a separate fragment
            // and therefore a separate list item.
            ("modify_max_energy", _, "self") => $"gain {amount} additional Energy",
            ("restrict_gold", _, "self") => "you can no longer gain Gold",
            ("restrict_potion", _, "self") => "you can no longer obtain potions",
            ("restrict_card_play", _, "self") =>
                $"you cannot play more than {amount} card{(amount == 1 ? "" : "s")} each turn",
            ("restrict_draw", _, "self") => "card effects no longer draw cards for you",
            ("modify_card_cost", _, "self") => "Power cards cost 1 more",
            ("enemy_strength_gain", _, "self") => $"enemies gain {amount} Strength when they enter combat",
            ("retain_hand", _, "self") => "you no longer discard your hand at the end of your turn",
            ("extra_turn", _, "self") => "take an extra turn if you play no cards",
            ("expand_card_pool", _, "self") => "card rewards may contain cards from any character",
            ("enchant_reward", _, "self") => "card rewards are enchanted with Glam",
            _ => throw new InvalidOperationException($"no EN text for effect {effect.Opcode}/{effect.Variant}/{effect.Target}"),
        };
    }

    public static string EffectZhs(EffectFragment effect)
    {
        // List-item form: NO trailing 。 (see EffectEn).
        int amount = effect.Amount;
        return (effect.Opcode, effect.Variant, effect.Target) switch
        {
            ("apply_power", "vigor", "self") => $"获得{amount}点活力",
            ("apply_power", "strength", "self") => $"获得{amount}点力量",
            ("apply_power", "thorns", "self") => $"获得{amount}点荆棘",
            ("apply_power", "vulnerable", "all_enemies") => $"给予所有敌人{amount}层易伤",
            ("gain_block", _, "self") => $"获得{amount}点格挡",
            ("heal", _, "self") => $"回复{amount}点生命",
            ("gain_energy", _, "self") => $"获得{amount}点能量",
            ("draw_cards", _, "self") => $"抽{amount}张牌",
            ("gain_max_hp", _, "self") => $"提升{amount}点生命上限",
            ("gain_gold", _, "self") => $"获得{amount}金币",
            ("gain_max_potion", _, "self") => $"获得{amount}个药水栏位",
            ("deal_damage", _, "all_enemies") => $"对所有敌人造成{amount}点伤害",
            ("lose_hp", "unblockable", "self") => $"失去{amount}点生命",
            ("lose_hp", _, "self") => $"受到{amount}点伤害",
            ("lose_max_hp", _, "self") => $"失去{amount}点生命上限",
            ("lose_gold", "all", "self") => "失去所有金币",
            ("lose_gold", _, "self") => $"失去{amount}金币",
            ("add_curse", _, _) => "将1张诅咒牌加入你的牌堆",
            ("modify_hand_draw", _, "self") when amount >= 0 => $"每场战斗第1回合额外抽{amount}张牌",
            ("modify_hand_draw", _, "self") => $"每场战斗第1回合少抽{-amount}张牌",
            ("modify_max_energy", _, "self") => $"额外获得{amount}点能量",
            ("restrict_gold", _, "self") => "你不再能获得金币",
            ("restrict_potion", _, "self") => "你不再能获得药水",
            ("restrict_card_play", _, "self") => $"每回合最多打出{amount}张牌",
            ("restrict_draw", _, "self") => "卡牌效果不再为你抽牌",
            ("modify_card_cost", _, "self") => "能力牌的费用提高1点",
            ("enemy_strength_gain", _, "self") => $"敌人进入战斗时获得{amount}点力量",
            ("retain_hand", _, "self") => "回合结束时不再弃掉你的手牌",
            ("extra_turn", _, "self") => "若你本回合未打出卡牌,则获得一个额外回合",
            ("expand_card_pool", _, "self") => "卡牌奖励可能包含任意角色的卡牌",
            ("enchant_reward", _, "self") => "卡牌奖励会被附上Glam",
            _ => throw new InvalidOperationException($"no ZHS text for effect {effect.Opcode}/{effect.Variant}/{effect.Target}"),
        };
    }

    private static readonly string[] AdjectivesEn =
    {
        "Anthony's", "Anomalous", "Astral", "Bizarre", "Brazen", "Calculating", "Chaotic", "Cryptic",
        "Curious", "Doubtful", "Erratic", "Experimental", "Improvised", "Incongruous", "Madcap", "Misplaced",
        "Misremembered", "Odd", "Peculiar", "Perplexing", "Recalculated", "Rewritten", "Scrambled", "Unlikely",
    };

    private static readonly string[] NounsEn =
    {
        "Amulet", "Anvil", "Bauble", "Bell", "Bottle", "Censer", "Charm", "Coin",
        "Compass", "Crown", "Dice", "Figurine", "Flask", "Fob", "Idol", "Keepsake",
        "Locket", "Medallion", "Orb", "Pendant", "Prism", "Ring", "Sigil", "Talisman",
    };

    private static readonly string[] AdjectivesZhs =
    {
        "安东尼的", "异常的", "星界的", "怪异的", "莽撞的", "精算的", "混沌的", "晦涩的",
        "好奇的", "存疑的", "反复无常的", "实验性的", "即兴的", "失调的", "疯狂的", "放错处的",
        "记错的", "奇特的", "非凡的", "费解的", "重算的", "重写的", "打乱的", "不太可能的",
    };

    private static readonly string[] NounsZhs =
    {
        "护身符", "铁砧", "小饰品", "铃铛", "瓶子", "香炉", "符咒", "硬币",
        "罗盘", "王冠", "骰子", "小雕像", "烧瓶", "表坠", "神像", "纪念物",
        "盒坠", "大勋章", "宝珠", "吊坠", "棱镜", "戒指", "印记", "护符",
    };

    public static string NameEn(int adjectiveIndex, int nounIndex) =>
        $"{AdjectivesEn[adjectiveIndex % AdjectivesEn.Length]} {NounsEn[nounIndex % NounsEn.Length]}";

    public static string NameZhs(int adjectiveIndex, int nounIndex) =>
        AdjectivesZhs[adjectiveIndex % AdjectivesZhs.Length] + NounsZhs[nounIndex % NounsZhs.Length];

    public const string FlavorEn = "The algorithm insists this is a relic.";
    public const string FlavorZhs = "算法坚持说这是一件遗物。";
}
