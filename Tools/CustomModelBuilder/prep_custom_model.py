"""Lay out a fake game/update dump = vanilla armor + Linkle overrides, so Milk Bar's
own GameFilesModifier.CreateModifiedModel() builds a Linkle version of the remote
player model (body split + all MP_Armor_* units + textures)."""
import json, shutil, sys
from pathlib import Path
import oead
W = Path(sys.argv[1])
G = Path(r"D:\GameFolder\WIIU\CEMU_1.26.2\game")
BASE = next((G / "Base Games").iterdir()) / "content"
UPD = next(p for p in (G / "Updates and DLC").iterdir() if "UPDATE" in p.name) / "content"
LK = Path(r"D:\GameFolder\WIIU\Mods\Linkle\bnp\content")
gm, um = W / "game" / "Model", W / "update" / "Model"
for d in (gm, um, W / "update" / "Pack", W / "store" / "merged" / "content" / "Model"):
    d.mkdir(parents=True, exist_ok=True)
for f in BASE.joinpath("Model").glob("Armor_*Tex1.sbfres"): shutil.copy2(f, gm / f.name)
for f in UPD.joinpath("Model").glob("Armor_*"): shutil.copy2(f, um / f.name)
n = 0
for f in LK.joinpath("Model").glob("*.sbfres"):
    if f.name.startswith("Armor_"):
        n += 1
        # each texture file must exist in exactly one of game/update (launcher adds textures with ResDict.Add)
        if "Tex1" in f.name and (gm / f.name).exists(): shutil.copy2(f, gm / f.name)
        else: shutil.copy2(f, um / f.name)
shutil.copy2(LK / "Model" / "Link.Tex1.sbfres", gm / "Link.Tex1.sbfres")
print("linkle armor files:", n)
# TitleBG = vanilla update TitleBG with Linkle's replaced entries
lk_tbg = oead.Sarc(LK.joinpath("Pack", "TitleBG.pack").read_bytes())
van = oead.Sarc(UPD.joinpath("Pack", "TitleBG.pack").read_bytes())
w = oead.SarcWriter.from_sarc(van)
for f in lk_tbg.get_files():
    w.files[f.name] = bytes(f.data); print("  TitleBG <-", f.name)
# also expose Linkle's Hylian set (lives in TitleBG) as a Model file so it gets MP_Armor_001_* units
(um / "Armor_001.sbfres").write_bytes(bytes(lk_tbg.get_file("Model/Armor_001.sbfres").data))
(W / "update" / "Pack" / "TitleBG.pack").write_bytes(bytes(w.write()[1]))
st = W / "store"
(st / "settings.json").write_text(json.dumps({"game_dir": str(W / "game"), "update_dir": str(W / "update"),
    "store_dir": str(st), "cemu_dir": r"D:\GameFolder\WIIU\CEMU_1.26.2"}, indent=2))
print("game Model:", len(list(gm.iterdir())), "update Model:", len(list(um.iterdir())))
