"""GUI smoke test, in phases.

An earlier version used one folder and one final check for the whole matrix. That cannot
work: the stale-plan case stops the entire run, so every "must have converted" expectation
after it is unreachable by construction. Worse, the state it leaves is byte-identical to
"the tester cancelled everything", so the result cannot say which protection fired. Its
first real run reported FAIL and the product had been correct throughout.

Each phase is therefore its own folder, its own short click sequence, and its own check,
and each proves exactly one property. The evidence is always what is on disk.

    python gui-smoke-test.py setup A       # then do phase A in the GUI
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

JP = ("こんにちは世界。日本語のテキストです。", "shift_jis")
RUSSIAN = ("Привет мир, это русский текст", "koi8-r")
PLAIN = ("plain ascii, no high bytes at all", "ascii")
MOVING = ("さようなら世界。これも日本語のテキストです。", "shift_jis")
BACKUPFAIL = ("これはバックアップ失敗の試験です。", "shift_jis")

# The only shape that reaches TextEquivalent: one byte, so nothing that decodes it at all
# can read it differently. ASCII of any real length is structurally determined instead,
# which is why this looks contrived - it has to be.
TINY = ("A", "ascii")

# Deliberately carries 0x80. In windows-1252 that is the euro sign; in iso-8859-1 it is a
# C1 control. An earlier sample used only accented letters, on which the two encodings
# agree exactly - so "the text survived" could not show which codec had been used, and the
# phase proved less than it claimed. This one cannot be satisfied by the wrong codec.
EURO = ("Prix: 100€ pour le café était déjà prêt", "cp1252")

PHASES = {
    "A": {
        "title": "Refusing and cancelling change nothing",
        "files": {"jp.txt": JP, "french.txt": EURO, "russian.txt": RUSSIAN,
                  "plain.txt": PLAIN, "tiny.txt": TINY},
        "steps": [
            "View the folder, tick every row, click Convert.",
            "The confirmation should say 2 file(s) need an explicit source encoding,",
            "  listing french.txt and russian.txt with the encodings in conflict.",
            "Click Cancel.",
        ],
        "unchanged": {
            "jp.txt": "cancelled, so nothing may be written",
            "french.txt": "refused, and cancelled",
            "russian.txt": "refused, and cancelled",
            "plain.txt": "cancelled",
            "tiny.txt": "cancelled",
        },
        "no_artifacts": True,
    },
    "B": {
        "title": "An explicit source is what reads the file, and only where it was given",
        "files": {"jp.txt": JP, "french.txt": EURO, "russian.txt": RUSSIAN},
        "steps": [
            "Tick 'Back up original files before converting' - this phase reads the record.",
            "View, tick every row, Convert.",
            "In the confirmation, UNTICK russian.txt so only french.txt stays ticked.",
            "Choose iso-8859-1. The button should read 'Use this encoding for 1 file(s)'.",
            "  (Detection says windows-1252 here, so choosing iso-8859-1 is what proves",
            "   the choice overrode it rather than merely agreeing with it.)",
            "Click it, then click Convert on the plan that comes back.",
            "Finally: Export -> 'Conversion journal (*.json)', saved into this folder",
            "  as journal.json.",
        ],
        "unchanged": {
            "russian.txt": "not answered for, so it stays refused",
        },
        "converted": {"jp.txt": JP[0]},
        # The discriminator. iso-8859-1 maps 0x80 to a C1 control; windows-1252 maps it to
        # the euro sign. Only the codec the tester chose can produce this.
        "text": {
            "french.txt": {
                "contains": [0x0080],
                "excludes": [0x20AC],
                "why": "the chosen iso-8859-1 reading, not the detected windows-1252 one",
            },
        },
        "sidecar": {
            "french.txt": {
                "DetectedCodePage": 28591,
                "TargetEncoding": "utf-8",
            },
        },
        "journal": {
            "french.txt": {
                "DetectionMode": "Explicit",
                "SourceEncoding": "iso-8859-1",
                "Status": "Converted",
            },
        },
    },
    "C": {
        "title": "A file changed while the dialog is open stops the whole run",
        "files": {"jp.txt": JP, "moving.txt": MOVING},
        "steps": [
            "View, tick both rows, Convert.",
            "LEAVE THE CONFIRMATION OPEN. In another editor, append anything to",
            "  moving.txt and save it.",
            "Now click Convert in the confirmation.",
            "It should refuse and name moving.txt.",
        ],
        "unchanged": {
            "jp.txt": "the run must stop whole, not convert the files that still match",
        },
        "no_artifacts": True,
        "edited": "moving.txt",
    },
    "D": {
        "title": "A conversion leaves a backup and a record; a failed backup aborts",
        "files": {"jp.txt": JP, "backupfail.txt": BACKUPFAIL},
        "bak_dirs": ["backupfail.txt.bak"],
        "steps": [
            "Tick 'Back up original files before converting'.",
            "View, tick both rows, Convert, and confirm.",
        ],
        "unchanged": {
            "backupfail.txt": "its .bak path is a directory, so the backup cannot be written",
        },
        "converted": {"jp.txt": JP[0]},
        "artifacts": ["jp.txt.bak", "jp.txt.ecmeta.json"],
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

    for name in spec.get("bak_dirs", []):
        os.makedirs(os.path.join(directory, name))

    with open(state_path(phase), "w", encoding="utf-8") as handle:
        json.dump({"root": directory, "before": snapshot(directory)}, handle, indent=1)

    print("Phase " + phase + " - " + spec["title"])
    print("\n  folder: " + directory)
    print("  files : " + ", ".join(spec["files"]))
    for name in spec.get("bak_dirs", []):
        print("  plus  : " + name + "/  (a directory, so the backup must fail)")
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


def check_sidecar(spec, directory, before, ok, fail):
    for name, expected in spec.get("sidecar", {}).items():
        path = os.path.join(directory, name + ".ecmeta.json")

        if not os.path.exists(path):
            fail(name + ": no recovery record (" + name + ".ecmeta.json)")
            continue

        record = json.load(open(path, encoding="utf-8"))

        for key, want in expected.items():
            if record.get(key) != want:
                fail("%s record: %s is %r, expected %r"
                     % (name, key, record.get(key), want))

        # Provenance the record must be internally consistent about, whatever the run was.
        if record.get("BackupSha256") != record.get("OriginalSha256"):
            fail(name + " record: the backup is not a copy of the original")
        elif record.get("OriginalSha256") != before.get(name):
            fail(name + " record: the original it names is not the file we created")
        elif record.get("SourceTextSha256") != record.get("OutputTextSha256"):
            fail(name + " record: the decoded text changed during conversion")
        else:
            ok("%-18s record consistent: backup, original and text hashes agree" % name)


def check_journal(spec, directory, ok, fail):
    wanted = spec.get("journal")

    if not wanted:
        return

    path = os.path.join(directory, "journal.json")

    if not os.path.exists(path):
        fail("journal.json is missing - export it from the GUI "
             "(Export -> 'Conversion journal (*.json)') into " + directory)
        return

    journal = json.load(open(path, encoding="utf-8"))
    entries = {e["RelativePath"]: e for e in journal.get("Entries", [])}

    for name, expected in wanted.items():
        entry = entries.get(name)

        if entry is None:
            fail("journal.json has no entry for " + name)
            continue

        for key, want in expected.items():
            if entry.get(key) != want:
                fail("journal %s: %s is %r, expected %r"
                     % (name, key, entry.get(key), want))

        # Only the GUI can show this pair differing: detection ran during View, and the
        # tester then overrode it. The CLI's -From never detects at all, so it cannot.
        if entry.get("DetectionMode") == "Explicit":
            if entry.get("DetectedEncoding") == entry.get("SourceEncoding"):
                ok("%-18s journal: explicit source recorded "
                   "(detection was not run separately)" % name)
            else:
                ok("%-18s journal: detected %s, read as %s"
                   % (name, entry.get("DetectedEncoding"), entry.get("SourceEncoding")))


def check_edited(spec, directory, ok, fail):
    """The tester's write must be there, and must not have been converted as well."""
    edited = spec.get("edited")

    if not edited:
        return

    raw = open(os.path.join(directory, edited), "rb").read()
    text, encoding = spec["files"][edited]
    original = text.encode(encoding)

    if raw == original:
        fail(edited + ": unchanged - the edit that makes the plan stale was never made, "
             "so this phase tested nothing")
    elif raw.startswith(original):
        ok("%-18s carries your edit, not a conversion" % edited)
    else:
        try:
            raw.decode("utf-8")
            fail(edited + ": looks converted rather than merely edited")
        except UnicodeDecodeError:
            ok("%-18s not converted" % edited)


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
    check_sidecar(spec, directory, before, ok, fail)
    check_journal(spec, directory, ok, fail)
    check_edited(spec, directory, ok, fail)

    for name in spec.get("artifacts", []):
        if os.path.exists(os.path.join(directory, name)):
            ok("%-18s present" % name)
        else:
            fail(name + ": missing")

    if spec.get("no_artifacts"):
        strays = [
            n for n in os.listdir(directory)
            if n.endswith((".bak", ".ecmeta.json")) and n not in spec.get("bak_dirs", [])
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
