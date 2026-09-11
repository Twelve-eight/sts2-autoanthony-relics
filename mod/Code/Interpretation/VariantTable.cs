using AutoAnthonyRelics.Generation;

namespace AutoAnthonyRelics.Interpretation;

/// <summary>
/// What the interpreter knows about one (opcode, variant) pair.
///
/// The three values are NOT a scale of "how done" - they are three different
/// answers, and the distinction between the last two is the whole point of
/// having a table at all:
///
///   Implemented      we resolve it ourselves (slices 1+2 here);
///   DelegatedToNative the original hands the fragment to the engine's own card
///                    model; re-implementing it would be inventing a second
///                    source of truth, so we hand off too;
///   Pending          we recognise it, it is in the pool, and we have not
///                    written its handler yet.
///
/// A pair that is in NONE of those sets is not "Pending" - it is unrecognised,
/// and Classify throws for it (UnclassifiedVariantException). Collapsing
/// "not written yet" into "not recognised" is exactly the failure mode this
/// table exists to prevent.
/// </summary>
public enum VariantClassification
{
    Implemented,
    DelegatedToNative,
    Pending,
}

/// <summary>Classification census over the pool.</summary>
public sealed record VariantCounts(int Implemented, int DelegatedToNative, int Pending)
{
    public int Total => Implemented + DelegatedToNative + Pending;

    public override string ToString() =>
        $"implemented={Implemented} delegated={DelegatedToNative} pending={Pending} total={Total}";
}

/// <summary>
/// Stage C's variant classification, exhaustive over the pool.
///
/// STRUCTURE
/// ---------
///   * <see cref="PoolPairList"/> is a FROZEN MANIFEST of every (opcode, variant)
///     pair in the pool (306 of them, measured over the two embedded JSON files).
///     It is written out literally rather than derived at load time so that a
///     data revision cannot silently reclassify anything: the probe asserts the
///     manifest equals the catalog's pair set exactly, so drift fails loudly.
///   * <see cref="DelegatedSet"/> and <see cref="ImplementedSet"/> are the two
///     explicit, hand-written classification sets.
///   * Pending is therefore not a default branch: it is "in the manifest and in
///     neither explicit set". A pair outside the manifest throws.
///
/// That is what lets the probe assert zero unknown fallthrough rather than
/// merely counting what happened to be hit.
/// </summary>
internal static class VariantTable
{
    public static string Key(string opcode, string variant) => opcode + "|" + variant;

    /// <summary>
    /// The 25 <c>*_proxyatomic_*</c> variants: the original's
    /// ChaosOperationExecutor.GeneratedValueProxyTemplates hand-off list
    /// (ChaosOperationExecutor.cs:47-52). 19 live under
    /// template_independent_action and 6 under combat_rule - which matches the
    /// implementation-surface count in research/step4-implementation-plan.md
    /// section 1.1 exactly.
    /// </summary>
    private static readonly HashSet<string> DelegatedSet = new(StringComparer.Ordinal)
    {
        "combat_rule|a_proxyatomic_buffer",
        "combat_rule|a_proxyatomic_calcify",
        "combat_rule|a_proxyatomic_forbiddengrimoire",
        "combat_rule|a_proxyatomic_parry",
        "combat_rule|a_proxyatomic_royalties",
        "combat_rule|a_proxyatomic_swordsage",
        "template_independent_action|cl_proxyatomic_alchemize",
        "template_independent_action|cl_proxyatomic_anointed",
        "template_independent_action|cl_proxyatomic_beatdown",
        "template_independent_action|cl_proxyatomic_catastrophe",
        "template_independent_action|cl_proxyatomic_hiddengem",
        "template_independent_action|i_proxyatomic_begone",
        "template_independent_action|i_proxyatomic_charge",
        "template_independent_action|i_proxyatomic_doubleenergy",
        "template_independent_action|i_proxyatomic_dredge",
        "template_independent_action|i_proxyatomic_eidolon",
        "template_independent_action|i_proxyatomic_foregoneconclusion",
        "template_independent_action|i_proxyatomic_guards",
        "template_independent_action|i_proxyatomic_multicast",
        "template_independent_action|i_proxyatomic_seance",
        "template_independent_action|i_proxyatomic_signalboost",
        "template_independent_action|i_proxyatomic_tempest",
        "template_independent_action|i_proxyatomic_transfigure",
        "template_independent_action|i_proxyatomic_voltaic",
        "template_independent_action|i_proxyatomic_whitenoise",
    };

    /// <summary>
    /// Stage C slices 1+2. Slice 1 is the eight effects that let a card deal
    /// damage, block, and pay out; slice 2 is the triggerIndex dispatch itself
    /// (trigger hosts + condition gates).
    ///
    /// Scope note for the two entries that look missing but are deliberate:
    ///   * apply_power|poison is NOT here: the original does not handle it in
    ///     either structured apply_power route (its variant length 6 falls
    ///     through the dispatch at ChaosOperationExecutor.cs:1056-1192); it is
    ///     carried by the N:RandomPoison template handler instead, which is
    ///     slice 3 work. Implementing it here would be UNfaithful.
    ///   * apply_power|focus_loss_this_turn is NOT here: the original routes it
    ///     to ChaosTemporaryFocusDownPower, a mod-defined power. The engine has
    ///     no general-purpose temporary focus-down power (HyperbeamFocusDownPower
    ///     is bound to one card), so this needs a power of our own - slice 3.
    /// </summary>
    private static readonly HashSet<string> ImplementedSet = new(StringComparer.Ordinal)
    {
        // ---- slice 1: damage / block / draw / resources --------------------
        "deal_damage|selected",
        "deal_damage|all",
        "deal_damage|random",
        "gain_block|immediate",
        "draw_cards|immediate",
        "gain_energy|immediate",
        "gain_stars|immediate",
        "lose_hp|immediate",
        "heal|immediate",

        // ---- slice 1: apply_power, the StructuredSelfPowerRoute set --------
        // (ChaosOperationExecutor.cs:1272-1308; requires spec.Target == "self")
        "apply_power|strength",
        "apply_power|strength_this_turn",
        "apply_power|strength_loss",
        "apply_power|strength_loss_this_turn",
        "apply_power|strength_per_target_vulnerable",
        "apply_power|dexterity_gain",
        "apply_power|dexterity_loss",
        "apply_power|doom",
        "apply_power|focus_loss",
        "apply_power|plating",
        "apply_power|vigor",

        // ---- slice 1: apply_power, the common switch set -------------------
        // (ChaosOperationExecutor.cs:1055-1191)
        "apply_power|strength_gain",
        "apply_power|vulnerable",
        "apply_power|vulnerable_double",
        "apply_power|weak",
        "apply_power|retain_hand_this_turn",

        // ---- slice 2: triggerIndex dispatch --------------------------------
        "trigger|event",
        "condition|card_exhausted_this_turn",
        "condition|cards_played_this_turn_below",
        "condition|doom_applied_this_turn",
        "condition|draw_pile_empty",
        "condition|enemy_intends_attack",
        "condition|exhaust_pile_minimum",
        "condition|fatal",
        "condition|first_play_of_this_card_this_turn",
        "condition|hand_empty",
        "condition|last_drawn_card_is_skill",
        "condition|no_attacks_in_hand",
        "condition|osty_alive",
        "condition|osty_attacked_this_turn",
        "condition|owner_lost_hp_this_turn",
        "condition|target_has_poison",
        "condition|target_has_vulnerable",
    };

    /// <summary>
    /// Why a pending pair is pending, for the pairs where the reason is not
    /// obvious from the opcode alone. Purely diagnostic - the probe prints it.
    /// </summary>
    private static readonly Dictionary<string, string> PendingNotes = new(StringComparer.Ordinal)
    {
        ["apply_power|poison"] =
            "routed outside the structured apply_power dispatch (N:RandomPoison template handler)",
        ["apply_power|focus_loss_this_turn"] =
            "needs a temporary focus-down power of our own; the engine's only one is card-bound",
        ["deal_damage|cards_played_combat"] =
            "damage is the combat card-play count, read from CombatHistory (ChaosOperationExecutor.cs:1317-1320)",
        ["deal_damage|selected_energy_x_hits"] =
            "needs the resolved energy-X value, which the generator does not carry",
        ["deal_damage|selected_energy_x_threshold"] =
            "needs the resolved energy-X value, which the generator does not carry",
        ["deal_damage|random_star_x_hits"] =
            "needs the resolved star-X value, which the generator does not carry",
        ["trigger|doom_threshold"] =
            "scope Modifier (a dependency prefix), not a carried trigger host; modifier work",
        ["condition|has_frost_orb"] =
            "scope Modifier; the original evaluates it in DependencyConditionMatches, not ConditionMatches",
        ["condition|cards_played_this_turn_at_least"] =
            "scope Modifier; dependency-prefix work, not ConditionMatches",
    };

    /// <summary>
    /// Frozen manifest of every (opcode, variant) pair in the pool: 33 opcodes,
    /// 291 distinct variants, 306 pairs (research/step4-implementation-plan.md
    /// section 1.1). Sorted, so a diff against a regenerated list is readable.
    /// </summary>
    private static readonly string[] PoolPairList =
    {
        "apply_power|dexterity_gain",
        "apply_power|dexterity_loss",
        "apply_power|doom",
        "apply_power|focus_loss",
        "apply_power|focus_loss_this_turn",
        "apply_power|plating",
        "apply_power|poison",
        "apply_power|retain_hand_this_turn",
        "apply_power|strength",
        "apply_power|strength_gain",
        "apply_power|strength_loss",
        "apply_power|strength_loss_this_turn",
        "apply_power|strength_per_target_vulnerable",
        "apply_power|strength_this_turn",
        "apply_power|vigor",
        "apply_power|vulnerable",
        "apply_power|vulnerable_double",
        "apply_power|weak",
        "choose_generated_card|random_colorless",
        "choose_generated_card|random_current_character",
        "choose_generated_card|random_other_character_attack",
        "combat_rule|a_proxyatomic_buffer",
        "combat_rule|a_proxyatomic_calcify",
        "combat_rule|a_proxyatomic_forbiddengrimoire",
        "combat_rule|a_proxyatomic_parry",
        "combat_rule|a_proxyatomic_royalties",
        "combat_rule|a_proxyatomic_swordsage",
        "combat_rule|derivative_bonus_damage",
        "combat_rule|derivative_hits_all",
        "combat_rule|derivative_retain",
        "combat_rule|die_on_unblocked_attack",
        "combat_rule|first_card_block_doubled_each_turn",
        "combat_rule|first_cards_free_each_turn",
        "combat_rule|first_derivative_bonus_damage",
        "combat_rule|kings_sword_hits_all",
        "combat_rule|played_skills_gain_sly",
        "combat_rule|poison_extra_triggers",
        "combat_rule|retain_block_between_turns",
        "combat_rule|retain_hand_at_turn_end",
        "combat_rule|skills_cost_zero",
        "combat_rule|vulnerable_enemy_damage_bonus",
        "combat_rule|weak_enemy_attack_damage_bonus",
        "condition|card_exhausted_this_turn",
        "condition|cards_played_this_turn_at_least",
        "condition|cards_played_this_turn_below",
        "condition|doom_applied_this_turn",
        "condition|draw_pile_empty",
        "condition|enemy_intends_attack",
        "condition|exhaust_pile_minimum",
        "condition|fatal",
        "condition|first_play_of_this_card_this_turn",
        "condition|hand_empty",
        "condition|has_frost_orb",
        "condition|last_drawn_card_is_skill",
        "condition|no_attacks_in_hand",
        "condition|osty_alive",
        "condition|osty_attacked_this_turn",
        "condition|owner_lost_hp_this_turn",
        "condition|target_has_poison",
        "condition|target_has_vulnerable",
        "create_card|current_character_random",
        "create_card|random_attack_zero_cost_this_turn",
        "create_card|random_colorless",
        "create_card|random_zero_cost",
        "create_copy|referenced_card",
        "create_copy|this_card",
        "deal_damage|all",
        "deal_damage|cards_played_combat",
        "deal_damage|random",
        "deal_damage|random_star_x_hits",
        "deal_damage|selected",
        "deal_damage|selected_energy_x_hits",
        "deal_damage|selected_energy_x_threshold",
        "discard_card|all",
        "discard_card|selected",
        "draw_and_discard|nonzero_cost",
        "draw_cards|immediate",
        "end_turn|after_card_resolution",
        "exhaust_card|all",
        "exhaust_card|random",
        "exhaust_card|referenced",
        "exhaust_card|selected",
        "gain_block|immediate",
        "gain_energy|immediate",
        "gain_max_hp|immediate",
        "gain_stars|immediate",
        "heal|immediate",
        "lose_hp|immediate",
        "modify_block|strength_scaled",
        "modify_cost|set_zero",
        "modify_damage|current_block",
        "modify_damage|strike_count_scaled",
        "modify_damage|triggered_attack_percentage",
        "modify_damage|vulnerable_scaled",
        "modify_hits|flat_extra",
        "modify_hits|hp_loss_scaled",
        "modify_orb_slots|loss",
        "modify_power|doom_per_threshold",
        "modify_x|double_at_threshold",
        "move_card|random",
        "move_card|selected",
        "restrict_block_from_cards|next_n_turns",
        "template_independent_action|cl_drawtofullhand",
        "template_independent_action|cl_exhaustuptohandcards",
        "template_independent_action|cl_gainnextturnblockequalcurrent",
        "template_independent_action|cl_moveselectedattackdrawtohand",
        "template_independent_action|cl_moveselectedskilldrawtohand",
        "template_independent_action|cl_proxyatomic_alchemize",
        "template_independent_action|cl_proxyatomic_anointed",
        "template_independent_action|cl_proxyatomic_beatdown",
        "template_independent_action|cl_proxyatomic_catastrophe",
        "template_independent_action|cl_proxyatomic_hiddengem",
        "template_independent_action|cl_puteventcardondrawtop",
        "template_independent_action|cl_putselectedhandcardondrawtop",
        "template_independent_action|cl_returnthistohand",
        "template_independent_action|d_costdownwhenstatusgenerated",
        "template_independent_action|d_increaseallclaws",
        "template_independent_action|d_increasethiscardblockrun",
        "template_independent_action|d_increasethiscardcost",
        "template_independent_action|d_replayeventcard",
        "template_independent_action|d_returneventcardtohand",
        "template_independent_action|d_setthiscardcostzero",
        "template_independent_action|i_addcardreward",
        "template_independent_action|i_addexhaustedattackdamage",
        "template_independent_action|i_applytoallenemies",
        "template_independent_action|i_autoplayrandomattackfromhand",
        "template_independent_action|i_copyselectedcardnextturn",
        "template_independent_action|i_discardhanddrawsame",
        "template_independent_action|i_doubleattackdamagenextturn",
        "template_independent_action|i_doubleblockthisturn",
        "template_independent_action|i_drawuntilnonattack",
        "template_independent_action|i_drawwithretain",
        "template_independent_action|i_exhaustrandomattack",
        "template_independent_action|i_freehandthisturn",
        "template_independent_action|i_grantslytohandskillthisturn",
        "template_independent_action|i_increasedamagethiscombat",
        "template_independent_action|i_playatrandomenemy",
        "template_independent_action|i_playexhaustedshivsattarget",
        "template_independent_action|i_playthiscard",
        "template_independent_action|i_playtopcardandexhaust",
        "template_independent_action|i_playtopxcards",
        "template_independent_action|i_preventdrawthisturn",
        "template_independent_action|i_proxyatomic_begone",
        "template_independent_action|i_proxyatomic_charge",
        "template_independent_action|i_proxyatomic_doubleenergy",
        "template_independent_action|i_proxyatomic_dredge",
        "template_independent_action|i_proxyatomic_eidolon",
        "template_independent_action|i_proxyatomic_foregoneconclusion",
        "template_independent_action|i_proxyatomic_guards",
        "template_independent_action|i_proxyatomic_multicast",
        "template_independent_action|i_proxyatomic_seance",
        "template_independent_action|i_proxyatomic_signalboost",
        "template_independent_action|i_proxyatomic_tempest",
        "template_independent_action|i_proxyatomic_transfigure",
        "template_independent_action|i_proxyatomic_voltaic",
        "template_independent_action|i_proxyatomic_whitenoise",
        "template_independent_action|i_reducethiscardcostcombat",
        "template_independent_action|i_replayattack",
        "template_independent_action|i_replaynextskills",
        "template_independent_action|i_transform",
        "template_independent_action|i_triggerpoisonnow",
        "template_independent_action|i_upgrade",
        "template_independent_action|ncr_costdownwhencreaturedies",
        "template_independent_action|ncr_increasethiscarddamagerun",
        "template_independent_action|ncr_returnfromdiscardonhighcostplay",
        "template_independent_action|ncr_setcostzeroifostyattacked",
        "template_independent_action|r_costdownwhendrawn",
        "template_independent_action|r_playatturnendiftopofdraw",
        "template_independent_action|r_playthiscard",
        "template_independent_action|r_putthisondraw",
        "template_independent_action|r_returnafterskillsplayed",
        "template_independent_action|r_returnthistohand",
        "template_modifier|cl_bonusperuniquedebuff",
        "template_modifier|cl_foreachdrawpilecard",
        "template_modifier|cl_increaserollingdamage",
        "template_modifier|d_foreachenemy",
        "template_modifier|d_foreachorb",
        "template_modifier|d_foreachuniqueorb",
        "template_modifier|d_repeatperorb",
        "template_modifier|d_wheneverstatusgenerated",
        "template_modifier|m_damageminuspercardinhand",
        "template_modifier|m_damagepercarddrawncombat",
        "template_modifier|m_damageperdiscardthisturn",
        "template_modifier|m_damageperexhaustcard",
        "template_modifier|m_repeatareaonkill",
        "template_modifier|m_repeatperattackthisturn",
        "template_modifier|m_repeatperskillinhand",
        "template_modifier|ncr_costdownpervoidplayed",
        "template_modifier|ncr_damagepercarddrawnthisturn",
        "template_modifier|ncr_damageperexhaustedsoul",
        "template_modifier|ncr_damageperostyattackcard",
        "template_modifier|ncr_foreachcarddrawnthisturn",
        "template_modifier|ncr_foreachetherealplayedcombat",
        "template_modifier|ncr_foreachexhaustedsoul",
        "template_modifier|ncr_foreachostyattackcard",
        "template_modifier|ncr_foreachostyattackthisturn",
        "template_modifier|ncr_ostycurrenthpbonusdamage",
        "template_modifier|ncr_ostymaxhpbonusdamage",
        "template_modifier|ncr_repeatperostyattackthisturn",
        "template_modifier|ncr_repeatpervoidplayedcombat",
        "template_modifier|ncr_whenevercreaturedies",
        "template_modifier|ncr_wheneverostyattackstargetthisturn",
        "template_modifier|r_atturnendwhentopofdraw",
        "template_modifier|r_bonuspergeneratedcardthiscombat",
        "template_modifier|r_bonusperstarcostcardinhand",
        "template_modifier|r_damageupwhendrawn",
        "template_modifier|r_foreachgeneratedcardcombat",
        "template_modifier|r_foreachpriorattackhitontarget",
        "template_modifier|r_foreachskillplayedthisturn",
        "template_modifier|r_foreachstarcostcard",
        "template_modifier|r_foreachstargainedthisturn",
        "template_modifier|r_repeatperskillplayedthisturn",
        "template_modifier|r_repeatperstargainedthisturn",
        "template_modifier|r_wheneverdrawn",
        "template_self_action|cl_addrandomattacktohand",
        "template_self_action|cl_choosefromrandomdrawcards",
        "template_self_action|cl_damageotherenemiesequal",
        "template_self_action|cl_gainblockequaldamage",
        "template_self_action|cl_gaingold",
        "template_self_action|cl_playtopdrawcard",
        "template_self_action|cl_transformselectedhandcards",
        "template_self_action|d_addrandompowertohand",
        "template_self_action|d_autoplayrandomattackfromdraw",
        "template_self_action|d_channeldark",
        "template_self_action|d_channelfrost",
        "template_self_action|d_channelglass",
        "template_self_action|d_channellightning",
        "template_self_action|d_channelplasma",
        "template_self_action|d_channelrandom",
        "template_self_action|d_createburnindiscard",
        "template_self_action|d_createdazedindiscard",
        "template_self_action|d_createslimeindiscard",
        "template_self_action|d_createtwowoundsindiscard",
        "template_self_action|d_createvoidindiscard",
        "template_self_action|d_createzerocostcopyindiscard",
        "template_self_action|d_evokealltwice",
        "template_self_action|d_evokeleftmostorb",
        "template_self_action|d_evokerightmostorb",
        "template_self_action|d_exhaustallstatuses",
        "template_self_action|d_gainfocus",
        "template_self_action|d_gainorbslots",
        "template_self_action|d_gaintemporaryfocus",
        "template_self_action|d_returnzerocostdiscardtohand",
        "template_self_action|d_shuffleallunexhaustedintodraw",
        "template_self_action|d_transformstatusestofuel",
        "template_self_action|d_triggerdarkpassives",
        "template_self_action|d_triggerrightmostorbpassive",
        "template_self_action|n_allpoison",
        "template_self_action|n_blockequalallpoison",
        "template_self_action|n_createinkshiv",
        "template_self_action|n_createshiv",
        "template_self_action|n_intangible",
        "template_self_action|n_keepblocknextturn",
        "template_self_action|n_nextturndraw",
        "template_self_action|n_tempdex",
        "template_self_action|n_thorns",
        "template_self_action|ncr_addrandometherealcardtohand",
        "template_self_action|ncr_addretaintoselectedhandcard",
        "template_self_action|ncr_addsweepinggazetohand",
        "template_self_action|ncr_addvoidtoselectedhandcard",
        "template_self_action|ncr_allenemiesloseeventhp",
        "template_self_action|ncr_applydoomall",
        "template_self_action|ncr_blocktripleostymaxhp",
        "template_self_action|ncr_createsoulindiscard",
        "template_self_action|ncr_createsoulindraw",
        "template_self_action|ncr_createsoulindrawx",
        "template_self_action|ncr_createsoulinhand",
        "template_self_action|ncr_healosty",
        "template_self_action|ncr_increaseallcardcoststhisturn",
        "template_self_action|ncr_killenemiesatdoomthreshold",
        "template_self_action|ncr_killosty",
        "template_self_action|ncr_ostyalldamage",
        "template_self_action|ncr_summon",
        "template_self_action|ncr_summonx",
        "template_self_action|ncr_upgraderandomdiscardcards",
        "template_self_action|r_adddebristohand",
        "template_self_action|r_copyselectedcolorlesscard",
        "template_self_action|r_enemieslosestrength",
        "template_self_action|r_fillhandwithdebris",
        "template_self_action|r_forge",
        "template_self_action|r_movediscardcardtodrawtop",
        "template_self_action|r_playselectedskillmultipletimes",
        "template_self_action|r_putkingsswordinhand",
        "template_self_action|r_putselectedhandcardondraw",
        "template_self_action|r_putselectedhandcardsondraw",
        "template_self_action|r_reflectblockeddamagethisturn",
        "template_target_action|d_triggerlightningpassivesattarget",
        "template_target_action|ncr_applydoom",
        "template_target_action|ncr_applydoomequaldamage",
        "template_target_action|ncr_applyeventdamageasdoom",
        "template_target_action|ncr_applypower_sicempower",
        "template_target_action|ncr_copytargetdebuffstoothers",
        "template_target_action|ncr_doomscaleddamage",
        "template_target_action|ncr_doublehangdamage",
        "template_target_action|ncr_doublevulnerableweak",
        "template_target_action|ncr_ostydamage",
        "template_target_action|ncr_targetlosestrength",
        "template_target_action|ncr_unpowereddamage",
        "template_target_action|r_kingssworddoubledamagethisturn",
        "template_target_action|t_poison",
        "template_target_action|t_removeblockandartifact",
        "template_target_action|t_xstrengthloss",
        "template_target_action|t_xweak",
        "trigger|doom_threshold",
        "trigger|event",
        "upgrade_card|referenced",
    };

    private static readonly HashSet<string> PoolPairSet = new(PoolPairList, StringComparer.Ordinal);

    /// <summary>Every pair in the frozen manifest, sorted.</summary>
    public static IReadOnlyList<string> PoolPairs => PoolPairList;

    public static bool IsPoolPair(string opcode, string variant) =>
        PoolPairSet.Contains(Key(opcode, variant));

    /// <summary>
    /// Total over the pool: every manifest pair gets one of the three answers, and
    /// anything outside the manifest throws rather than being guessed at.
    /// </summary>
    public static VariantClassification Classify(string opcode, string variant)
    {
        string key = Key(opcode, variant);
        if (!PoolPairSet.Contains(key))
        {
            throw new UnclassifiedVariantException(opcode, variant);
        }
        return ClassifyKey(key);
    }

    public static VariantClassification Classify(GeneratedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Classify(operation.Spec.Opcode, operation.Spec.Variant);
    }

    /// <summary>Human-readable reason a pair is still pending, or a generic one.</summary>
    public static string PendingNote(string opcode, string variant) =>
        PendingNotes.TryGetValue(Key(opcode, variant), out string? note)
            ? note
            : "handler not written yet (slice 3+)";

    public static VariantCounts Counts()
    {
        int implemented = 0;
        int delegated = 0;
        int pending = 0;
        foreach (string key in PoolPairList)
        {
            switch (ClassifyKey(key))
            {
                case VariantClassification.Implemented:
                    implemented++;
                    break;
                case VariantClassification.DelegatedToNative:
                    delegated++;
                    break;
                default:
                    pending++;
                    break;
            }
        }
        return new VariantCounts(implemented, delegated, pending);
    }

    private static VariantClassification ClassifyKey(string key)
    {
        if (DelegatedSet.Contains(key))
        {
            return VariantClassification.DelegatedToNative;
        }
        return ImplementedSet.Contains(key) ? VariantClassification.Implemented : VariantClassification.Pending;
    }
}