"""Validate a Steam Workshop VDF the way steamcmd actually parses it.

Usage:  python check-vdf.py <path-to-workshop_upload.vdf>

WHY THIS FILE WAS REWRITTEN (2026-09-20)
----------------------------------------
The previous version treated a backslash-quote (\\") as a legal escape and so
reported "balanced+paired: True" for the exact file that made steamcmd abort:

    CKeyValuesSystem::AddStringToPool: key name too long (1165 chars)
    RecursiveLoadFromBuffer: got } in key in file workshopitem [offset: 14009]
    ERROR! Failed to parse build config file "..workshop_upload.vdf"

steamcmd's KeyValues reader does NOT interpret backslash escapes. It treats
'\\' as an ordinary character and the following '"' as the END of the string.
So a value written as \\"text\\" parses as "value ends here", then the remainder
becomes a KEY, which is what produced the 1165-character key name.

A validator that blesses the one input that breaks the real consumer is worse
than no validator, so the rule here is the consumer's rule: a text field may
contain NEITHER a bare ASCII quote NOR any backslash.

It also no longer hardcodes a changenote version (it asserted v0.1.6 long after
the payload moved to v0.1.9, so it silently reported "False" forever).

The authoritative guard is in .tooling/workshop-push-all.ps1 (quote/backslash,
byte size, description drift); this script is the standalone cross-repo check.
"""

import re
import sys

BS = chr(92)
QUOTE = chr(34)

# Steam caps these in BYTES (steamworks isteamremotestorage.h):
#   k_cchPublishedDocumentTitleMax = 128 + 1
#   k_cchPublishedDocumentDescriptionMax = 8000
#   k_cchPublishedDocumentChangeDescriptionMax = 8000
LIMITS = {"title": 129, "description": 8000, "changenote": 8000}


def parse_kv(raw):
    """Tokenize like steamcmd: no backslash escapes; every '"' terminates."""
    i, n, tokens = 0, len(raw), []
    while i < n:
        c = raw[i]
        if c in " \t\r\n":
            i += 1
        elif c in "{}":
            tokens.append(c)
            i += 1
        elif c == QUOTE:
            i += 1
            buf = []
            while i < n and raw[i] != QUOTE:
                buf.append(raw[i])
                i += 1
            if i >= n:
                raise ValueError("unterminated string")
            i += 1
            tokens.append("S:" + "".join(buf))
        else:
            j = i
            while j < n and raw[j] not in " \t\r\n{}" + QUOTE:
                j += 1
            tokens.append("W:" + raw[i:j])
            i = j
    return tokens


def check_structure(tokens):
    depth, keys, ok, k = 0, 0, True, 0
    problems = []
    while k < len(tokens):
        t = tokens[k]
        if t == "{":
            depth += 1
        elif t == "}":
            depth -= 1
            if depth < 0:
                ok = False
                problems.append("extra close brace")
                break
        else:
            if k + 1 >= len(tokens):
                ok = False
                problems.append("dangling key: " + t[:40])
                break
            nxt = tokens[k + 1]
            if nxt == "{":
                k += 1
                depth += 1
            elif nxt.startswith(("S:", "W:")):
                keys += 1
                k += 1
            else:
                ok = False
                problems.append("value missing after " + t[:40])
                break
        k += 1
    return ok, depth, keys, problems


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    path = sys.argv[1]
    with open(path, encoding="utf-8") as fh:
        raw = fh.read()

    failures = []

    # 1. The rule that matters: no backslash and no bare quote in any text field.
    #    (Backslashes ARE legitimate in contentfolder as a path separator, so only
    #    the three prose fields are checked.)
    fields = {}
    for key in LIMITS:
        m = re.search(r'(?s)"' + key + r'"[ \t]*"(.*?)"[ \t]*\r?\n', raw)
        if not m:
            failures.append("field %r not found" % key)
            continue
        body = m.group(1)
        fields[key] = body
        nbs = body.count(BS)
        nq = body.count(QUOTE)
        if nbs or nq:
            failures.append(
                "%s: %d backslash(es), %d ASCII quote(s) - steamcmd CANNOT parse this "
                "and aborts the whole upload session (use CJK quotes U+201C/U+201D)"
                % (key, nbs, nq)
            )
        nbytes = len(body.encode("utf-8"))
        if nbytes > LIMITS[key]:
            failures.append(
                "%s: %d bytes exceeds Steam's %d-byte cap (fails with 'Invalid Parameter')"
                % (key, nbytes, LIMITS[key])
            )

    # 2. Structure, parsed with the consumer's semantics.
    try:
        tokens = parse_kv(raw)
    except ValueError as exc:
        failures.append("parse error: %s" % exc)
        tokens = []
    if tokens:
        ok, depth, keys, problems = check_structure(tokens)
        if not ok or depth != 0:
            failures.extend(problems or ["unbalanced braces (depth end %d)" % depth])

    # 3. Reported facts.
    for key in LIMITS:
        if key in fields:
            print("%-12s %5d bytes" % (key, len(fields[key].encode("utf-8"))))
    if "description" in fields:
        d = fields["description"]
        print("[/list] balance in description: (%d, %d)" % (d.count("[list]"), d.count("[/list]")))

    if failures:
        print("\nFAIL")
        for f in failures:
            print("  - " + f)
        return 1
    print("\nOK: parseable by steamcmd, no backslashes/quotes, within byte limits.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
