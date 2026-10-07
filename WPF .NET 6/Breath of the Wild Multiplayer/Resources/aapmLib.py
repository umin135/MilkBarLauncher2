"""MilkBar2 aapmLib (build: pyinstaller --onefile aapmLib.py, needs `pip install oead`).

The original (aamp lib, set_param(name, str)) rewrote every edited value as a plain
string parameter. The game expects e.g. ModelList Folder/UnitName as FixedSafeString64,
so a changed player model was ignored (still Link) or broke loading (infinite load).
This version applies the same instructions but keeps each parameter's original type.

I/O contract (unchanged): %APPDATA%\\BOTWM\\Temp\\AampTemp.txt
  in : [{"Data": "<hex bytes>", "Instruction": "l(param_root).l(..).o(..).v(Name,Value);..."}]
  out: ["<hex bytes>", ...]
"""
import json, os, sys
import oead

STRING_TYPES = {
    oead.aamp.Parameter.Type.String32: oead.FixedSafeString32,
    oead.aamp.Parameter.Type.String64: oead.FixedSafeString64,
    oead.aamp.Parameter.Type.String256: oead.FixedSafeString256,
}


def arg(action):
    return action[action.index("(") + 1:action.rindex(")")]


# Parameters Milk Bar edits that the game reads as FixedSafeString64. Files already
# damaged by the original aapmLib hold them as StringRef, so repair those too.
STR64_NAMES = {"Folder", "UnitName", "BindBone", "AttentionUser"}


def convert(old, value, name=""):
    t = old.type
    if t == oead.aamp.Parameter.Type.StringRef and name in STR64_NAMES:
        return oead.FixedSafeString64(value)
    if t in STRING_TYPES:
        return STRING_TYPES[t](value)
    if t == oead.aamp.Parameter.Type.StringRef:
        return value
    if t == oead.aamp.Parameter.Type.Bool:
        return value.strip().lower() in ("1", "true")
    if t == oead.aamp.Parameter.Type.Int:
        return int(value)
    if t == oead.aamp.Parameter.Type.U32:
        return oead.U32(int(value))
    if t == oead.aamp.Parameter.Type.F32:
        return oead.F32(float(value))
    return oead.FixedSafeString64(value)


def apply(pio, instructions):
    for instruction in filter(None, instructions.split(";")):
        cur = pio
        for action in instruction.split("."):
            if action.startswith("l"):
                name = arg(action)
                if name != "param_root":  # the ParameterIO itself is param_root
                    cur = cur.lists[name]
            elif action.startswith("o"):
                cur = cur.objects[arg(action)]
            elif action.startswith("v"):
                name, value = arg(action).split(",", 1)
                old = cur.params[name] if name in cur.params else None
                new = convert(old, value, name) if old is not None else oead.FixedSafeString64(value)
                cur.params[name] = oead.aamp.Parameter(new)


import re

ARMOR_RE = re.compile(r"^Armor_(\d{3}|Default)$")


def custom_model_batch(data):
    """Patched launcher: for an MP_<name> model the local player is set up like Link
    (armor actors keep their own Armor_* names) and only the body gets Folder=MP_<name>.
    Returns MP_<name> if this batch is such a setup, else None."""
    for item in data:
        m = re.search(r"\.v\(Folder,(MP_[^,;)]+)\)", item["Instruction"])
        if m:
            return m.group(1)
    return None


def load_units(folder):
    base = os.path.dirname(sys.executable) if getattr(sys, "frozen", False) else os.path.dirname(os.path.abspath(__file__))
    p = os.path.join(base, folder + ".units.txt")
    if not os.path.exists(p):
        return None
    with open(p, encoding="utf-8") as f:
        return {l.strip() for l in f if l.strip()}


def remap(instruction, model, units):
    """Armor_* pieces -> the same piece of the custom model (<model>_MP holds MP_Armor_* units,
    <model> holds the default extras). Unknown pieces keep the vanilla Link model."""
    m = re.search(r"\.v\(Folder,([^,;)]+)\)", instruction)
    u = re.search(r"\.v\(UnitName,([^,;)]+)\)", instruction)
    if not m or not u or not ARMOR_RE.match(m.group(1)):
        return instruction
    folder, unit = m.group(1), u.group(1)
    if unit.startswith("Armor_Default_Extra_"):
        new_folder, new_unit = model, unit
    else:
        new_folder, new_unit = model + "_MP", "MP_" + unit.replace("_A", "").replace("_B", "")
        if units is not None and new_unit not in units:
            return instruction
    return (instruction.replace(f".v(Folder,{folder})", f".v(Folder,{new_folder})")
                       .replace(f".v(UnitName,{unit})", f".v(UnitName,{new_unit})"))


def main():
    tmp = os.path.join(os.environ["APPDATA"], "BOTWM", "Temp")
    path = os.path.join(tmp, "AampTemp.txt")
    with open(path, "r", encoding="utf-8-sig") as f:
        data = json.load(f)
    out, errors = [], []
    model = custom_model_batch(data)
    units = load_units(model + "_MP") if model else None
    for i, item in enumerate(data):
        raw = bytes.fromhex(item["Data"])
        try:
            pio = oead.aamp.ParameterIO.from_binary(raw)
            instruction = remap(item["Instruction"], model, units) if model else item["Instruction"]
            apply(pio, instruction)
            raw = bytes(pio.to_binary())
        except Exception as e:  # keep this file untouched rather than failing the whole batch
            errors.append(f"[{i}] {type(e).__name__}: {e} | {item['Instruction'][:300]}")
        out.append(" ".join(format(x, "02x") for x in raw))
    with open(path, "w", encoding="utf-8") as f:
        f.write(json.dumps(out, indent=4))
    with open(os.path.join(tmp, "aapmLib_fixed_detail.log"), "a", encoding="utf-8") as f:
        f.write(f"batch of {len(data)} files, {len(errors)} left unchanged\n" + "".join(e + "\n" for e in errors))


if __name__ == "__main__":
    main()
