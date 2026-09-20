"""Audits localization call sites against the Simplified Chinese dictionaries.

Usage: python tools/loc_audit.py
Exit code is 1 when a wrapped key has no zh entry, a translation disagrees with its
key about placeholders or printf specifiers, or a key is defined twice.

Scope and blind spots - read before trusting the numbers:

* The denominator is only strings *already wrapped* in `Loc.T(...)` / `Loc.TPrintf(...)`
  / `LocText.Of(...)`. A user-visible literal that nobody wrapped is invisible here,
  so "0 missing" means "every wrapped string is translated", not "the UI is fully
  translated".
* `Loc.T(variable)` sites pass a key computed at runtime. They are counted as neither
  used nor missing, and their dictionary entries therefore show up under UNUSED. Known
  indirect sites live in `INDIRECT_MARKERS` below and are unioned into `used`.
* A marker followed by something that is not a plain quoted literal (an interpolated
  `$"..."`, a verbatim `@"..."`, a raw `\"\"\"..\"\"\"`, or a variable) is skipped. Those
  are counted and reported under SKIPPED so the blind spot stays visible.
* A *second, unwrapped copy* of an already-translated sentence is invisible to this tool.
  The key is `used` (from the wrapped site) and `defined` (in the dictionary), so nothing
  is MISSING even though one of the two call sites ships English. Only reading the diff
  catches that.
"""
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Saucy")
LOC_DIR = os.path.join(ROOT, "Core", "Localization")
ZH_DIR = os.path.join(LOC_DIR, "Zh")
LOC_CS = os.path.join(LOC_DIR, "Loc.cs")
MODULE_DISPLAY_NAMES_CS = os.path.join(ROOT, "Core", "ModuleDisplayNames.cs")

# Direct wraps: the literal right after the marker is the translation key.
# `Loc.TPrintf(` is listed separately because `Loc.T(` does not match it.
DIRECT_MARKERS = ("Loc.T(", "Loc.TPrintf(", "LocText.Of(")

# Indirect wraps: these helpers call Loc.T on their argument, so the literal at the
# *call site* is the key even though no Loc.T appears there.
INDIRECT_MARKERS = (
    "TriadDeckLog.Print(",
    "PrintNavigationBlocked(",
    # Literals assigned here are translated later by a `Loc.T(variable)` consumer.
    "blockReason = ",
    "TooltipHint =",
    "NavigationBlockedMessage =",
)

# Every simple escape C# accepts, including C# 13's `\e` (Saucy.csproj sets LangVersion
# preview). Completeness matters: an unknown escape used to fall through to
# `ESCAPES.get(nxt, nxt)`, which silently dropped the backslash and produced a key one
# character short of the one the compiler emits - a miss the gate would never report,
# because the truncated key is simply absent from the dictionary in a way that looks like
# an ordinary MISSING rather than a parser bug.
ESCAPES = {
    "n": "\n", "t": "\t", "r": "\r", "a": "\a", "b": "\b", "f": "\f", "v": "\v",
    "e": "\x1b", '"': '"', "'": "'", "\\": "\\", "0": "\0",
}

# \uXXXX is exactly 4 hex digits and \UXXXXXXXX exactly 8; \xH through \xHHHH is not
# fixed width, and munches greedily - `\x41BC` is one char, not `A` followed by "BC".
HEX_ESCAPE = re.compile(r"\\(?:u([0-9a-fA-F]{4})|U([0-9a-fA-F]{8})|x([0-9a-fA-F]{1,4}))")


def read_literal(text, i):
    """Reads one C# string literal starting at text[i] == '"'. Returns (value, next_index).

    Raises ValueError on anything it cannot model exactly. Callers must catch it: a
    guessed key is worse than a reported parse failure, because a guessed key fails as
    an ordinary MISSING row that reads like a translator's oversight.
    """
    assert text[i] == '"'
    i += 1
    out = []
    while i < len(text):
        c = text[i]
        if c == "\\":
            hexmatch = HEX_ESCAPE.match(text, i)
            if hexmatch is not None:
                # lastindex, not a truthiness scan: the scan is only correct because no
                # alternative can match zero width, which a future edit could change.
                point = int(hexmatch.group(hexmatch.lastindex), 16)
                if 0xD800 <= point <= 0xDFFF:
                    # chr() accepts lone surrogates, but this parser models UTF-32 while
                    # C# models UTF-16, so an escaped pair would never equal the same
                    # character written literally - and print() dies on it later anyway.
                    raise ValueError("lone surrogate \\u%04X at offset %d" % (point, i))
                out.append(chr(point))
                i = hexmatch.end()
                continue
            if i + 1 >= len(text):
                raise ValueError("literal ends on a backslash at offset %d" % i)
            nxt = text[i + 1]
            if nxt not in ESCAPES:
                raise ValueError("unknown escape \\%s at offset %d" % (nxt, i))
            out.append(ESCAPES[nxt])
            i += 2
            continue
        if c == '"':
            return "".join(out), i + 1
        out.append(c)
        i += 1
    raise ValueError("unterminated literal")


def read_concat(text, i):
    """Reads a (possibly '+'-concatenated, possibly multi-line) literal expression.

    Returns (None, i) when the expression is not wholly made of plain literals - a raw
    string (`\"\"\"..\"\"\"`), or a `"lit " + variable` concatenation. Returning the
    literal half of such an expression would register a truncated key as translated.
    """
    parts = []
    pending_concat = False
    while True:
        while i < len(text) and text[i] in " \t\r\n":
            i += 1
        if i >= len(text) or text[i] != '"':
            # A '+' was consumed but no literal follows: the key is computed, not constant.
            return (None, i) if pending_concat else (("".join(parts), i) if parts else (None, i))
        if text.startswith('"""', i):
            return None, i
        value, i = read_literal(text, i)
        parts.append(value)
        pending_concat = False
        j = i
        while j < len(text) and text[j] in " \t\r\n":
            j += 1
        if j < len(text) and text[j] == "+":
            i = j + 1
            pending_concat = True
            continue
        break
    return ("".join(parts), i) if parts else (None, i)


def is_verbatim_or_raw(text, i):
    """True when the quote at text[i] opens a verbatim or a raw string literal.

    `read_literal` models neither: in a verbatim `@"..."` a backslash is an ordinary
    character, so `@"C:\\data"` would raise on an "unknown escape", and a raw string's
    tripled quotes read as an empty literal followed by garbage. Callers fail closed.
    """
    if text.startswith('"""', i):
        return True
    j = i - 1
    while j >= 0 and text[j] in "$@":
        if text[j] == "@":
            return True
        j -= 1
    return False


def count_trailing_args(text, i):
    """Counts the arguments after a format literal, from index i to the closing paren.

    Returns None when the call cannot be parsed confidently (unbalanced, a `params`
    array spread, generic angle brackets whose commas are type separators rather
    than argument separators, or a verbatim/raw string argument), so the caller skips
    the parity check instead of guessing.
    """
    depth = 0
    args = 0
    while i < len(text):
        c = text[i]
        if c == '"':
            if is_verbatim_or_raw(text, i):
                return None
            try:
                _, i = read_literal(text, i)
            except ValueError:
                return None
            continue
        if c in "<>":
            # `Foo<A, B>(x)` would otherwise count 2 args for 1. Fail closed.
            return None
        if c in "([{":
            depth += 1
        elif c in ")]}":
            if depth == 0:
                return args
            depth -= 1
        elif c == "," and depth == 0:
            args += 1
        elif c == ";" and depth == 0:
            return None
        i += 1
    return None


def cs_files(root):
    for dirpath, _, names in os.walk(root):
        for name in names:
            if name.endswith(".cs"):
                yield os.path.join(dirpath, name)


PLACEHOLDER = re.compile(r"\{(\d+)[^}]*\}")

# A printf conversion specifier as native stb_sprintf would read it. `%%` is an escape,
# not a conversion, so it is consumed first and never reported. The lone space flag is
# excluded: "win % among" is prose, and matching it buries the real drift in noise.
PRINTF_SPEC = re.compile(r"%%|%[-+#0]*[\d*]*(?:\.\d+)?(?:hh|h|ll|l|j|z|t|L)?([diouxXeEfFgGaAcsp])")


def placeholders(text):
    return sorted(set(PLACEHOLDER.findall(text)))


def max_placeholder(text):
    found = PLACEHOLDER.findall(text)
    return max((int(n) for n in found), default=-1)


def printf_specs(text):
    return sorted(m.group(0) for m in PRINTF_SPEC.finditer(text) if m.group(1))


def has_stray_brace(text):
    """True when a brace is neither an escaped `{{`/`}}` nor part of a `{0}` placeholder.

    `string.Format` throws FormatException on a stray brace, so a translator's typo would
    crash the frame it renders on rather than merely look wrong. Placeholder comparison
    misses `"{0}}"`, which has the same placeholder set as its key.
    """
    rest = PLACEHOLDER.sub("", text.replace("{{", "").replace("}}", ""))
    return "{" in rest or "}" in rest


def zh_sources():
    """Reads the runtime merge order out of Loc.ZhSources so the audit sees what ships.

    A table that exists on disk but is not merged would otherwise look translated here.
    """
    text = open(LOC_CS, encoding="utf-8-sig").read()
    block = text.split("ZhSources =>", 1)[1].split("]", 1)[0]
    merged = [name + ".cs" for name in re.findall(r"(\w+)\.Entries", block)]
    # An empty list would make every zh entry "not merged" and every key MISSING, which
    # reads as a catastrophic regression rather than as "the parser lost its anchor".
    assert merged, "could not parse Loc.ZhSources - has the property been renamed?"
    return merged


PARSE_ERRORS = []


def safe_read_concat(text, i, rel, start):
    """read_concat, but a parse failure becomes a reported row instead of a traceback.

    A bare traceback out of `collect_used` would discard every other section of the
    report - the printing all happens after collection - and name no file. Returning
    None here keeps the run going while still failing the gate.
    """
    try:
        return read_concat(text, i)
    except ValueError as ex:
        PARSE_ERRORS.append((rel, text.count("\n", 0, start) + 1, str(ex)))
        return None, i


def collect_used():
    used = {}
    skipped = []
    for path in cs_files(ROOT):
        # Localization infrastructure passes variables to Loc.T by design and holds no keys.
        if os.path.normpath(LOC_DIR) in os.path.normpath(path):
            continue
        rel = os.path.relpath(path, ROOT)
        text = open(path, encoding="utf-8-sig").read()
        for marker in DIRECT_MARKERS + INDIRECT_MARKERS:
            for m in re.finditer(re.escape(marker), text):
                key, end = safe_read_concat(text, m.end(), rel, m.start())
                if key is None:
                    # A marker wrapping another marker, like Print(LocText.Of("…")), is not a
                    # blind spot: the inner marker already captured the key.
                    rest = text[m.end():].lstrip()
                    if not rest.startswith(DIRECT_MARKERS):
                        line = text.count("\n", 0, m.start()) + 1
                        skipped.append((rel, line, text[m.start():m.start() + 60].strip()))
                    continue
                used.setdefault(key, set()).add(rel)
                if marker in DIRECT_MARKERS:
                    check_arity(key, rel, text, end, m.start())
    return used, skipped


ARITY_ERRORS = []


def check_arity(key, rel, text, end, start):
    """Flags a call whose argument count cannot satisfy the key's highest placeholder."""
    args = count_trailing_args(text, end)
    if args is None:
        return
    needed = max_placeholder(key) + 1
    if args < needed:
        line = text.count("\n", 0, start) + 1
        ARITY_ERRORS.append((rel, line, key, needed, args))


def collect_module_display_names():
    """`Loc.T(ModuleDisplayNames.X)` passes a variable, so the const's value is the key.

    Without this the eight module names would all be reported UNUSED, training the reader
    to ignore that section.
    """
    keys = set()
    text = open(MODULE_DISPLAY_NAMES_CS, encoding="utf-8-sig").read()
    for m in re.finditer(r"public const string \w+ = ", text):
        key, _ = safe_read_concat(text, m.end(), "Core/ModuleDisplayNames.cs", m.start())
        if key is not None:
            keys.add(key)
    assert keys, "could not parse ModuleDisplayNames consts"
    return keys


def collect_defined(expected_files):
    defined = {}
    duplicates = []
    for name in expected_files:
        path = os.path.join(ZH_DIR, name)
        text = open(path, encoding="utf-8-sig").read()
        # Anchored at start-of-line: an indexer inside a comment or a nested expression
        # is not a dictionary entry, and counting one would hide a real duplicate.
        for m in re.finditer(r'^[ \t]*\["', text, re.M):
            key, end = safe_read_concat(text, m.end() - 1, name, m.start())
            if key is None:
                continue
            value, _ = safe_read_concat(text, text.index("=", end) + 1, name, m.start())
            if key in defined:
                duplicates.append(key)
            defined[key] = (name, value)
    return defined, duplicates


def report(title, rows, render):
    print("\n--- %s (%d) ---" % (title, len(rows)))
    for row in rows:
        print(render(row))


def main():
    merged = zh_sources()
    on_disk = sorted(n for n in os.listdir(ZH_DIR) if n.endswith(".cs"))
    unmerged = sorted(set(on_disk) - set(merged))

    used, skipped = collect_used()
    for key in collect_module_display_names():
        used.setdefault(key, set()).add("Core/ModuleDisplayNames.cs")
    defined, duplicates = collect_defined(merged)

    missing = sorted(set(used) - set(defined))
    unused = sorted(set(defined) - set(used))
    mismatched = sorted(
        key for key, (_, value) in defined.items()
        if value is not None and placeholders(key) != placeholders(value)
    )
    printf_drift = sorted(
        key for key, (_, value) in defined.items()
        if value is not None and printf_specs(key) != printf_specs(value)
    )
    stray_braces = sorted(
        key for key, (_, value) in defined.items()
        if value is not None and has_stray_brace(value)
    )

    print("wrapped keys: %d" % len(used))
    print("zh entries:   %d  (from %s)" % (len(defined), ", ".join(merged)))
    print("skipped call sites (non-literal argument): %d" % len(skipped))

    report("MISSING zh translation", missing,
           lambda k: "  [%s]\n      %s" % (", ".join(sorted(used[k])), k))
    report("UNUSED zh entries", unused, lambda k: "  %s" % k)
    report("PLACEHOLDER MISMATCH", mismatched,
           lambda k: "  %s\n      %s" % (k, defined[k][1]))
    report("PRINTF SPECIFIER DRIFT", printf_drift,
           lambda k: "  %s  %s\n      %s  %s" % (k, printf_specs(k), defined[k][1],
                                                 printf_specs(defined[k][1])))
    report("DUPLICATE keys", sorted(set(duplicates)), lambda k: "  %s" % k)
    report("STRAY BRACE in translation", stray_braces,
           lambda k: "  %s\n      %s" % (k, defined[k][1]))
    report("ARGUMENT COUNT SHORTFALL", ARITY_ERRORS,
           lambda r: "  %s:%d needs %d arg(s), passes %d\n      %s" % (r[0], r[1], r[3], r[4], r[2]))
    report("ZH FILES NOT MERGED BY Loc.ZhSources", unmerged, lambda n: "  %s" % n)
    report("LITERAL PARSE FAILURES", PARSE_ERRORS,
           lambda r: "  %s:%d  %s" % (r[0], r[1], r[2]))
    report("SKIPPED call sites", skipped,
           lambda r: "  %s:%d  %s" % (r[0], r[1], r[2]))

    failures = (missing or mismatched or printf_drift or duplicates or ARITY_ERRORS
                or unmerged or stray_braces or PARSE_ERRORS)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
