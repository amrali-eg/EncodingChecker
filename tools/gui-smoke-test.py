"""GUI smoke test, in phases.

The first version used one corpus and one final check for the whole matrix. That cannot
work: the stale-plan case stops the entire run, so every "must have converted"
expectation after it is unreachable by construction. Worse, the state it leaves is
identical to "the tester cancelled everything", so it cannot say which protection fired.

Each phase is therefore its own folder, its own short click sequence, and its own check,
and each proves exactly one property. The evidence is always the bytes on disk.

    python smoke.py setup A       # then do phase A in the GUI
    python smoke.py verify A
"""
import hashlib
import json
import os
import shutil
import sys

BASE = r"C:\Users\Amr\Desktop"
HERE = os.path.dirname(os.path.abspath(__file__))

JP = ("こんにちは世界。日本語のテキストです。", "shift_jis")
FRENCH = ("Le café était déjà prêt", "cp1252")
RUSSIAN = ("Привет мир, это русский текст", "koi8-r")
PLAIN = ("plain ascii, no high bytes at all", "ascii")
TINY = ("A", "ascii")
MOVING = ("さようなら世界。これも日本語のテキストです。", "shift_jis")
BACKUPFAIL = ("これはバックアップ失敗の試験です。", "shift_jis")

PHASES = {
    "A": {
        "title": "Refusing and cancelling change nothing",
        "files": {"jp.txt": JP, "french.txt": FRENCH, "russian.txt": RUSSIAN,
                  "plain.txt": PLAIN, "tiny.txt": TINY},
        "bak_dirs": [],
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
        "converted": {},
        "no_artifacts": True,
    },
    "B": {
        "title": "An explicit source applies only to the files it was given for",
        "files": {"jp.txt": JP, "french.txt": FRENCH, "russian.txt": RUSSIAN},
        "bak_dirs": [],
        "steps": [
            "View, tick every row, Convert.",
            "In the confirmation, UNTICK russian.txt so only french.txt stays ticked.",
            "Choose windows-1252. The button should read 'Use this encoding for 1 file(s)'.",
            "Click it, then click Convert on the plan that comes back.",
        ],
        "unchanged": {
            "russian.txt": "not answered for, so it stays refused",
        },
        "converted": {
            "jp.txt": JP[0],
            "french.txt": FRENCH[0],
        },
        "no_artifacts": False,
    },
    "C": {
        "title": "A file changed while the dialog is open stops the whole run",
        "files": {"jp.txt": JP, "moving.txt": MOVING},
        "bak_dirs": [],
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
        "converted": {},
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
        "no_artifacts": False,
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

    for name in spec["bak_dirs"]:
        os.makedirs(os.path.join(directory, name))

    with open(state_path(phase), "w", encoding="utf-8") as handle:
        json.dump({"root": directory, "before": snapshot(directory)}, handle, indent=1)

    print("Phase " + phase + " - " + spec["title"])
    print("\n  folder: " + directory)
    print("  files : " + ", ".join(spec["files"]))
    for name in spec["bak_dirs"]:
        print("  plus  : " + name + "/  (a directory, so the backup must fail)")
    print("\n  in the GUI:")
    for step in spec["steps"]:
        print("    " + step)
    print("\n  then: python smoke.py verify " + phase)
    return 0


def verify(phase):
    spec = PHASES[phase]

    with open(state_path(phase), encoding="utf-8") as handle:
        state = json.load(handle)

    directory = state["root"]
    before = state["before"]
    after = snapshot(directory)
    failures = []

    for name, why in spec["unchanged"].items():
        if name not in after:
            failures.append(name + ": MISSING (" + why + ")")
        elif after[name] != before[name]:
            failures.append(name + ": CHANGED but must not have - " + why)
        else:
            print("  ok   %-18s unchanged   (%s)" % (name, why))

    for name, expected in spec["converted"].items():
        if name not in after:
            failures.append(name + ": MISSING")
            continue

        raw = open(os.path.join(directory, name), "rb").read()

        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError as ex:
            failures.append(
                name + ": still not UTF-8, so it was never converted (" + str(ex) + ")")
            continue

        if text != expected:
            failures.append(
                name + ": converted, but the text changed\n"
                "        expected " + repr(expected) + "\n"
                "        actual   " + repr(text))
        else:
            print("  ok   %-18s converted, text preserved exactly" % name)

    # The file the tester edited must carry that edit and nothing else - EC must not have
    # converted it either. Telling the tester's write apart from a conversion is precisely
    # what the single-corpus version of this script could not do, and why its result was
    # unreadable.
    edited = spec.get("edited")

    if edited:
        raw = open(os.path.join(directory, edited), "rb").read()
        text, encoding = spec["files"][edited]
        original = text.encode(encoding)

        if raw == original:
            failures.append(
                edited + ": unchanged - the edit that makes the plan stale was never "
                "made, so this phase tested nothing")
        elif raw.startswith(original):
            print("  ok   %-18s carries your edit, not a conversion" % edited)
        else:
            try:
                raw.decode("utf-8")
                failures.append(edited + ": looks converted rather than merely edited")
            except UnicodeDecodeError:
                print("  ok   %-18s not converted" % edited)

    for name in spec.get("artifacts", []):
        if os.path.exists(os.path.join(directory, name)):
            print("  ok   %-18s present" % name)
        else:
            failures.append(name + ": missing")

    if spec.get("no_artifacts"):
        strays = [
            n for n in os.listdir(directory)
            if n.endswith((".bak", ".ecmeta.json")) and n not in spec["bak_dirs"]
        ]

        if strays:
            failures.append(
                "nothing should have been written, but found: " + ", ".join(strays))
        else:
            print("  ok   %-18s no backups or records written" % "(folder)")

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
