"""
Checks that the mod (C#) and the bot (Python) give the same tune code.

Run it after changing src/TuneFingerprint.cs, src/TuneFile.cs, src/TuneRules.cs,
src/KndFile.cs or tools/tune_code.py:

    python tools/check_codes.py
    python tools/check_codes.py "<CarX>/kino/mods/KN_Base/tunes"    (also check real KINO tunes)

It writes a few hundred random .trctune files and KINO .knd files, including tricky values
(rounding ties, negative zero, huge numbers, badly written numbers, damaged files), gets each
file's code from both versions, and lists any file where they disagree. The project's own
tunes/ folder is checked too, plus any folders, .knd files or .zip files given on the command
line. It also runs the C# self-test of the "Check my current tune" logic.
"""
import os
import random
import shutil
import struct
import subprocess
import sys
import tempfile
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import tune_code  # noqa: E402

ROOT = os.path.dirname(HERE)
CHECKER_PROJECT = os.path.join(HERE, "codecheck", "CodeCheck.csproj")
CHECKER_EXE = os.path.join(HERE, "codecheck", "bin", "Release", "CodeCheck.exe")

FILE_COUNT = 400
SEED = 20261021

KEYS = [
    "front_spring_way", "front_spring_stiffness", "front_fast_bump", "front_camber", "front_toe",
    "front_caster", "front_tyre_pressure", "rear_spring_stiffness", "rear_camber", "rear_toe",
    "brake_front_percent", "diff_lock_coef", "final_drive", "gearbox1", "mass",
    "wastegate_sound", "seq_transmision_sound",
]
IDS = ["3", "119", "0", "007", "  42 ", "", "-1", "abc", "1234567890", "999999999", "1_0", "+5", "٣"]
TRICKY = [
    "0", "-0.0", "0.0005", "-0.0005", "0.0004", "-0.0004", "0.0625", "-0.0625", "1.0005", "2.5e-4",
    "1E3", "+1.5", ".5", "5.", "123456.789", "-98.7654321", "0.1", "0.2", "0.3", "0.1234999",
    "3.4028235e38", "1e39", "1e400", "1e-50", "999999999", "1000000000", "nan", "NaN", "inf",
    "Infinity", "-Infinity", "1_000", "1,5", "abc", "1e", "0x10", "1.2.3", "--1", "١٢",
]
TRICKY_FLOATS = [
    0.0, -0.0, 0.0005, -0.0005, 0.0625, -0.0625, 1.0005, 0.1, 1e-45, 3.4e38, 1e9, 999999.9,
    float("nan"), float("inf"), float("-inf"),
]


def random_value(rng):
    pick = rng.random()
    if pick < 0.2:
        return rng.choice(TRICKY)
    if pick < 0.35:
        # Written with a 5 in the 4th decimal, so the text itself sits on a rounding tie.
        return f"{rng.uniform(-5, 5):.3f}5"
    if pick < 0.6:
        return repr(rng.uniform(-200, 200))
    if pick < 0.8:
        return f"{rng.uniform(-1, 1):.{rng.randint(0, 7)}f}"
    return f"{rng.uniform(0, 50000):.{rng.randint(0, 4)}f}"


def random_key(rng):
    pick = rng.random()
    if pick < 0.85:
        return rng.choice(KEYS)
    if pick < 0.9:
        return rng.choice(KEYS).upper()
    if pick < 0.95:
        return "x" * rng.randint(120, 300)  # long enough for a 2-byte string length in .knd files
    return "  " + rng.choice(KEYS) + "\t"


def write_random_tunes(folder, rng):
    for i in range(FILE_COUNT):
        lines = ["[info]", f"name = Random {i}", f"car_id = {rng.choice(IDS)}", f"slot_id = {rng.choice(IDS)}", "", "[values]"]
        for key in rng.sample(KEYS, rng.randint(1, len(KEYS))):
            comment = "   # note" if rng.random() < 0.1 else ""
            lines.append(f"{key} = {random_value(rng)}{comment}")
            if rng.random() < 0.05:
                lines.append(f"{key.upper()} = {random_value(rng)}")  # duplicate name: last one wins
        if rng.random() < 0.05:
            lines.append("[")  # a lone bracket isn't a section header
        with open(os.path.join(folder, f"random_{i:03d}.trctune"), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")


def knd_string(text):
    data = text.encode("utf-8")
    length, prefix = len(data), bytearray()
    while True:
        byte = length & 0x7F
        length >>= 7
        prefix.append(byte | (0x80 if length else 0))
        if not length:
            return bytes(prefix) + data


def random_float(rng):
    if rng.random() < 0.2:
        return rng.choice(TRICKY_FLOATS)
    return rng.uniform(-0.2, 1.2)


def random_knd(rng):
    entries = [(random_key(rng), random_float(rng)) for _ in range(rng.randint(0, 40))]
    car_id = rng.choice([3, 19, 119, 0, -5, rng.randint(1, 10 ** 6)])
    slot_id = rng.choice([119, 135, 839, 0, rng.randint(1, 10 ** 6)])

    data = bytearray(b"\x00\x02\x22\x03")
    data += struct.pack("<ii", car_id, slot_id)
    data += knd_string(rng.choice(["Ultimate", "UltimatePro", ""]))
    data += struct.pack("<i", len(entries) if rng.random() > 0.03 else -1)
    for key, value in entries:
        data += knd_string(key) + struct.pack("<f", value)
    if rng.random() < 0.8:
        data += struct.pack("<ii", 0, 2) + knd_string("final_drive") + struct.pack("<f", 0.3)
        data += knd_string("gearbox1") + struct.pack("<f", 0.25)
        data += b"\xff\xff\xff\xff" + bytes(16)
    if rng.random() < 0.1:
        data = data[:rng.randint(0, len(data))]  # damaged file
    return bytes(data)


def write_random_knds(folder, rng):
    for i in range(FILE_COUNT):
        with open(os.path.join(folder, f"random_{i:03d}.knd"), "wb") as f:
            f.write(random_knd(rng))


def copy_extra(paths, folder):
    """Copies .knd and .trctune files from the given folders, files and zips into the test folder."""
    count = 0
    for path in paths:
        sources = []
        if os.path.isdir(path):
            for dirpath, _, names in os.walk(path):
                sources += [os.path.join(dirpath, n) for n in names]
        else:
            sources.append(path)
        for source in sources:
            lower = source.lower()
            if lower.endswith(".zip"):
                with zipfile.ZipFile(source) as archive:
                    for entry in archive.infolist():
                        if entry.filename.lower().endswith(".knd"):
                            count += 1
                            with open(os.path.join(folder, f"extra_{count:04d}.knd"), "wb") as f:
                                f.write(archive.read(entry))
            elif lower.endswith(".knd") or lower.endswith(".trctune"):
                count += 1
                shutil.copyfile(source, os.path.join(folder, f"extra_{count:04d}{os.path.splitext(lower)[1]}"))
    return count


def build_checker():
    subprocess.run(
        ["dotnet", "build", CHECKER_PROJECT, "-c", "Release", "-nologo", "-v", "quiet"],
        check=True, stdout=subprocess.DEVNULL,
    )


def csharp_codes(folders):
    out = subprocess.run([CHECKER_EXE, *folders], check=True, capture_output=True, text=True, encoding="utf-8").stdout
    return dict(line.split("\t") for line in out.splitlines() if line)


def python_codes(folders):
    codes = {}
    for folder in folders:
        for dirpath, _, names in os.walk(folder):
            for name in names:
                path = os.path.join(dirpath, name)
                if name.endswith(".trctune"):
                    codes[name] = tune_code.trctune_code(path)
                elif name.endswith(".knd"):
                    with open(path, "rb") as f:
                        try:
                            codes[name] = tune_code.knd_code(f.read())
                        except ValueError:
                            codes[name] = "SKIP"
    return codes


def main():
    build_checker()
    selftest = subprocess.run([CHECKER_EXE, "--selftest"], capture_output=True, text=True)
    print(selftest.stdout.strip())

    temp = tempfile.mkdtemp(prefix="trc_codecheck_")
    try:
        rng = random.Random(SEED)
        write_random_tunes(temp, rng)
        write_random_knds(temp, rng)
        extra = copy_extra(sys.argv[1:], temp)
        folders = [temp, os.path.join(ROOT, "tunes")]
        cs, py = csharp_codes(folders), python_codes(folders)
    finally:
        shutil.rmtree(temp, ignore_errors=True)

    mismatches = [name for name in sorted(py) if cs.get(name) != py[name]]
    missing = sorted(set(cs) ^ set(py))
    for name in mismatches:
        print(f"DIFFERENT  {name}: C# {cs.get(name)}  Python {py[name]}")
    for name in missing:
        print(f"ONLY ONE SIDE  {name}")

    skipped = sum(1 for code in py.values() if code == "SKIP")
    extra_text = f", including {extra} real file(s)" if extra else ""
    if mismatches or missing or selftest.returncode != 0:
        print(f"FAILED: {len(mismatches) + len(missing)} of {len(py)} files disagree{extra_text}.")
        return 1
    print(f"OK: all {len(py)} files give the same code in C# and Python{extra_text} "
          f"({skipped} damaged .knd files rejected by both).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
