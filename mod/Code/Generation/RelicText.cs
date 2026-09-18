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
            // Extra pool (user order 2026-09-18), ported from QuriousCraftingRelics.
            ("retain_hand_card", _, "self") => $"give up to {amount} card{(amount == 1 ? "" : "s")} in your hand Retain",
            ("sly_hand_card", _, "self") => $"give up to {amount} card{(amount == 1 ? "" : "s")} in your hand Sly",
            ("ethereal_hand_card", _, "self") => $"give up to {amount} card{(amount == 1 ? "" : "s")} in your hand Ethereal",
            ("enchant_hand", "sharp", "self") => $"enchant up to {amount} Attack card{(amount == 1 ? "" : "s")} in your hand with Sharp",
            ("enchant_hand", "nimble", "self") => $"enchant up to {amount} Block-granting card{(amount == 1 ? "" : "s")} in your hand with Nimble",
            ("enchant_hand", "imbued", "self") => $"enchant up to {amount} Skill card{(amount == 1 ? "" : "s")} in your hand with Imbued",
            ("enchant_deck", "sharp", "self") => $"enchant a random Attack card in your deck with {amount} Sharp",
            ("enchant_deck", "nimble", "self") => $"enchant a random Block-granting card in your deck with {amount} Nimble",
            ("enchant_deck", "imbued", "self") => $"enchant a random Skill card in your deck with {amount} Imbued",
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
            ("enchant_reward", _, "self") => "卡牌奖励会被附上华彩",
            // Extra pool (user order 2026-09-18). Keyword and enchantment names
            // copied from the base game's own zhs localization dump
            // (SlayTheSpire2.pck -> "RETAIN.title" 保留, "SLY.title" 奇巧,
            // "ETHEREAL.title" 虚无, "SHARP.title" 锋利, "NIMBLE.title" 灵巧,
            // "IMBUED.title" 注能), not from memory - see the terminology
            // glossary's L28 rule. Note 奇巧 is the base game's Sly, which
            // differs from a literal reading.
            ("retain_hand_card", _, "self") => $"使手牌中最多{amount}张牌获得保留",
            ("sly_hand_card", _, "self") => $"使手牌中最多{amount}张牌获得奇巧",
            ("ethereal_hand_card", _, "self") => $"使手牌中最多{amount}张牌获得虚无",
            ("enchant_hand", "sharp", "self") => $"使手牌中最多{amount}张攻击牌获得锋利附魔",
            ("enchant_hand", "nimble", "self") => $"使手牌中最多{amount}张可获得格挡的牌获得灵巧附魔",
            ("enchant_hand", "imbued", "self") => $"使手牌中最多{amount}张技能牌获得注能附魔",
            ("enchant_deck", "sharp", "self") => $"使牌堆中1张随机攻击牌获得{amount}层锋利附魔",
            ("enchant_deck", "nimble", "self") => $"使牌堆中1张随机可获得格挡的牌获得{amount}层灵巧附魔",
            ("enchant_deck", "imbued", "self") => $"使牌堆中1张随机技能牌获得{amount}层注能附魔",
            _ => throw new InvalidOperationException($"no ZHS text for effect {effect.Opcode}/{effect.Variant}/{effect.Target}"),
        };
    }

    /// <summary>
    /// One source relic's name morpheme. Both fields are SUBSTRINGS of the
    /// source relic's official title (verified against the engine's own
    /// localization dump), so a reader who knows the source relic recognises it
    /// in the generated name - which is the whole point of the scheme.
    /// </summary>
    public sealed record NameMorpheme(string Source, string En, string Zhs);

    /// <summary>
    /// Source relic -> name morpheme, for every source in the ledger's
    /// supported atom set. This is the AAR equivalent of the original
    /// Auto-Anthonyology's hand-authored `NameParts` / `ChineseSplitOverrides`
    /// tables: the original splits each card's official name into chunks and
    /// recombines two of them, so the generated name is visibly built from the
    /// source cards' own words. Here each entry is one source relic's
    /// distinguishing word, taken from its official title in BOTH languages.
    ///
    /// Coverage is asserted by <see cref="Morpheme"/>: a source with no entry
    /// throws rather than silently falling back to invented text. The set is
    /// closed (it is exactly the ledger's supported provenance), so this is a
    /// data-completeness check, not a runtime branch.
    /// </summary>
    private static readonly NameMorpheme[] Morphemes =
    {
        new("Akabeko", "Akabeko", "赤牛"),
        new("Anchor", "Anchor", "锚"),
        new("BagOfMarbles", "Marbles", "弹珠"),
        new("BagOfPreparation", "Preparation", "背包"),
        new("BigMushroom", "Mushroom", "蘑菇"),
        new("BlessedAntler", "Antler", "鹿角"),
        new("BloodSoakedRose", "Rose", "玫瑰"),
        new("BloodVial", "Vial", "血瓶"),
        new("BronzeScales", "Scales", "鳞片"),
        new("CallingBell", "Bell", "铃铛"),
        new("CaptainsWheel", "Wheel", "舵盘"),
        new("ChosenCheese", "Cheese", "芝士"),
        new("CursedPearl", "Pearl", "珍珠"),
        new("DragonFruit", "Dragon", "火龙果"),
        new("Ectoplasm", "Ectoplasm", "外质"),
        new("Fiddle", "Fiddle", "提琴"),
        new("FragrantMushroom", "Fragrant", "芳香"),
        new("GamePiece", "Piece", "棋子"),
        new("Glitter", "Glitter", "亮片"),
        new("GremlinHorn", "Gremlin", "地精"),
        new("HornCleat", "Cleat", "夹板"),
        new("Lantern", "Lantern", "灯笼"),
        new("LeafyPoultice", "Poultice", "药膏"),
        new("Mango", "Mango", "芒果"),
        new("MeatOnTheBone", "Bone", "骨肉"),
        new("OldCoin", "Coin", "钱币"),
        new("PaelsEye", "Pael", "佩尔"),
        new("PhialHolster", "Holster", "皮套"),
        new("PhilosophersStone", "Stone", "贤者"),
        new("PotionBelt", "Belt", "腰带"),
        new("PrecariousShears", "Shears", "羊毛剪"),
        new("PreservedFog", "Fog", "活雾"),
        new("PrismaticGem", "Prism", "棱彩"),
        new("RippleBasin", "Basin", "水盆"),
        new("RoyalPoison", "Poison", "猛毒"),
        new("RunicPyramid", "Pyramid", "金字塔"),
        new("ScreamingFlagon", "Flagon", "酒壶"),
        new("SealOfGold", "Seal", "金印"),
        new("SereTalon", "Talon", "爪"),
        new("SilkenTress", "Tress", "发束"),
        new("Sozu", "Sozu", "添水"),
        new("SpikedGauntlets", "Gauntlet", "手甲"),
        new("Strawberry", "Strawberry", "草莓"),
        new("Vajra", "Vajra", "金刚杵"),
        new("VelvetChoker", "Choker", "颈圈"),
        // Extra pool (user order 2026-09-18), ported from QuriousCraftingRelics.
        // These sources are card-level affixes with no source RELIC, so unlike
        // the entries above the word is the base game's own official name for
        // the keyword/enchantment it applies (SlayTheSpire2.pck loc dump:
        // RETAIN 保留, SLY 奇巧, ETHEREAL 虚无, SHARP 锋利, NIMBLE 灵巧,
        // IMBUED 注能) rather than a substring of a relic title. That keeps the
        // table's invariant - every morpheme is a real word the player already
        // sees in this game, in both languages - which is what Morpheme()
        // failing loud is protecting.
        new("QuriousHandRetain", "Retain", "保留"),
        new("QuriousHandSly", "Sly", "奇巧"),
        new("QuriousHandEthereal", "Ethereal", "虚无"),
        new("QuriousEnchantSharp", "Sharp", "锋利"),
        new("QuriousEnchantNimble", "Nimble", "灵巧"),
        new("QuriousEnchantImbued", "Imbued", "注能"),
        new("QuriousPickupSharp", "Whetstone", "磨石"),
        new("QuriousPickupNimble", "Footwork", "步法"),
        new("QuriousPickupImbued", "Sigil", "刻印"),
    };

    private static readonly Dictionary<string, NameMorpheme> MorphemeBySource = BuildMorphemeIndex();

    /// <summary>
    /// Every source relic this table covers, ordinal-sorted. Used as the floor
    /// of the name pool (see RelicGenerator.PickName): the pool must never be
    /// empty, so every known source is always a candidate at low weight.
    /// </summary>
    public static IReadOnlyList<string> AllSources { get; } = BuildSourceList();

    private static IReadOnlyList<string> BuildSourceList()
    {
        var sources = new List<string>(Morphemes.Length);
        foreach (NameMorpheme morpheme in Morphemes)
        {
            sources.Add(morpheme.Source);
        }
        sources.Sort(StringComparer.Ordinal);
        return sources;
    }

    private static Dictionary<string, NameMorpheme> BuildMorphemeIndex()
    {
        var index = new Dictionary<string, NameMorpheme>(StringComparer.Ordinal);
        foreach (NameMorpheme morpheme in Morphemes)
        {
            index.Add(morpheme.Source, morpheme);
        }
        return index;
    }

    /// <summary>
    /// The morpheme of one source relic. <paramref name="atomId"/> is an atom id
    /// (<c>Akabeko#AfterSideTurnStart#0</c>); the source is the part before the
    /// first '#', which is how every other provenance consumer in the mod reads
    /// it.
    /// </summary>
    public static NameMorpheme Morpheme(string atomId)
    {
        int hash = atomId.IndexOf('#');
        string source = hash < 0 ? atomId : atomId[..hash];
        if (!MorphemeBySource.TryGetValue(source, out NameMorpheme? morpheme))
        {
            // Fail loud rather than invent: an unmapped source means the ledger
            // gained provenance this table has not been extended for.
            throw new InvalidOperationException(
                $"no name morpheme for source relic '{source}' (atom '{atomId}'); add it to RelicText.Morphemes");
        }
        return morpheme;
    }

    /// <summary>
    /// Compose a relic name from two source morphemes. English joins with a
    /// space and keeps each morpheme's own casing; Chinese concatenates, since
    /// the language has no word separator (the original's ComposeChinese does
    /// the same).
    /// </summary>
    public static string ComposeEn(string stem, string tail) => $"{stem} {tail}";

    public static string ComposeZhs(string stem, string tail) => stem + tail;

    public const string FlavorEn = "The algorithm insists this is a relic.";
    public const string FlavorZhs = "算法坚持说这是一件遗物.";
}
