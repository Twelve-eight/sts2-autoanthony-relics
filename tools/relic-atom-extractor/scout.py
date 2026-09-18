"""Reconnaissance pass over the engine relic sources.

Before committing to an atom model we need to know how the 300 relics are
actually shaped: which hooks they override, which engine commands they call,
and how much of each body is a simple "on event, do X" versus irreducible
custom logic.

This script does NOT emit atoms - it prints the shape so the atom model can be
designed against measured facts instead of guesses.

Usage: python scout.py [sample_size]
"""
import os
import re
import sys
from collections import Counter

SRC = r"G:/omp works/sts2-spire1/research/engine-dllsrc/MegaCrit.Sts2.Core.Models.Relics"

# Hooks that can carry a recombineable combat effect.
HOOK_RE = re.compile(
    r"public override (?:async )?Task (\w+)\s*\(", re.M)
# Modifier-style hooks return a value instead of being an event.
MODIFIER_RE = re.compile(
    r"public override (?:decimal|int|bool) (Modify\w+)\s*\(", re.M)
CMD_RE = re.compile(r"await (\w+Cmd)\.(\w+)")
RARITY_RE = re.compile(r"RelicRarity\.(\w+)")
VARS_RE = re.compile(r"CanonicalVars\s*=>\s*(.+?);\s*\n", re.S | re.M)
CLASS_RE = re.compile(r"public sealed class (\w+)")


def scan(path: str) -> dict:
    text = open(path, encoding="utf-8", errors="replace").read()
    cls = CLASS_RE.search(text)
    rarity = RARITY_RE.search(text)
    hooks = HOOK_RE.findall(text)
    modifiers = MODIFIER_RE.findall(text)
    cmds = Counter(f"{c}.{m}" for c, m in CMD_RE.findall(text))
    vars_block = VARS_RE.search(text)
    return {
        "class": cls.group(1) if cls else os.path.basename(path)[:-3],
        "rarity": rarity.group(1) if rarity else "Unknown",
        "hooks": hooks,
        "modifiers": modifiers,
        "cmds": cmds,
        "has_vars": vars_block is not None,
        "vars_text": (vars_block.group(1)[:120] if vars_block else ""),
        "lines": text.count("\n") + 1,
        "saved_props": text.count("[SavedProperty]"),
    }


def main() -> int:
    sample = int(sys.argv[1]) if len(sys.argv) > 1 else 0
    files = sorted(f for f in os.listdir(SRC) if f.endswith(".cs"))
    # Skip the abstract base and any nested/partial helpers.
    files = [f for f in files if not f.startswith("_")]
    if sample:
        files = files[:sample]

    relics = [scan(os.path.join(SRC, f)) for f in files]

    n = len(relics)
    print(f"=== scanned {n} relic classes ===")
    print()

    hook_count = Counter()
    mod_count = Counter()
    cmd_count = Counter()
    rarity_count = Counter()
    no_hook = []
    multi_hook = []
    has_saved = 0

    for r in relics:
        hook_count.update(r["hooks"])
        mod_count.update(r["modifiers"])
        cmd_count.update(r["cmds"])
        rarity_count[r["rarity"]] += 1
        total = len(r["hooks"]) + len(r["modifiers"])
        if total == 0:
            no_hook.append(r["class"])
        elif total > 1:
            multi_hook.append((r["class"], total))
        if r["saved_props"]:
            has_saved += 1

    print(f"--- rarity ({len(rarity_count)}) ---")
    for k, v in rarity_count.most_common():
        print(f"  {k:12} {v}")
    print()

    print(f"--- hooks ({sum(hook_count.values())} total) ---")
    for k, v in hook_count.most_common():
        print(f"  {k:40} {v}")
    print()

    print(f"--- modifier hooks ({sum(mod_count.values())} total) ---")
    for k, v in mod_count.most_common():
        print(f"  {k:40} {v}")
    print()

    print(f"--- engine commands ({sum(cmd_count.values())} total) ---")
    for k, v in cmd_count.most_common(30):
        print(f"  {k:40} {v}")
    print()

    print(f"--- shape ---")
    print(f"  relics with NO hook       : {len(no_hook)}")
    print(f"  relics with >1 hook       : {len(multi_hook)}")
    print(f"  relics using SavedProperty: {has_saved}")
    print()

    # Complexity: how many relics are "simple" (1 hook, <=2 commands, no saved state)?
    simple = [r for r in relics
              if len(r["hooks"]) + len(r["modifiers"]) <= 1
              and sum(r["cmds"].values()) <= 2
              and not r["saved_props"]]
    print(f"  simple (<=1 hook, <=2 cmds, no saved state): {len(simple)} / {n}")
    print()

    if sample:
        print("--- sample detail ---")
        for r in relics:
            print(f"  {r['class']:28} {r['rarity']:10} "
                  f"hooks={r['hooks']} mods={r['modifiers']} "
                  f"cmds={dict(r['cmds'])} saved={r['saved_props']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
