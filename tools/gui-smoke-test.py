"""Small, repeatable GUI release smoke test.

Each phase has its own folder and a file-based verification step.  The GUI is the only
thing under test: this script prepares disposable files and checks what actually happened
on disk afterwards.

    python gui-smoke-test.py setup A       # then perform the stated GUI steps
    python gui-smoke-test.py verify A
"""
import hashlib
import json
import os
import shutil
import sys

BASE = os.environ.get("EC_SMOKE_DIR", os.path.join(
    os.path.expanduser("~"), "Desktop"))
HERE = os.path.dirname(os.path.abspath(__file__))

UNICODE = ("Hello, 世界 — Привет", "utf-8")
JP = ("こんにちは世界。日本語のテキストです。", "shift_jis")
RUSSIAN = ("Привет мир, это русский текст", "koi8-r")
PLAIN = ("plain ascii, no high bytes at all", "ascii")

# UTF-16BE text whose byte-swapped UTF-16LE reading is ordinary-looking A/LF/B.
# Both byte orders strictly decode, so automatic conversion must be refused.
AMBIGUOUS_UTF16_BE = ("\u4100\u0a00\u4200" * 4, "utf-16-be")

# Deliberately carries 0x80.  In windows-1252 that is the euro sign; in iso-8859-1 it is
# a C1 control.  It proves that the user's explicit choice, rather than a legacy guess,
# controls the conversion.
EURO = ("Prix: 100€ pour le café était déjà prêt", "cp1252")

PHASES = {
    "A": {
        "title": "Reviewing and cancelling change nothing",
        "files": {"unicode.txt": UNICODE, "jp.txt": JP, "french.txt": EURO,
                  "russian.txt": RUSSIAN, "plain.txt": PLAIN},
        "steps": [
            "In the Release build, set 'Directory to check' to the folder shown above.",
            "Choose utf-8 in 'Convert to', then click View.",
            "Check that all 5 files are listed. Tick every row and click Convert.",
            "The review must show unicode.txt and plain.txt as ready to convert.",
            "It must show jp.txt, french.txt, and russian.txt as needing a source encoding.",
            "Click Cancel.",
        ],
        "unchanged": {
            "unicode.txt": "cancelled, so nothing may be written",
            "jp.txt": "cancelled, so nothing may be written",
            "french.txt": "refused, and cancelled",
            "russian.txt": "refused, and cancelled",
            "plain.txt": "cancelled",
        },
        "no_artifacts": True,
    },
    "B": {
        "title": "Unicode and ASCII convert safely without a source choice",
        "files": {"unicode.txt": UNICODE, "plain.txt": PLAIN},
        "steps": [
            "In the Release build, set 'Directory to check' to the folder shown above.",
            "Choose utf-8 in 'Convert to', click View, tick both rows, and click Convert.",
            "The review must show both files as ready to convert and no source-encoding chooser.",
            "Confirm conversion.",
        ],
        "converted": {"unicode.txt": UNICODE[0], "plain.txt": PLAIN[0]},
    },
    "C": {
        "title": "An explicit legacy source applies only to the selected files",
        "files": {"french.txt": EURO, "russian.txt": RUSSIAN},
        "steps": [
            "In the Release build, set 'Directory to check' to the folder shown above.",
            "Choose utf-8 in 'Convert to', click View, tick both rows, and click Convert.",
            "Both files must require a source encoding. UNTICK russian.txt.",
            "Choose iso-8859-1 and click 'Confirm for 1 file(s)'.",
            "The review opens again. Click 'Convert 1 ready file(s)'.",
            "Only french.txt may be converted. russian.txt must remain refused and unchanged.",
        ],
        "unchanged": {"russian.txt": "no source encoding was supplied for it"},
        "text": {
            "french.txt": {
                "contains": [0x0080],
                "excludes": [0x20AC],
                "why": "the explicit iso-8859-1 reading chosen by the tester",
            },
        },
    },
    "D": {
        "title": "Ambiguous BOM-less UTF-16 is refused without writing",
        "files": {"ambiguous-utf16be.txt": AMBIGUOUS_UTF16_BE},
        "steps": [
            "In the Release build, set 'Directory to check' to the folder shown above.",
            "Choose utf-8 in 'Convert to', click View, tick the row, and click Convert.",
            "The review must say that the BOM-less UTF-16 byte order cannot be proven safely.",
            "Click Cancel. Do not choose a source encoding for this phase.",
        ],
        "unchanged": {
            "ambiguous-utf16be.txt": "automatic BOM-less UTF-16 conversion was refused",
        },
        "no_artifacts": True,
    },
    "E": {
        "title": "An explicit BOM-less UTF-16 source converts safely",
        "files": {"ambiguous-utf16be.txt": AMBIGUOUS_UTF16_BE},
        "steps": [
            "In the Release build, set 'Directory to check' to the folder shown above.",
            "Choose utf-8 in 'Convert to', click View, tick the row, and click Convert.",
            "The review must refuse automatic conversion and offer a source-encoding chooser.",
            "Choose utf-16BE and click 'Confirm for 1 file(s)'.",
            "The refreshed review must show 1 file ready. Click 'Convert 1 file(s)'.",
        ],
        "converted": {"ambiguous-utf16be.txt": AMBIGUOUS_UTF16_BE[0]},
        "artifacts": [
            "ambiguous-utf16be.txt.bak",
            "ambiguous-utf16be.txt.ecmeta.json",
        ],
    },
}


def root(phase):
    return os.path.join(BASE, "EC-smoke-" + phase)


def state_path(phase):
    return os.path.join(HERE, "smoke-state-" + phase + ".json")


def powershell_quote(value):
    """Returns a PowerShell single-quoted string for copy-and-paste instructions."""
    return "'" + value.replace("'", "''") + "'"


def sha256(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def snapshot(directory):
    return {
        name: sha256(os.path.join(directory, name))
        for name in sorted(os.listdir(directory))
        if os.path.isfile(os.path.join(directory, name))
    }


def required_setup_steps(spec):
    """GUI preconditions a phase's assertions depend on, derived from the spec."""
    steps = []

    if spec.get("artifacts"):
        steps.append(
            "Ensure 'Create backup' is TICKED before converting; this phase "
            "verifies the .bak and .ecmeta.json recovery files.")

    if spec.get("no_artifacts"):
        steps.append(
            "Leave 'Create backup' TICKED; this phase proves nothing is written "
            "even when backups are enabled.")

    return steps


def setup(phase):
    spec = PHASES[phase]
    directory = root(phase)

    if os.path.isdir(directory):
        shutil.rmtree(directory)
    os.makedirs(directory)

    for name, (text, encoding) in spec["files"].items():
        with open(os.path.join(directory, name), "wb") as handle:
            handle.write(text.encode(encoding))

    with open(state_path(phase), "w", encoding="utf-8") as handle:
        json.dump(
            {"root": directory, "before": snapshot(directory), "manual_complete": False},
            handle,
            indent=1)

    print("Phase " + phase + " - " + spec["title"])
    print("\n  folder: " + directory)
    print("  files : " + ", ".join(spec["files"]))
    print("\n  in the GUI:")
    # Derived from the spec rather than written into each phase's steps. A phase
    # that asserts .bak and .ecmeta.json depends on the backup checkbox, which the
    # GUI persists between sessions: phase E once failed on "missing recovery
    # artifact" because an earlier run had left it unticked, which says nothing
    # about the behaviour under test. Deriving it means a phase that starts
    # asserting artifacts cannot forget to ask for them.
    for step in required_setup_steps(spec):
        print("    " + step)

    for step in spec["steps"]:
        print("    " + step)
    print("\n  PowerShell commands to copy and paste after the GUI steps:")
    print("    $smoke = " + powershell_quote(os.path.abspath(__file__)))
    print("    python $smoke mark " + phase)
    print("    python $smoke verify " + phase)

    next_phase = chr(ord(phase) + 1)
    if next_phase in PHASES:
        print("    python $smoke setup " + next_phase)

    return 0


def mark(phase):
    """Records that the tester completed the displayed GUI steps for this phase."""
    path = state_path(phase)

    with open(path, encoding="utf-8") as handle:
        state = json.load(handle)

    state["manual_complete"] = True

    with open(path, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=1)

    print("Phase " + phase + " marked ready for verification.")
    return 0


def check_unchanged(spec, before, after, ok, fail):
    for name, why in spec.get("unchanged", {}).items():
        if name not in after:
            fail(name + ": MISSING (" + why + ")")
        elif after[name] != before[name]:
            fail(name + ": CHANGED but must not have - " + why)
        else:
            ok("%-18s unchanged   (%s)" % (name, why))


def check_converted(spec, directory, after, ok, fail):
    for name, expected in spec.get("converted", {}).items():
        if name not in after:
            fail(name + ": MISSING")
            continue

        raw = open(os.path.join(directory, name), "rb").read()

        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError as ex:
            fail(name + ": still not UTF-8, so it was never converted (" + str(ex) + ")")
            continue

        if text != expected:
            fail(name + ": converted, but the text changed\n"
                 "        expected " + repr(expected) + "\n"
                 "        actual   " + repr(text))
        else:
            ok("%-18s converted, text preserved exactly" % name)


def check_text(spec, directory, ok, fail):
    """Which codec produced the output, asserted on the output itself."""
    for name, rule in spec.get("text", {}).items():
        path = os.path.join(directory, name)

        if not os.path.exists(path):
            fail(name + ": MISSING")
            continue

        try:
            text = open(path, "rb").read().decode("utf-8")
        except UnicodeDecodeError as ex:
            fail(name + ": still not UTF-8, so it was never converted (" + str(ex) + ")")
            continue

        for point in rule.get("contains", []):
            if chr(point) not in text:
                fail(name + ": missing U+%04X - %s" % (point, rule["why"]))
                break
        else:
            for point in rule.get("excludes", []):
                if chr(point) in text:
                    fail(name + ": contains U+%04X, so a different codec read it - %s"
                         % (point, rule["why"]))
                    break
            else:
                ok("%-18s %s" % (name, rule["why"]))


def check_artifacts(spec, directory, ok, fail):
    for name in spec.get("artifacts", []):
        if not os.path.isfile(os.path.join(directory, name)):
            fail(name + ": missing recovery artifact")
        else:
            ok("%-18s present" % name)


def verify(phase):
    spec = PHASES[phase]

    with open(state_path(phase), encoding="utf-8") as handle:
        state = json.load(handle)

    if not state.get("manual_complete"):
        print("PHASE " + phase + ": NOT VERIFIED")
        print("  Complete the displayed GUI steps first, then run:")
        print("    python " + powershell_quote(os.path.abspath(__file__)) + " mark " + phase)
        return 2

    directory = state["root"]
    before = state["before"]
    after = snapshot(directory)
    failures = []

    def ok(line):
        print("  ok   " + line)

    def fail(line):
        failures.append(line)

    check_unchanged(spec, before, after, ok, fail)
    check_converted(spec, directory, after, ok, fail)
    check_text(spec, directory, ok, fail)
    check_artifacts(spec, directory, ok, fail)
    if spec.get("no_artifacts"):
        strays = [
            n for n in os.listdir(directory)
            if n.endswith((".bak", ".ecmeta.json"))
        ]

        if strays:
            fail("nothing should have been written, but found: " + ", ".join(strays))
        else:
            ok("%-18s no backups or records written" % "(folder)")

    print()

    if failures:
        print("PHASE " + phase + ": FAIL")
        for failure in failures:
            print("  " + failure)
        return 1

    print("PHASE " + phase + ": PASS - " + spec["title"])
    return 0


if __name__ == "__main__":
    mode = sys.argv[1].lower() if len(sys.argv) > 1 else "setup"
    which = (sys.argv[2] if len(sys.argv) > 2 else "A").upper()

    if which not in PHASES:
        print("phases: " + ", ".join(PHASES))
        sys.exit(2)

    if mode == "setup":
        sys.exit(setup(which))
    if mode == "mark":
        sys.exit(mark(which))
    if mode == "verify":
        sys.exit(verify(which))

    print("commands: setup, mark, verify")
    sys.exit(2)
