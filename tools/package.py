"""Builds the module and packs the release.

    python tools/package.py ["C:/path/to/Mount & Blade II Bannerlord"]

Output (release/ is not in git):
    release/v<version>/ColoredPoliticalRegionsBorders/   the module folder (also what goes to the Workshop)
    release/v<version>/ColoredPoliticalRegionsBorders_Bannerlord_v<version>.zip
        ColoredPoliticalRegionsBorders/SubModule.xml
        ColoredPoliticalRegionsBorders/bin/Win64_Shipping_Client/ColoredPoliticalRegionsBorders.dll
        HOW_TO_INSTALL.txt
    release/workshop/WorkshopCreate.xml, WorkshopUpdate.xml, upload_to_workshop.bat, update_workshop.bat
        for TaleWorlds.MountAndBlade.SteamWorkshop.exe (in the game's bin folder)
"""
import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = ROOT
MODULE_ID = "ColoredPoliticalRegionsBorders"
GAME = sys.argv[1] if len(sys.argv) > 1 else r"Z:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"


def main():
    submodule = os.path.join(PROJECT, "Module", "SubModule.xml")
    version = re.search(r'<Version value="v([\d.]+)"', open(submodule, encoding="utf-8").read()).group(1)
    subprocess.run(["dotnet", "build", "-c", "Release", "-nologo", "-p:BannerlordDir=" + GAME], cwd=PROJECT, check=True)

    out = os.path.join(ROOT, "release", "v" + version)
    module = os.path.join(out, MODULE_ID)
    if os.path.isdir(module):
        shutil.rmtree(module)
    os.makedirs(os.path.join(module, "bin", "Win64_Shipping_Client"))
    shutil.copy2(submodule, os.path.join(module, "SubModule.xml"))
    shutil.copy2(os.path.join(PROJECT, "bin", "Release", MODULE_ID + ".dll"),
                 os.path.join(module, "bin", "Win64_Shipping_Client", MODULE_ID + ".dll"))

    zip_path = os.path.join(out, "%s_Bannerlord_v%s.zip" % (MODULE_ID, version))
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for folder, _, files in os.walk(module):
            for name in files:
                full = os.path.join(folder, name)
                z.write(full, os.path.relpath(full, out))
        z.write(os.path.join(ROOT, "docs", "store", "HOW_TO_INSTALL.txt"), "HOW_TO_INSTALL.txt")
    print("module:", module)
    print("zip:   ", zip_path, os.path.getsize(zip_path), "bytes")
    workshop(module, version)


# ---- Steam Workshop: files for TaleWorlds.MountAndBlade.SteamWorkshop.exe ------------------------------
# https://moddocs.bannerlord.com/steam-workshop/uploading_updating_mod/
GAME_VERSION = "v1.4.8"
TAGS = ["Utility", "UI", "Native", "Singleplayer", GAME_VERSION]


def xml_value(s):
    return (s.replace("&", "&amp;").replace('"', "&quot;").replace("<", "&lt;").replace(">", "&gt;")
            .replace("\r", "").replace("\n", "&#10;"))


def workshop(module, version):
    out = os.path.join(ROOT, "release", "workshop")
    os.makedirs(out, exist_ok=True)
    image = os.path.join(ROOT, "docs", "images", "workshop_preview.jpg")
    description = open(os.path.join(ROOT, "docs", "store", "workshop_description.txt"), encoding="utf-8").read()
    links = dict(l.strip().split("=", 1) for l in open(os.path.join(ROOT, "docs", "store", "links.txt"), encoding="utf-8") if "=" in l and not l.startswith("#"))
    for key, value in links.items():
        description = description.replace("{" + key + "}", value)
    tags = "\n".join('\t\t\t<Tag Value="%s"/>' % t for t in TAGS)
    create = """<Tasks>
\t<CreateItem/>
\t<UpdateItem>
\t\t<ModuleFolder Value="%s"/>
\t\t<ItemDescription Value="%s"/>
\t\t<Tags>
%s
\t\t</Tags>
\t\t<Image Value="%s"/>
\t\t<Visibility Value="Public"/>
\t</UpdateItem>
</Tasks>
""" % (xml_value(module), xml_value(description.strip()), tags, xml_value(image))
    open(os.path.join(out, "WorkshopCreate.xml"), "w", encoding="utf-8").write(create)
    update = """<Tasks>
\t<GetItem>
\t\t<ItemId Value="%s"/>
\t</GetItem>
\t<UpdateItem>
\t\t<ModuleFolder Value="%s"/>
\t\t<ChangeNotes Value="%s"/>
\t\t<Tags>
%s
\t\t</Tags>
\t</UpdateItem>
</Tasks>
""" % (links.get("WORKSHOP_ID", "PUT_THE_ITEM_ID_HERE"), xml_value(module), xml_value("v" + version), tags)
    open(os.path.join(out, "WorkshopUpdate.xml"), "w", encoding="utf-8").write(update)
    tool = os.path.join(GAME, "bin", "Win64_Shipping_Client", "TaleWorlds.MountAndBlade.SteamWorkshop.exe")
    for name, xml in (("upload_to_workshop.bat", "WorkshopCreate.xml"), ("update_workshop.bat", "WorkshopUpdate.xml")):
        bat = ("@echo off\r\n"
               "rem Sends the mod to the Steam Workshop with TaleWorlds' uploader. Steam must be running.\r\n"
               "cd /d \"%s\"\r\n"
               "TaleWorlds.MountAndBlade.SteamWorkshop.exe \"%s\"\r\n"
               "pause\r\n") % (os.path.dirname(tool), os.path.join(out, xml))
        open(os.path.join(out, name), "w", encoding="utf-8").write(bat)
    print("workshop:", out)


if __name__ == "__main__":
    main()
