"""
Python version of the TRC tune code (TuneFingerprint.cs, TuneFile.cs, TuneRules.cs, KndFile.cs).

The Discord bot will use this to check codes that drivers submit, so it must produce exactly
the same code as the mod. Run it on .trctune files, KINO .knd files, or .zip files of .knd files:

    python tools/tune_code.py tunes/_EXAMPLE.trctune
    python tools/tune_code.py "TRC TUNES V2.1.zip"
    python tools/tune_code.py --csv official_tunes > docs/official-codes.csv

After changing anything here or in those C# files, run: python tools/check_codes.py
"""
import hashlib
import math
import os
import re
import struct
import sys
import zipfile
from decimal import Decimal, ROUND_HALF_UP

DECIMALS = 3

# Same as TuneRules.MaxValue: bigger values could round or print differently in C#.
MAX_VALUE = 1_000_000_000

# Same as TuneFingerprint.IgnoredKeys: sound-only settings don't count toward the code.
IGNORED_KEYS = {"seq_transmision_sound", "wastegate_sound"}

# Same as TuneRules: only ASCII is trimmed and lowercased.
WHITESPACE = " \t\r\n\f\v"
ASCII_LOWER = str.maketrans("ABCDEFGHIJKLMNOPQRSTUVWXYZ", "abcdefghijklmnopqrstuvwxyz")

# Numbers C# accepts with NumberStyles.Float: optional sign, digits with an optional
# dot, optional exponent. Python's float() also takes "1_000", "inf" and "nan"; C# doesn't.
NUMBER = re.compile(r"[+-]?(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?")
ID = re.compile(r"[0-9]{1,9}")

KND_MAX_ENTRIES = 10000


def trim(text: str) -> str:
    return (text or "").strip(WHITESPACE)


def key_of(text: str) -> str:
    return trim(text).translate(ASCII_LOWER)


def normalize_id(text: str) -> str:
    """A car or setup ID as plain digits without leading zeros, or "" (TuneRules.Id)."""
    text = trim(text)
    return str(int(text)) if ID.fullmatch(text) else ""


def is_usable(value: float) -> bool:
    return math.isfinite(value) and abs(value) < MAX_VALUE


def parse_number(text: str):
    """The value as C#'s float.TryParse would store it, or None where TuneFile.cs skips the line."""
    if not NUMBER.fullmatch(text):
        return None
    try:
        value = struct.unpack("f", struct.pack("f", float(text)))[0]
    except OverflowError:
        return None
    return value if is_usable(value) else None


def format_value(value: float) -> str:
    # Mirrors Math.Round(value, 3, MidpointRounding.AwayFromZero) then "F3".
    # C# rounds the float after widening it to double, so use repr of the float32 value.
    as_double = struct.unpack("f", struct.pack("f", value))[0]
    rounded = Decimal(repr(as_double)).quantize(Decimal(1).scaleb(-DECIMALS), rounding=ROUND_HALF_UP)
    if rounded == 0:
        rounded = abs(rounded)
    return f"{rounded:.{DECIMALS}f}"


def compute(car_id: str, slot_id: str, values: dict) -> str:
    text = f"car={car_id}\nslot={slot_id}\n"
    # StringComparer.Ordinal compares UTF-16 code units, so sort by those.
    for key in sorted(values, key=lambda k: k.encode("utf-16-be", "surrogatepass")):
        if key not in IGNORED_KEYS:
            text += f"{key}={format_value(values[key])}\n"
    digest = hashlib.sha256(text.encode("utf-8")).hexdigest().upper()
    return f"TRC-{digest[0:4]}-{digest[4:8]}"


# ---- .trctune files ----

def load(path: str):
    """Reads a .trctune file like TuneFile.Load: returns (info, values)."""
    info, values, section = {}, {}, ""
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        for raw in f:
            line = trim(raw.split("#", 1)[0])
            if not line:
                continue
            if len(line) >= 2 and line.startswith("[") and line.endswith("]"):
                section = key_of(line[1:-1])
                continue
            if "=" not in line or line.index("=") == 0:
                continue
            key, value = line.split("=", 1)
            key, value = key_of(key), trim(value)
            if section == "info":
                info[key] = value
            elif section == "values":
                number = parse_number(value)
                if number is not None:
                    values[key] = number
    return info, values


def trctune_code(path: str) -> str:
    info, values = load(path)
    return compute(normalize_id(info.get("car_id", "")), normalize_id(info.get("slot_id", "")), values)


# ---- KINO .knd files (KndFile.cs) ----

class _Reader:
    """Reads like .NET's BinaryReader; raises ValueError where it would throw."""

    def __init__(self, data: bytes):
        self.data, self.pos = data, 0

    def take(self, count: int) -> bytes:
        if count < 0 or self.pos + count > len(self.data):
            raise ValueError("the file ends too early")
        chunk = self.data[self.pos:self.pos + count]
        self.pos += count
        return chunk

    def int32(self) -> int:
        return struct.unpack("<i", self.take(4))[0]

    def float32(self) -> float:
        return struct.unpack("<f", self.take(4))[0]

    def string(self) -> str:
        length, shift = 0, 0
        while True:
            if shift == 35:
                raise ValueError("bad string length")
            byte = self.take(1)[0]
            length |= (byte & 0x7F) << shift
            shift += 7
            if not byte & 0x80:
                break
        length &= 0xFFFFFFFF
        if length >= 2 ** 31:
            raise ValueError("bad string length")
        return self.take(length).decode("utf-8", errors="replace")


def _read_values(reader: _Reader) -> dict:
    count = reader.int32()
    if count < 0 or count > KND_MAX_ENTRIES:
        raise ValueError(f"unexpected number of settings ({count})")
    values = {}
    for _ in range(count):
        key = key_of(reader.string())
        value = reader.float32()
        if key and is_usable(value):
            values[key] = value
    return values


def read_knd(data: bytes) -> dict:
    """Reads a KINO .knd tune like KndFile.Read. Raises ValueError when it isn't one."""
    reader = _Reader(data)
    try:
        reader.take(4)
        car_id, slot_id = reader.int32(), reader.int32()
        slot = reader.string()
        values = _read_values(reader)
    except ValueError as e:
        raise ValueError(f"damaged or not a KINO tune ({e})") from e
    if car_id <= 0 or slot_id <= 0:
        raise ValueError("no valid car or setup ID: not a KINO tune")

    engine_id = -1
    try:
        candidate = reader.int32()
        _read_values(reader)
        engine_id = candidate
    except ValueError:
        pass

    return {"car_id": car_id, "slot_id": slot_id, "slot": slot, "engine_id": engine_id, "values": values}


def knd_code(data: bytes) -> str:
    knd = read_knd(data)
    return compute(str(knd["car_id"]), str(knd["slot_id"]), knd["values"])


def _knd_line(name: str, data: bytes) -> str:
    try:
        knd = read_knd(data)
    except ValueError as e:
        return f"{name}: skipped, {e}"
    code = compute(str(knd["car_id"]), str(knd["slot_id"]), knd["values"])
    return f"{name}: {code}  (car {knd['car_id']}, {knd['slot']} {knd['slot_id']}, {len(knd['values'])} values)"


def official_codes_csv(root: str) -> str:
    """
    CSV of every official tune: set, file, car_id, slot_id, slot, code. For the bot's list of official codes.
    root is official_tunes/: one subfolder per set (the folder name is the set ID, see TuneSets.cs).
    Some sets reuse a tune unchanged for a car, so a code can appear under more than one set: the
    tune is identical, and the mod names the newest set that has it (TuneSets order).
    """
    lines = ["set,file,car_id,slot_id,slot,code"]
    for tune_set in sorted(os.listdir(root), key=str.lower):
        folder = os.path.join(root, tune_set)
        if not os.path.isdir(folder):
            continue
        for name in sorted(os.listdir(folder), key=str.lower):
            if not name.lower().endswith(".knd"):
                continue
            with open(os.path.join(folder, name), "rb") as f:
                knd = read_knd(f.read())
            code = compute(str(knd["car_id"]), str(knd["slot_id"]), knd["values"])
            lines.append(f"{tune_set},{name},{knd['car_id']},{knd['slot_id']},{knd['slot']},{code}")
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--csv":
        sys.stdout.write(official_codes_csv(sys.argv[2]))
        sys.exit(0)

    for path in sys.argv[1:]:
        lower = path.lower()
        if lower.endswith(".zip"):
            with zipfile.ZipFile(path) as archive:
                for entry in archive.infolist():
                    if entry.filename.lower().endswith(".knd"):
                        print(_knd_line(f"{os.path.basename(path)}!{entry.filename}", archive.read(entry)))
        elif lower.endswith(".knd"):
            with open(path, "rb") as f:
                print(_knd_line(path, f.read()))
        else:
            info, _ = load(path)
            code = trctune_code(path)
            published = info.get("code", "")
            status = "" if not published else ("  (matches published code)" if published.upper() == code else f"  (published code is {published})")
            print(f"{path}: {code}{status}")
