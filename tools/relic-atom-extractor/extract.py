"""Extract recombineable effect atoms from the engine's relic classes.

The atom model mirrors the original AutoAnthony pool on purpose
(opcode + variant + target + values + optional condition + optional trigger)
so that the generation/interpreter machinery built for cards keeps working:
only the DATA changes, not the recombination algorithm.

What counts as an atom:
  one (trigger | modifier hook) x one engine command x its numeric value,
  optionally gated by a recognisable condition.

Relics we deliberately REFUSE to atomise (marked GenerationEligible=false):
  * bodies using [SavedProperty]  - they carry per-run state the atom model
    cannot express, and recombining them would silently drop that state;
  * bodies we cannot reduce to a known command (custom card transforms,
    reward screens) - inventing an opcode for them would be inventing
    semantics.

Refusing is the honest outcome. A wrong atom is worse than no atom.

POLICY REVERSAL (user order 2026-09-17): the Ancient relic QUERY hooks are
now atomised (see QUERY_HOOKS). The earlier refusal covered "potion
procurement" by name; that is reversed because ShouldProcurePotion and its
siblings are named engine hooks whose bodies return one fully determined
answer, so their opcode is the hook's own name rather than an invented
semantics. The reversal is recorded in DEVELOP.md, not silently applied.

Usage: python extract.py [--out FILE] [--report]
"""
import json
import os
import re
import sys
from collections import Counter

SRC = r"G:/omp works/Sts/sts2-spire1/research/engine-dllsrc/MegaCrit.Sts2.Core.Models.Relics"

# --- event hook -> trigger kind -------------------------------------------
# Only hooks that can fire repeatedly (or once) and carry a self-contained
# effect are mapped. Hooks whose semantics are "you own this, do bookkeeping"
# (AfterObtained) are mapped too but flagged, because they fire once.
HOOK_TRIGGER = {
    "BeforeCombatStart": ("combat_start", "immediate"),
    "BeforeCombatStartLate": ("combat_start", "immediate"),
    "AfterCombatEnd": ("combat_end", "immediate"),
    "AfterCombatVictory": ("combat_victory", "immediate"),
    "AfterCombatVictoryEarly": ("combat_victory", "immediate"),
    "AfterSideTurnStart": ("turn_start", "combat"),
    "BeforeSideTurnStart": ("turn_start", "combat"),
    "AfterPlayerTurnStart": ("turn_start", "combat"),
    "AfterPlayerTurnStartLate": ("turn_start", "combat"),
    "BeforeSideTurnEnd": ("turn_end", "combat"),
    "AfterSideTurnEnd": ("turn_end", "combat"),
    "BeforeSideTurnEndEarly": ("turn_end", "combat"),
    "BeforeSideTurnEndVeryEarly": ("turn_end", "combat"),
    "AfterCardPlayed": ("card_played", "combat"),
    "BeforeCardPlayed": ("card_played", "combat"),
    "AfterCardExhausted": ("card_exhausted", "combat"),
    "AfterCardDiscarded": ("card_discarded", "combat"),
    "AfterDamageReceived": ("damage_received", "combat"),
    "AfterAttack": ("attack", "combat"),
    "AfterBlockBroken": ("block_broken", "combat"),
    "AfterBlockCleared": ("block_cleared", "combat"),
    "AfterEnergyReset": ("turn_start", "combat"),
    "AfterEnergyResetLate": ("turn_start", "combat"),
    "AfterShuffle": ("shuffle", "combat"),
    "AfterPotionUsed": ("potion_used", "combat"),
    "AfterOrbChanneled": ("orb_channeled", "combat"),
    "AfterStarsSpent": ("stars_spent", "combat"),
    "AfterRoomEntered": ("room_entered", "immediate"),
    "AfterObtained": ("obtained", "immediate"),
    "AfterRestSiteHeal": ("rest_site_heal", "immediate"),
    "AfterHandEmptied": ("hand_emptied", "combat"),
    "AfterCurrentHpChanged": ("hp_changed", "combat"),
    "AfterFlush": ("turn_end", "combat"),
    "AfterCardChangedPiles": ("card_moved", "combat"),
    "AfterCardEnteredCombat": ("combat_start", "immediate"),
    "AfterCreatureAddedToCombat": ("creature_entered", "combat"),
    "AfterDeath": ("creature_died", "combat"),
    "AfterPreventingDeath": ("prevent_death", "combat"),
    "AfterGoldGained": ("gold_gained", "immediate"),
    "AfterItemPurchased": ("item_purchased", "immediate"),
    "AfterPotionProcured": ("potion_procured", "immediate"),
    "AfterPotionDiscarded": ("potion_discarded", "immediate"),
    "AfterDiedToDoom": ("doom_death", "combat"),
    "AfterTakingExtraTurn": ("extra_turn", "combat"),
}

# --- modifier hook -> opcode ----------------------------------------------
MODIFIER_OPCODE = {
    "ModifyHandDraw": "modify_hand_draw",
    "ModifyMaxEnergy": "modify_max_energy",
    "ModifyRestSiteHealAmount": "modify_rest_heal",
    "ModifyHpLostAfterOstyLate": "modify_osty_hp_loss",
    "ModifyCardPlayCount": "modify_card_play_count",
    "ModifyPowerAmountGivenAdditive": "modify_power_given",
    "ModifyPowerAmountGivenMultiplicative": "modify_power_given",
    "ModifyBlockAmount": "modify_block_amount",
    "ModifyDamage": "modify_damage",
}

# --- restriction / benefit hooks -> (opcode, variant) ---------------------
# POLICY REVERSAL (user order 2026-09-17). The module docstring above used to
# say that bodies reducible to no known command - it named potion procurement
# verbatim - are refused, because inventing an opcode for them would be
# inventing semantics. That decision is now reversed for the Ancient relic
# hooks below: each one IS a named engine hook with a body that reads as a
# single, fully determined answer, so the opcode is not invented, it is the
# hook's own name. The refusals that remain are the ones where the body
# genuinely cannot be reduced (custom transforms, RNG target selectors).
#
# These hooks are "may I?" queries or multiplicative modifiers rather than
# engine commands, so they contain no Cmd call and the command scanner above
# cannot see them. Values are still best-effort here; the ledger hand-verifies
# every emitted atom (value, polarity, target, condition).
#
# KEYED BY (relic, hook), NOT by hook alone. A hook name is not a semantics:
# TryModifyCardRewardOptionsLate has nine implementors in the engine
# (FresnelLens / FrozenEgg / MoltenEgg / ToxicEgg / WingCharm enchant a card
# type, SilverCrucible upgrades, Glitter / SilkenTress apply Glam), and
# ModifyCardRewardCreationOptions is implemented by non-Ancient relics with
# unrelated pool rules. A hook-name-keyed table would label all of them with
# one variant and bake the mislabels into the "reproducible" output.
#
# ModifyMaxEnergy / ModifyHandDraw / ModifyCardPlayCount are deliberately
# ABSENT: the modifier scanner above already emits them, and a second entry
# would double-count one hook.
QUERY_HOOKS = {
    # --- restriction-type downsides ---
    ("Ectoplasm", "ModifyGoldGained"): ("restrict_gold", "zero"),
    ("Sozu", "ShouldProcurePotion"): ("restrict_potion", "block"),
    ("VelvetChoker", "ShouldPlay"): ("restrict_card_play", "cap"),
    ("Fiddle", "ShouldDraw"): ("restrict_draw", "block"),
    ("PhilosophersStone", "AfterCreatureAddedToCombat"): ("enemy_strength_gain", "strength"),
    ("SpikedGauntlets", "TryModifyEnergyCostInCombat"): ("modify_card_cost", "power"),
    # --- pure benefits ---
    ("RunicPyramid", "ShouldFlush"): ("retain_hand", "always"),
    ("PaelsEye", "ShouldTakeExtraTurn"): ("extra_turn", "always"),
    ("PrismaticGem", "ModifyCardRewardCreationOptions"): ("expand_card_pool", "character"),
    ("Glitter", "TryModifyCardRewardOptionsLate"): ("enchant_reward", "glam"),
}

# Registered query hooks whose relic carries [SavedProperty] per-run counters.
# The atom model cannot express those counters (TreasureRoomsEntered, IsUsedUp,
# Cooldown, IsUsed, KindleCount), so emitting the hook as a state-free affix
# would promise behaviour the engine gates on state we do not track. They are
# named here so the exclusion is explicit and auditable rather than an
# accident of the saved-state drop.
QUERY_HOOKS_STATEFUL = {
    ("SilverCrucible", "ShouldGenerateTreasure"),
    ("WingedBoots", "ShouldAllowFreeTravel"),
    ("PaelsLegion", "ModifyBlockMultiplicative"),
    ("SilkenTress", "TryModifyCardRewardOptionsLate"),
    ("PumpkinCandle", "ModifyMaxEnergy"),
}

# --- engine command -> (opcode, variant resolver) -------------------------
# variant is resolved from the generic argument or the call shape.
POWER_KIND_RE = re.compile(r"PowerCmd\.Apply<(\w+)>")


def power_variant(power: str) -> tuple[str, str]:
    """Map an engine PowerModel name onto (opcode, variant)."""
    p = power
    if p.endswith("Power"):
        p = p[: -len("Power")]
    return "apply_power", p.lower()


def snake_case(name: str) -> str:
    """PascalCase type argument -> snake_case variant.

    CurseOfTheBell -> curse_of_the_bell, matching the variant names the
    executor's CurseTypes table keys on.
    """
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


# --- condition patterns ----------------------------------------------------
# Order matters: first match wins.
CONDITION_PATTERNS = [
    (r"TurnNumber\s*<=\s*1", "first_turn"),
    (r"TurnNumber\s*==\s*1", "first_turn"),
    (r"TurnNumber\s*>\s*1", "not_first_turn"),
    (r"TurnNumber\s*%\s*(\d+)", "every_n_turns"),
    (r"player\s*!=\s*base\.Owner", "owner_turn"),
    (r"IsDead", "target_dead"),
    (r"side\s*==\s*CombatSide\.Player", "player_side"),
    (r"side\s*!=\s*CombatSide\.Player", "enemy_side"),
    (r"Hp\s*<=\s*0", "low_hp"),
    (r"Cards\.Count\s*==\s*0", "hand_empty"),
    (r"Energy\s*==\s*0", "no_energy"),
]


def extract_condition(body: str) -> str | None:
    """Extract a gating condition, accounting for guard clauses.

    A relic that does `if (TurnNumber > 1) return count;` is NOT "active on
    turns after the first" - it is bailing out, so the effect only happens on
    turn 1. Reading the guard literally produced
    'BagOfPreparation ... if=not_first_turn', which is backwards.
    """
    # Guard clauses: an if whose body only returns/continues negates itself.
    guard = re.search(
        r"if\s*\([^)]*TurnNumber\s*>\s*(\d+)[^)]*\)\s*(?:\r?\n\s*)?\{\s*return[^;]*;", body)
    if guard:
        return "first_turn"
    guard_owner = re.search(
        r"if\s*\(\s*player\s*!=\s*base\.Owner\s*\)\s*(?:\r?\n\s*)?\{\s*return[^;]*;", body)
    if guard_owner:
        return "owner_turn"

    for pat, kind in CONDITION_PATTERNS:
        if re.search(pat, body):
            return kind
    return None


# --- numeric value ---------------------------------------------------------
VAR_RE = re.compile(r"DynamicVars(?:\.Get<[^>]+>|\[\"(\w+)\"\]|\.(\w+))")
# new MaxHpVar(20m) -> ("MaxHpVar", None, "20")
# new CardsVar(2)   -> ("CardsVar", None, "2")
# new PowerVar<VigorPower>(8m) -> ("PowerVar", "VigorPower", "8")
VAR_DECL_RE = re.compile(r"new (\w+Var)(?:<(\w+)>)?\s*\(\s*([\d.]+)")
POWER_VAR_RE = re.compile(r"new PowerVar<(\w+)>")


def extract_amount(cls_text: str, body: str) -> tuple[int, bool]:
    """Best-effort numeric value for the effect.

    Only ever reads from THIS method body. Falling back to a class-level
    DynamicVar is what produced 'BigMushroom modify_hand_draw amt=20' - the
    20 belongs to its max-HP var, not to the draw modifier. A wrong number is
    worse than no number, so when the body does not name one we report
    found=False and let the generator/substitute handle it.
    """
    # 1. A literal in the call itself, e.g. ..., 8m, ...
    m = re.search(r",\s*(\d+)m?\s*[,)]", body)
    if m:
        return int(m.group(1)), True
    # 2. A DynamicVar read inside this body, matched BY NAME against the class
    #    declaration. `DynamicVars.Cards` must resolve to `new CardsVar(2)`,
    #    not to whichever var happens to be declared first - BigMushroom
    #    declares MaxHpVar(20) then CardsVar(2), and its ModifyHandDraw wants 2.
    vm = re.search(r"DynamicVars(?:\.Get<[^>]+>|\.(\w+)|\[\"(\w+)\"\])", body)
    var_name = vm.group(1) or vm.group(2) if vm else None
    if var_name:
        wanted = var_name if var_name.endswith("Var") else var_name + "Var"
        for dm in VAR_DECL_RE.finditer(cls_text):
            decl = dm.group(1)  # e.g. "MaxHpVar", "CardsVar", "PowerVar"
            if decl == wanted or decl == var_name:
                try:
                    return int(float(dm.group(3))), True
                except ValueError:
                    pass
        # PowerVar<VigorPower>(8m): generic form, name comes from the type arg.
        for dm in VAR_DECL_RE.finditer(cls_text):
            if dm.group(1) == "PowerVar" and dm.group(2) == var_name:
                try:
                    return int(float(dm.group(3))), True
                except ValueError:
                    pass
    return 0, False


CLASS_RE = re.compile(r"public sealed class (\w+)")
RARITY_RE = re.compile(r"RelicRarity\.(\w+)")
METHOD_RE = re.compile(
    r"public override (?:async )?Task (\w+)\s*\(([^)]*)\)\s*\{", re.M)
MODIFIER_METHOD_RE = re.compile(
    r"public override (?:decimal|int|bool) (Modify\w+)\s*\(([^)]*)\)\s*\{", re.M)
# Query hooks return a decision or a replacement object and may be expression
# bodied; the return type varies (bool / int / decimal / ActMap /
# CardCreationOptions), so the name is the only stable anchor.
QUERY_METHOD_RE = re.compile(
    r"public override (?:[\w\.<>\?\[\]]+) (\w+)\s*\(([^)]*)\)\s*(?:\{|=>)", re.M)
CMD_CALL_RE = re.compile(r"await (\w+Cmd)\.(\w+)(<(\w+)>)?")


def method_body(text: str, start: int) -> str:
    """Brace-matched body starting at the '{' index."""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start : i + 1]
    return text[start:]


def process(path: str) -> tuple[list[dict], dict]:
    text = open(path, encoding="utf-8", errors="replace").read()
    cls = CLASS_RE.search(text)
    name = cls.group(1) if cls else os.path.basename(path)[:-3]
    rarity_m = RARITY_RE.search(text)
    rarity = rarity_m.group(1) if rarity_m else "Unknown"
    has_saved = "[SavedProperty]" in text

    atoms: list[dict] = []
    rejected: list[str] = []

    # --- event hooks ---
    for m in METHOD_RE.finditer(text):
        hook = m.group(1)
        body = method_body(text, text.index("{", m.end() - 1))
        if (name, hook) in QUERY_HOOKS:
            # Registered as a query hook (see QUERY_HOOKS). The event scanner
            # would misread it: PhilosophersStone.AfterCreatureAddedToCombat
            # applies Strength to the ENEMY, while the command scanner defaults
            # the target to self. The query scanner owns this hook.
            continue
        if hook not in HOOK_TRIGGER:
            rejected.append(f"{hook}:unmapped_hook")
            continue
        trigger_kind, lifetime = HOOK_TRIGGER[hook]

        cmds = list(CMD_CALL_RE.finditer(body))
        if not cmds:
            rejected.append(f"{hook}:no_command")
            continue

        for c in cmds:
            cmd, method, generic = c.group(1), c.group(2), c.group(4)
            opcode = variant = None
            if cmd == "PowerCmd" and method == "Apply" and generic:
                opcode, variant = power_variant(generic)
            elif (cmd, method) == ("CardPileCmd", "AddCurseToDeck") and generic:
                # The curse card is the generic argument, so it IS the variant
                # (Greed -> greed). The ledger verifies it per source relic.
                opcode, variant = "add_curse", snake_case(generic)
            elif (cmd, method) in {
                ("CreatureCmd", "GainBlock"),
                ("CreatureCmd", "Damage"),
                ("CreatureCmd", "Heal"),
                ("CreatureCmd", "GainMaxHp"),
                ("CreatureCmd", "LoseMaxHp"),
                ("PlayerCmd", "GainEnergy"),
                ("PlayerCmd", "GainStars"),
                ("PlayerCmd", "GainGold"),
                ("PlayerCmd", "GainMaxPotionCount"),
                ("PlayerCmd", "LoseGold"),
                ("CardPileCmd", "Draw"),
                ("OstyCmd", "Summon"),
                ("OrbCmd", "Channel"),
            }:
                opcode = {
                    ("CreatureCmd", "GainBlock"): "gain_block",
                    ("CreatureCmd", "Damage"): "lose_hp",
                    ("CreatureCmd", "Heal"): "heal",
                    ("CreatureCmd", "GainMaxHp"): "gain_max_hp",
                    ("CreatureCmd", "LoseMaxHp"): "lose_max_hp",
                    ("PlayerCmd", "GainEnergy"): "gain_energy",
                    ("PlayerCmd", "GainStars"): "gain_stars",
                    ("PlayerCmd", "GainGold"): "gain_gold",
                    ("PlayerCmd", "GainMaxPotionCount"): "gain_max_potion",
                    ("PlayerCmd", "LoseGold"): "lose_gold",
                    ("CardPileCmd", "Draw"): "draw_cards",
                    ("OstyCmd", "Summon"): "summon_osty",
                    ("OrbCmd", "Channel"): "channel_orb",
                }[(cmd, method)]
                variant = "immediate"
            else:
                rejected.append(f"{hook}:{cmd}.{method}")
                continue

            amount, found = extract_amount(text, body)
            target = "self"
            if opcode == "lose_hp" and "HittableEnemies" in body:
                target = "all_enemies"
            elif opcode == "lose_hp":
                target = "selected_enemy"

            atoms.append({
                "Id": f"{name}#{hook}#{len(atoms)}",
                "Source": name,
                "Rarity": rarity,
                "Spec": {
                    "Opcode": opcode,
                    "Variant": variant,
                    "Target": target,
                    "Values": ([{"Id": "amount", "BaseValue": amount,
                                 "Source": "fixed", "Offset": 0,
                                 "Upgradable": False}]
                               if found else []),
                    "Condition": ({"Kind": k} if (k := extract_condition(body)) else None),
                    "Trigger": {"Kind": trigger_kind, "Lifetime": lifetime},
                },
            })

    # --- modifier hooks ---
    for m in MODIFIER_METHOD_RE.finditer(text):
        hook = m.group(1)
        body = method_body(text, text.index("{", m.end() - 1))
        opcode = MODIFIER_OPCODE.get(hook)
        if not opcode:
            rejected.append(f"{hook}:unmapped_modifier")
            continue
        amount, found = extract_amount(text, body)
        atoms.append({
            "Id": f"{name}#{hook}",
            "Source": name,
            "Rarity": rarity,
            "Spec": {
                "Opcode": opcode,
                "Variant": "passive",
                "Target": "self",
                "Values": ([{"Id": "amount", "BaseValue": amount,
                             "Source": "fixed", "Offset": 0,
                             "Upgradable": False}]
                           if found else []),
                "Condition": ({"Kind": k} if (k := extract_condition(body)) else None),
                "Trigger": None,
            },
        })

    # --- restriction / benefit query hooks ---
    # These return a value instead of awaiting a Cmd, so the command scanner
    # above cannot see them. Keyed by (relic, hook): a hook name alone is not a
    # semantics (see QUERY_HOOKS). The event scanner already skips every
    # registered pair, so a hook cannot emit two atoms under two opcodes.
    for m in QUERY_METHOD_RE.finditer(text):
        hook = m.group(1)
        if (name, hook) not in QUERY_HOOKS:
            continue
        body = method_body(text, text.index("{", m.end() - 1))
        opcode, variant = QUERY_HOOKS[(name, hook)]
        amount, found = extract_amount(text, body)
        atoms.append({
            "Id": f"{name}#{hook}",
            "Source": name,
            "Rarity": rarity,
            "Spec": {
                "Opcode": opcode,
                "Variant": variant,
                "Target": "self",
                "Values": ([{"Id": "amount", "BaseValue": amount,
                             "Source": "fixed", "Offset": 0,
                             "Upgradable": False}]
                           if found else []),
                "Condition": ({"Kind": k} if (k := extract_condition(body)) else None),
                "Trigger": None,
            },
        })

    stats = {
        "class": name,
        "rarity": rarity,
        "has_saved": has_saved,
        "atoms": len(atoms),
        "rejected": rejected,
    }
    return atoms, stats


def main() -> int:
    out = None
    report = False
    args = sys.argv[1:]
    if "--out" in args:
        out = args[args.index("--out") + 1]
    report = "--report" in args

    files = sorted(f for f in os.listdir(SRC) if f.endswith(".cs"))
    files = [f for f in files if not f.startswith("_")]

    all_atoms: list[dict] = []
    stats_list: list[dict] = []
    reject_counter: Counter = Counter()

    for f in files:
        atoms, stats = process(os.path.join(SRC, f))
        # Relics carrying per-run saved state are not recombineable: their
        # behaviour depends on state the atom model cannot carry.
        if stats["has_saved"]:
            stats_list.append({**stats, "atoms": 0, "excluded": "saved_state"})
            continue
        all_atoms.extend(atoms)
        stats_list.append(stats)
        reject_counter.update(stats["rejected"])

    src_count = sum(1 for a in all_atoms)
    with_atoms = sum(1 for s in stats_list if s.get("atoms", 0) > 0)

    print(f"relic classes scanned : {len(files)}")
    print(f"relics contributing   : {with_atoms}")
    print(f"atoms extracted       : {src_count}")

    opcodes = Counter(a["Spec"]["Opcode"] for a in all_atoms)
    print(f"\ndistinct opcodes      : {len(opcodes)}")
    for k, v in opcodes.most_common():
        print(f"  {k:24} {v}")

    variants = Counter((a["Spec"]["Opcode"], a["Spec"]["Variant"]) for a in all_atoms)
    print(f"\ndistinct (opcode,variant): {len(variants)}")

    triggers = Counter(a["Spec"]["Trigger"]["Kind"] for a in all_atoms
                       if a["Spec"]["Trigger"])
    print(f"\ndistinct trigger kinds : {len(triggers)}")
    for k, v in triggers.most_common(20):
        print(f"  {k:24} {v}")

    conds = Counter(a["Spec"]["Condition"]["Kind"] for a in all_atoms
                    if a["Spec"]["Condition"])
    print(f"\ndistinct conditions    : {len(conds)}")
    for k, v in conds.most_common():
        print(f"  {k:24} {v}")

    if report:
        print("\n--- rejected (top 20) ---")
        for k, v in reject_counter.most_common(20):
            print(f"  {k:40} {v}")

    if out:
        payload = {
            "schema": "relic-atoms/v1",
            "source": "MegaCrit.Sts2.Core.Models.Relics (engine decompile)",
            "atomCount": len(all_atoms),
            "atoms": all_atoms,
        }
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(payload, fh, indent=1, ensure_ascii=False)
        print(f"\nwrote {out} ({len(all_atoms)} atoms)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
