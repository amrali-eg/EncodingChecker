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
}


def root(phase):
    return os.path.join(BASE, "EC-smoke-" + phase)


def state_path(phase):
    return os.path.join(HERE, "smoke-state-" + phase + ".json")


def sha256(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def snapshot(directory):
    return {
        name: sha256(os.path.join(directory, name))
        for name in sorted(os.listdir(directory))
        if os.path.isfile(os.path.join(directory, name))
    }


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
        json.dump({"root": directory, "before": snapshot(directory)}, handle, indent=1)

    print("Phase " + phase + " - " + spec["title"])
    print("\n  folder: " + directory)
    print("  files : " + ", ".join(spec["files"]))
    print("\n  in the GUI:")
    for step in spec["steps"]:
        print("    " + step)
    print("\n  then: python gui-smoke-test.py verify " + phase)
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


def verify(phase):
    spec = PHASES[phase]

    with open(state_path(phase), encoding="utf-8") as handle:
        state = json.load(handle)

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
    mode = sys.argv[1] if len(sys.argv) > 1 else "setup"
    which = (sys.argv[2] if len(sys.argv) > 2 else "A").upper()

    if which not in PHASES:
        print("phases: " + ", ".join(PHASES))
        sys.exit(2)

    sys.exit(setup(which) if mode == "setup" else verify(which))
