"""Install video for the Bannerlord version (manual install from Nexus Mods / GitHub), with EN/TR captions.

    python tools/make_video.py [repo address shown in the video] [--stills]

Writes docs/video/cprb_bannerlord_install.mp4 (1920 x 1080, 30 fps). --stills writes a few frames to
docs/video/stills/ instead, to check the layout quickly. Needs Pillow and imageio-ffmpeg.
The game shots are the real screenshots in docs/screens/.
"""
import os
import re
import sys
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import video_ui as v
from video_ui import W, H, ui, ui_bold, glyph, text, ramp, smooth, Path

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHOTS = os.path.join(ROOT, "docs", "screens")
OUT = os.path.join(ROOT, "docs", "video")
FPS = 30
FADE = 0.35

REPO = "github.com/champierr/Colored-Political-Regions-Borders-Bannerlord"
VERSION = re.search(r'<Version value="v([\d.]+)"', open(os.path.join(ROOT, "Module", "SubModule.xml"), encoding="utf-8").read()).group(1)
ZIP = "ColoredPoliticalRegionsBorders_Bannerlord_v%s.zip" % VERSION
EXTRACTED = "ColoredPoliticalRegionsBorders_Bannerlord_v%s" % VERSION
FOLDER = "ColoredPoliticalRegionsBorders"
DATE = "10/30/2026 10:20"
GOLD = (226, 190, 110)
PARCHMENT = (240, 230, 210)


def title_font(size):
    return v.font("BOOKOSB.TTF", size)


def serif(size):
    return v.font("georgia.ttf", size)


_shots = {}


def shot(name):
    if name not in _shots:
        _shots[name] = Image.open(os.path.join(SHOTS, name)).convert("RGB").resize((W, H))
    return _shots[name]


def zoomed(img, scale, cx=0.5, cy=0.5):
    """img scaled by scale (>= 1) around (cx, cy), cropped back to W x H."""
    if scale <= 1.0001:
        return img
    w, h = int(W * scale), int(H * scale)
    big = img.resize((w, h), Image.BILINEAR)
    x = int((w - W) * cx)
    y = int((h - H) * cy)
    return big.crop((x, y, x + W, y + H))


_desk = None


def desk():
    global _desk
    if _desk is None:
        _desk = v.desktop()
    return _desk.copy()


class Cache:
    """Base frames by state key, so blurred shadows and windows are drawn once per state."""

    def __init__(self):
        self.items = {}

    def get(self, key, make):
        if key not in self.items:
            if len(self.items) > 40:
                self.items.clear()
            self.items[key] = make()
        return self.items[key].copy()


cache = Cache()


# ---- captions ---------------------------------------------------------------------------------

_band = None


def caption(img, step, en, tr):
    global _band
    if _band is None:
        _band = Image.new("RGBA", (W, 190), (0, 0, 0, 0))
        bd = ImageDraw.Draw(_band)
        for i in range(190):
            bd.line((0, i, W, i), fill=(8, 10, 16, int(215 * min(1.0, i / 70))))
    img.paste(_band, (0, H - 190), _band)
    d = ImageDraw.Draw(img)
    if step:
        d.rounded_rectangle((60, H - 150, 60 + 64, H - 86), 14, fill=GOLD)
        text(d, (92, H - 118), step, ui_bold(36), (30, 22, 10), "mm")
    x = 150 if step else W // 2
    anchor = "lm" if step else "mm"
    text(d, (x, H - 132), en, ui_bold(36), (250, 245, 235), anchor)
    text(d, (x, H - 80), tr, ui(30), (215, 205, 185), anchor)


def game_message(img, s, alpha=1.0):
    """The yellow message line at the bottom left of the campaign map."""
    layer = Image.new("RGBA", (1100, 60), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.text((2, 3), s, font=v.font("georgia.ttf", 26), fill=(0, 0, 0, int(200 * alpha)))
    d.text((0, 0), s, font=v.font("georgia.ttf", 26), fill=(232, 210, 122, int(255 * alpha)))
    img.paste(layer, (48, 860), layer)


# ---- scene 0: title ---------------------------------------------------------------------------

def card_background():
    bg = shot("overview.jpg").filter(ImageFilter.GaussianBlur(7))
    return ImageEnhance.Brightness(bg).enhance(0.38)


def s_title(t):
    img = cache.get("title", card_background)
    d = ImageDraw.Draw(img)
    text(d, (W // 2, 330), "Colored Political Regions & Borders", title_font(86), GOLD, "mm")
    text(d, (W // 2, 440), "Mount & Blade II: Bannerlord", serif(52), PARCHMENT, "mm")
    text(d, (W // 2, 560), "How to install  \u2022  Nexus Mods / GitHub download", ui(40), (235, 228, 214), "mm")
    text(d, (W // 2, 616), "Kurulum  \u2022  Nexus Mods / GitHub indirmesi", ui(34), (210, 200, 180), "mm")
    if t < 0.6:
        img = Image.blend(Image.new("RGB", (W, H)), img, smooth(t / 0.6))
    return img


# ---- scene 1: download in the browser -----------------------------------------------------------

BROWSER = (150, 50, 1770, 890)
DL_BUTTON = (1400, 452, 1560, 500)
BUBBLE = (1290, 156, 1740, 300)
SHOW_FOLDER = (1670, 222, 1716, 262)


def browser_base(downloading):
    img = desk()
    x0, y0, x1, y1 = BROWSER
    v.shadow(img, BROWSER, 10)
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle(BROWSER, 10, fill=(255, 255, 255), outline=(195, 198, 205))
    d.rounded_rectangle((x0, y0, x1, y0 + 44), 10, fill=(222, 226, 233))
    d.rectangle((x0, y0 + 30, x1, y0 + 44), fill=(222, 226, 233))
    d.rounded_rectangle((x0 + 12, y0 + 8, x0 + 430, y0 + 44), 9, fill=(255, 255, 255))
    glyph(d, (x0 + 32, y0 + 26), "star", 13, (200, 140, 40))
    text(d, (x0 + 50, y0 + 26), "Colored Political Regions & Borders", ui(15), (30, 30, 30), "lm")
    glyph(d, (x0 + 412, y0 + 26), "close", 9, (90, 90, 90))
    glyph(d, (x0 + 452, y0 + 26), "add", 12, (90, 90, 90))
    for i, g in enumerate(("minimize", "maximize", "close")):
        glyph(d, (x1 - 130 + i * 46, y0 + 22), g, 12, (40, 40, 40))
    ty = y0 + 44
    for i, g in enumerate(("back", "forward", "refresh")):
        glyph(d, (x0 + 30 + i * 40, ty + 28), g, 15, (70, 70, 70) if i != 1 else (170, 170, 170))
    d.rounded_rectangle((x0 + 140, ty + 8, x1 - 120, ty + 48), 20, fill=(240, 242, 246))
    glyph(d, (x0 + 166, ty + 28), "lock", 13, (70, 70, 70))
    text(d, (x0 + 186, ty + 28), REPO + "/releases", ui(16), (40, 40, 40), "lm")
    if downloading:
        d.ellipse((x1 - 100, ty + 8, x1 - 60, ty + 48), fill=(220, 232, 250))
    glyph(d, (x1 - 80, ty + 28), "download", 17, (20, 90, 200) if downloading else (70, 70, 70))
    glyph(d, (x1 - 36, ty + 28), "more", 15, (70, 70, 70))
    d.line((x0 + 1, ty + 56, x1 - 1, ty + 56), fill=(225, 225, 230))
    # page
    py = ty + 57
    d.rectangle((x0 + 1, py, x1 - 1, py + 70), fill=(36, 41, 47))
    text(d, (x0 + 60, py + 35), " / ".join(REPO.split("/")[1:3]), ui_bold(20), (240, 240, 240), "lm")
    cy = py + 110
    text(d, (x0 + 60, cy), "Colored Political Regions & Borders \u2013 Bannerlord v" + VERSION, ui_bold(30), (25, 25, 25), "lm")
    d.rounded_rectangle((x0 + 60, cy + 30, x0 + 140, cy + 58), 12, outline=(40, 160, 70), width=2)
    text(d, (x0 + 100, cy + 44), "Latest", ui(14), (40, 140, 60), "mm")
    for i, w in enumerate((900, 1160, 760)):
        d.rounded_rectangle((x0 + 60, cy + 82 + i * 30, x0 + 60 + w, cy + 96 + i * 30), 6, fill=(232, 234, 238))
    # file box
    fy = cy + 190
    text(d, (x0 + 60, fy), "Assets / Files", ui_bold(22), (25, 25, 25), "lm")
    box = (x0 + 60, fy + 30, x1 - 60, fy + 110)
    d.rounded_rectangle(box, 8, outline=(210, 214, 220), fill=(250, 251, 252))
    v.folder_icon(d, box[0] + 20, box[1] + 18, 44, zip_=True)
    text(d, (box[0] + 80, box[1] + 40), ZIP, ui_bold(19), (20, 90, 200), "lm")
    text(d, (box[0] + 920, box[1] + 40), "27 KB", ui(17), (90, 90, 90), "lm")
    return img


def s_download(t):
    path = Path([(0, 1500, 760), (1.6, 1480, 476), (3.8, 1480, 476), (5.0, 1693, 242), (7, 1693, 242)],
                clicks=[1.9, 5.5])
    downloading = t >= 2.1
    img = cache.get(("browser", downloading), lambda: browser_base(downloading))
    d = ImageDraw.Draw(img, "RGBA")
    hover_btn = 1.5 <= t < 2.2
    v.button(d, DL_BUTTON, "Download", primary=True, hover=hover_btn)
    glyph(d, (DL_BUTTON[0] + 22, (DL_BUTTON[1] + DL_BUTTON[3]) // 2), "download", 16, (255, 255, 255))
    if downloading:
        k = ramp(t, 2.1, 3.6)
        v.shadow(img, BUBBLE, 8, 12, (0, 5), 80)
        d = ImageDraw.Draw(img, "RGBA")
        d.rounded_rectangle(BUBBLE, 8, fill=(255, 255, 255), outline=(205, 205, 210))
        text(d, (BUBBLE[0] + 20, BUBBLE[1] + 26), "Recent downloads", ui_bold(16), (30, 30, 30), "lm")
        v.folder_icon(d, BUBBLE[0] + 18, BUBBLE[1] + 58, 40, zip_=True)
        text(d, (BUBBLE[0] + 70, BUBBLE[1] + 72), ZIP if len(ZIP) < 40 else ZIP[:37] + "...", ui(15), (25, 25, 25), "lm")
        if k < 1:
            d.rounded_rectangle((BUBBLE[0] + 70, BUBBLE[1] + 96, BUBBLE[2] - 30, BUBBLE[1] + 104), 4, fill=(225, 228, 235))
            d.rounded_rectangle((BUBBLE[0] + 70, BUBBLE[1] + 96, BUBBLE[0] + 70 + int((BUBBLE[2] - 100 - BUBBLE[0]) * k), BUBBLE[1] + 104), 4, fill=(20, 110, 220))
        else:
            text(d, (BUBBLE[0] + 70, BUBBLE[1] + 102), "27 KB \u2022 Done", ui(14), (90, 90, 90), "lm")
            hov = 4.9 <= t < 5.8
            d.rounded_rectangle(SHOW_FOLDER, 6, fill=(225, 235, 250) if hov else (245, 246, 248))
            glyph(d, ((SHOW_FOLDER[0] + SHOW_FOLDER[2]) // 2, (SHOW_FOLDER[1] + SHOW_FOLDER[3]) // 2), "openfolder", 18, (40, 60, 90))
            if hov:
                tip = (SHOW_FOLDER[0] - 120, SHOW_FOLDER[3] + 10, SHOW_FOLDER[0] + 30, SHOW_FOLDER[3] + 40)
                d.rounded_rectangle(tip, 5, fill=(50, 50, 55))
                text(d, ((tip[0] + tip[2]) // 2, (tip[1] + tip[3]) // 2), "Show in folder", ui(14), (240, 240, 240), "mm")
    path.draw(img, t)
    caption(img, "1", "Download the zip \u2013 Nexus Mods: Files tab, or GitHub: Releases",
            "Zip dosyasını indir \u2013 Nexus Mods: Files sekmesi ya da GitHub: Releases")
    return img


# ---- scene 2: extract ---------------------------------------------------------------------------

EXP = (240, 70, 1680, 870)
DOWNLOADS = [("zip", ZIP, DATE, "Compressed (zipped) Folder", "27 KB"),
             ("txt", "shopping list.txt", "10/02/2026 11:12", "Text Document", "1 KB")]
EXTRACTED_ITEMS = [("folder", FOLDER, DATE, "File folder", ""),
                   ("txt", "HOW_TO_INSTALL.txt", DATE, "Text Document", "8 KB")]
MENU = [("openfolder", "Open", False), (None, "Open in new tab", False), (None, "Open in new window", False),
        ("extract", "Extract All...", False), ("star", "Pin to Quick access", False), ("copy", "Copy as path", False),
        (None, "Properties", False), ("more", "Show more options", False)]
DIALOG = (600, 240, 1320, 700)


def extract_dialog(img, hover_extract):
    x0, y0, x1, y1 = DIALOG
    v.shadow(img, DIALOG, 8, 20, (0, 10), 120)
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle(DIALOG, 8, fill=(255, 255, 255), outline=(170, 170, 175))
    d.rounded_rectangle((x0, y0, x1, y0 + 40), 8, fill=(243, 243, 243))
    d.rectangle((x0, y0 + 30, x1, y0 + 40), fill=(243, 243, 243))
    v.folder_icon(d, x0 + 14, y0 + 9, 22, zip_=True)
    text(d, (x0 + 46, y0 + 20), "Extract Compressed (Zipped) Folders", ui(15), (30, 30, 30), "lm")
    glyph(d, (x1 - 24, y0 + 20), "close", 11, (60, 60, 60))
    text(d, (x0 + 40, y0 + 80), "Select a Destination and Extract Files", ui(22), (0, 51, 153), "lm")
    text(d, (x0 + 40, y0 + 136), "Files will be extracted to this folder:", ui(16), (30, 30, 30), "lm")
    d.rectangle((x0 + 40, y0 + 160, x1 - 160, y0 + 194), fill=(255, 255, 255), outline=(130, 130, 135))
    text(d, (x0 + 50, y0 + 177), "C:\\Users\\You\\Downloads\\" + EXTRACTED, ui(14), (20, 20, 20), "lm")
    v.button(d, (x1 - 140, y0 + 160, x1 - 40, y0 + 194), "Browse...")
    v.checkbox(d, (x0 + 40, y0 + 230), True, "Show extracted files when complete")
    d.rectangle((x0 + 1, y1 - 70, x1 - 1, y1 - 1), fill=(243, 243, 243))
    v.button(d, (x1 - 270, y1 - 52, x1 - 150, y1 - 18), "Extract", primary=True, hover=hover_extract)
    v.button(d, (x1 - 135, y1 - 52, x1 - 30, y1 - 18), "Cancel")


def s_extract(t):
    # times: right-click 1.1, menu 1.2-2.6, dialog 2.8-4.5, copying 4.5-5.3, extracted folder from 5.4
    path = Path([(0, 1100, 640), (0.9, 560, 270), (1.9, 640, 422), (2.4, 640, 422), (3.7, 1110, 645), (4.6, 1110, 645),
                 (5.8, 1110, 645), (6.6, 560, 270), (11, 560, 270)],
                clicks=[2.6, 4.4], right_clicks=[1.1])
    stage = "list" if t < 1.2 else "menu" if t < 2.7 else "dialog" if t < 4.5 else "copy" if t < 5.4 else "done"
    if stage in ("list", "menu", "dialog", "copy"):
        base_key = ("downloads",)
        img = cache.get(base_key, lambda: _explorer_frame("Downloads", ["This PC", "Downloads"], DOWNLOADS, (ZIP,), "Downloads"))
    else:
        hover = FOLDER if t > 6.5 else None
        img = cache.get(("extracted", hover), lambda: _explorer_frame(EXTRACTED, ["This PC", "Downloads", EXTRACTED],
                                                                   EXTRACTED_ITEMS, (), "Downloads", hover))
    if stage == "menu":
        x, y = path.at(t)
        hover = "Extract All..." if t > 2.05 else None
        img = cache.get(("menu", hover), lambda: _with_menu(img, hover))
    elif stage in ("dialog", "copy"):
        hover_x = 3.6 <= t < 4.5
        img = cache.get(("dialog", hover_x, stage), lambda: _with_dialog(img, hover_x, stage))
        if stage == "copy":
            k = ramp(t, 4.6, 5.3)
            d = ImageDraw.Draw(img)
            box = (700, 360, 1220, 560)
            d.rounded_rectangle((box[0] + 40, box[1] + 120, box[2] - 40, box[1] + 134), 6, fill=(220, 220, 225))
            d.rounded_rectangle((box[0] + 40, box[1] + 120, box[0] + 40 + int((box[2] - box[0] - 80) * k), box[1] + 134), 6, fill=(6, 176, 37))
    path.draw(img, t)
    caption(img, "2", "Right-click the zip  >  Extract All...  >  Extract", "Zip'e sağ tıkla  >  Tümünü ayıkla...  >  Ayıkla")
    return img


def _explorer_frame(title, crumbs, items, selected, nav, hover=None, box=EXP):
    img = desk()
    v.explorer(img, box, title, crumbs, items, selected=selected, hover=hover, nav=nav)
    return img


def _with_menu(base, hover):
    img = base.copy()
    v.context_menu(img, (560, 270), MENU, hover=hover)
    return img


def _with_dialog(base, hover_x, stage):
    img = base.copy()
    extract_dialog(img, hover_x)
    if stage == "copy":
        box = (700, 360, 1220, 560)
        v.shadow(img, box, 8, 16, (0, 8), 120)
        d = ImageDraw.Draw(img)
        d.rounded_rectangle(box, 8, fill=(255, 255, 255), outline=(170, 170, 175))
        text(d, (box[0] + 40, box[1] + 50), "Copying 3 items", ui_bold(20), (30, 30, 30), "lm")
        text(d, (box[0] + 40, box[1] + 86), "from " + ZIP[:28] + "...  to  Downloads", ui(14), (90, 90, 90), "lm")
    return img


# ---- scene 3: Steam library > Browse local files > Modules ---------------------------------------

STEAM = (120, 60, 1500, 860)
GAME_ROW = (STEAM[0] + 14, STEAM[1] + 172, STEAM[0] + 336, STEAM[1] + 206)
GAME_DIR = ["SteamLibrary", "steamapps", "common", "Mount & Blade II Bannerlord"]
GAME_ITEMS = [("folder", n, "10/29/2026 14:48", "File folder", "") for n in ("bin", "Data", "GUI", "Icons", "Modules", "Shaders", "XmlSchemas")] + \
             [("txt", "package_info.txt", "10/29/2026 14:48", "Text Document", "1 KB")]
MODULES = ["BirthAndDeath", "CustomBattle", "FastMode", "Multiplayer", "Native", "SandBox", "SandBoxCore", "StoryMode"]
EXP2 = (470, 130, 1840, 900)


def module_items(extra=None):
    names = sorted(MODULES + ([extra] if extra else []), key=str.lower)
    return [("folder", n, "10/29/2026 20:46" if n == extra else "10/29/2026 14:48", "File folder", "") for n in names]


def dark_menu(img, xy, entries, hover=None, width=240):
    x, y = xy
    box = (x, y, x + width, y + 14 + len(entries) * 36)
    v.shadow(img, box, 4, 10, (0, 4), 120)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle(box, 4, fill=(61, 68, 80))
    boxes = {}
    for i, (label, sub) in enumerate(entries):
        rb = (x + 4, y + 7 + i * 36, x + width - 4, y + 41 + i * 36)
        if label == hover:
            d.rectangle(rb, fill=(220, 222, 228))
        text(d, (x + 18, rb[1] + 17), label, ui(16), (30, 30, 30) if label == hover else (225, 228, 232), "lm")
        if sub:
            glyph(d, (x + width - 20, rb[1] + 17), "chevron", 10, (30, 30, 30) if label == hover else (200, 200, 205))
        boxes[label] = rb
    return boxes


def steam_base():
    img = desk()
    x0, y0, x1, y1 = STEAM
    v.shadow(img, STEAM, 4, 18)
    d = ImageDraw.Draw(img)
    d.rectangle(STEAM, fill=(27, 40, 56))
    d.rectangle((x0, y0, x1, y0 + 56), fill=(23, 29, 37))
    for i, (label, on) in enumerate((("STORE", False), ("LIBRARY", True), ("COMMUNITY", False))):
        text(d, (x0 + 40 + i * 170, y0 + 28), label, ui_bold(20), (26, 159, 255) if on else (200, 205, 210), "lm")
    for i, g in enumerate(("minimize", "maximize", "close")):
        glyph(d, (x1 - 110 + i * 38, y0 + 28), g, 11, (190, 195, 200))
    d.rectangle((x0, y0 + 56, x0 + 350, y1), fill=(33, 38, 47))
    d.rounded_rectangle((x0 + 14, y0 + 76, x0 + 336, y0 + 108), 4, fill=(50, 56, 66))
    glyph(d, (x0 + 34, y0 + 92), "search", 13, (150, 155, 160))
    text(d, (x0 + 52, y0 + 92), "Search", ui(15), (150, 155, 160), "lm")
    text(d, (x0 + 18, y0 + 142), "GAMES (4)", ui_bold(14), (150, 160, 175), "lm")
    games = ["Mount & Blade II: Bannerlord", "Mount & Blade: Warband", "Castle Builder", "Northern Saga"]
    for i, g in enumerate(games):
        rb = (x0 + 14, y0 + 172 + i * 40, x0 + 336, y0 + 206 + i * 40)
        if i == 0:
            d.rectangle(rb, fill=(62, 104, 150))
        d.rectangle((rb[0] + 8, rb[1] + 7, rb[0] + 28, rb[1] + 27), fill=(120, 95, 60) if i < 2 else (80, 90, 100))
        text(d, (rb[0] + 40, rb[1] + 17), g, ui(15), (240, 242, 245) if i == 0 else (195, 200, 205), "lm")
    # game page
    hero = ImageEnhance.Brightness(shot("overview.jpg").resize((x1 - x0 - 351, 340)).filter(ImageFilter.GaussianBlur(3))).enhance(0.6)
    img.paste(hero, (x0 + 351, y0 + 57))
    d = ImageDraw.Draw(img)
    text(d, (x0 + 400, y0 + 330), "MOUNT & BLADE II: BANNERLORD", title_font(38), (245, 240, 230), "lm")
    d.rectangle((x0 + 351, y0 + 397, x1, y0 + 480), fill=(23, 32, 44))
    d.rounded_rectangle((x0 + 400, y0 + 414, x0 + 560, y0 + 462), 3, fill=(80, 170, 50))
    text(d, (x0 + 480, y0 + 438), "PLAY", ui_bold(22), (255, 255, 255), "mm")
    for i, w in enumerate((520, 700, 430)):
        d.rectangle((x0 + 400, y0 + 520 + i * 34, x0 + 400 + w, y0 + 534 + i * 34), fill=(45, 58, 74))
    return img


STEAM_MENU = [("Play", False), ("Add to Favorites", False), ("Add to", True), ("Manage", True), ("Properties...", False)]
MANAGE_MENU = [("Add desktop shortcut", False), ("Set custom artwork", False), ("Browse local files", False),
               ("Hide this game", False), ("Uninstall", False)]


def s_modules(t):
    menu_xy = (GAME_ROW[0] + 200, GAME_ROW[1] + 22)
    sub_xy = (menu_xy[0] + 236, menu_xy[1] + 7 + 3 * 36)
    path = Path([(0, 900, 560), (1.0, menu_xy[0] - 2, menu_xy[1] - 4), (1.9, menu_xy[0] + 120, menu_xy[1] + 132),
                 (2.3, sub_xy[0] + 60, sub_xy[1] + 25), (2.8, sub_xy[0] + 90, sub_xy[1] + 96), (3.4, sub_xy[0] + 90, sub_xy[1] + 96),
                 (4.6, 800, 486), (5.6, 800, 486), (9.5, 800, 486)],
                clicks=[3.1], right_clicks=[1.15], double_clicks=[5.1])
    if t < 3.3:
        img = cache.get("steam", steam_base)
        if t >= 1.25:
            hover = "Manage" if t >= 1.75 else None
            sub_hover = "Browse local files" if t >= 2.65 else None
            img = cache.get(("steam_menu", hover, sub_hover), lambda: _steam_menus(menu_xy, sub_xy, hover, sub_hover))
    else:
        inside = t >= 5.45
        rows_hover = None if inside else ("Modules" if t >= 4.4 else None)
        img = cache.get(("game_dir", inside, rows_hover), lambda: _game_dir(inside, rows_hover))
    path.draw(img, t)
    caption(img, "3", "Steam > Library > right-click Bannerlord > Manage > Browse local files > open Modules",
            "Steam > Kütüphane > Bannerlord'a sağ tıkla > Yönet > Yerel dosyalara göz at > Modules")
    return img


def _steam_menus(menu_xy, sub_xy, hover, sub_hover):
    img = steam_base()
    dark_menu(img, menu_xy, STEAM_MENU, hover)
    if hover == "Manage":
        dark_menu(img, sub_xy, MANAGE_MENU, sub_hover, width=250)
    return img


def _game_dir(inside, hover):
    img = steam_base()
    if inside:
        v.explorer(img, EXP2, "Modules", GAME_DIR + ["Modules"], module_items(), nav="This PC")
    else:
        v.explorer(img, EXP2, "Mount & Blade II Bannerlord", GAME_DIR, GAME_ITEMS, hover=hover, nav="This PC")
    return img


# ---- scene 4: copy the folder into Modules ---------------------------------------------------------

LEFT = (30, 70, 950, 860)
RIGHT = (970, 70, 1890, 860)


def s_copy(t):
    # left folder row centre (from the explorer layout): y = top + 46 + 52 + 53 + 10 + 34 + 17
    row_y = LEFT[1] + 212
    path = Path([(0, 600, 600), (0.7, 420, row_y), (1.0, 420, row_y), (2.7, 1450, 640), (3.0, 1450, 640),
                 (3.9, 1380, RIGHT[1] + 212 + 36), (5.4, 1380, RIGHT[1] + 212 + 36), (6.4, 1330, RIGHT[1] + 212 + 36), (11, 1330, RIGHT[1] + 212 + 36)],
                clicks=[1.0], double_clicks=[4.3])
    copied = t >= 2.85
    inside = t >= 4.75
    img = cache.get(("copy", copied, inside), lambda: _copy_frame(copied, inside))
    if 1.0 <= t < 2.85:
        x, y = path.at(t)
        d = ImageDraw.Draw(img, "RGBA")
        v.folder_icon(d, x + 6, y + 18, 54)
        d.rounded_rectangle((x + 28, y + 70, x + 228, y + 100), 4, fill=(255, 255, 255, 235), outline=(160, 160, 165))
        over_right = x > RIGHT[0]
        tip = ("+ Copy to Modules" if over_right else "Move")
        text(d, (x + 40, y + 85), tip, ui(15), (20, 20, 20), "lm")
    if inside and t >= 5.6:
        d = ImageDraw.Draw(img, "RGBA")
        k = ramp(t, 5.6, 6.0)
        box = (RIGHT[0] + 360, RIGHT[1] + 330, RIGHT[2] - 30, RIGHT[1] + 420)
        d.rounded_rectangle(box, 10, fill=(232, 248, 236, int(250 * k)), outline=(40, 160, 80, int(255 * k)), width=2)
        glyph(d, (box[0] + 32, (box[1] + box[3]) // 2), "check", 22, (30, 140, 60, int(255 * k)))
        text(d, (box[0] + 60, box[1] + 30), "SubModule.xml must be here", ui_bold(17), (20, 90, 40, int(255 * k)), "lm")
        text(d, (box[0] + 60, box[1] + 60), "Modules\\" + FOLDER + "\\SubModule.xml", ui(14), (40, 90, 50, int(255 * k)), "lm")
    path.draw(img, t)
    caption(img, "4", "Copy the " + FOLDER + " folder into Modules", FOLDER + " klasörünü Modules'ün içine kopyala")
    return img


def _copy_frame(copied, inside):
    img = desk()
    v.explorer(img, LEFT, EXTRACTED[:24] + "...", ["Downloads", EXTRACTED[:30] + "..."], EXTRACTED_ITEMS,
               selected=(FOLDER,), nav="Downloads")
    if inside:
        v.explorer(img, RIGHT, FOLDER, ["...", "Modules", FOLDER],
                   [("folder", "bin", "10/29/2026 20:46", "File folder", ""), ("xml", "SubModule.xml", "10/29/2026 20:46", "XML Source File", "1 KB")],
                   selected=("SubModule.xml",), nav="This PC")
    else:
        v.explorer(img, RIGHT, "Modules", ["...", "Mount & Blade II Bannerlord", "Modules"],
                   module_items(FOLDER if copied else None), selected=((FOLDER,) if copied else ()), nav="This PC")
    return img


# ---- scene 5: launcher ----------------------------------------------------------------------------

LAUNCHER = (260, 80, 1660, 880)
MODS = [("Native", "v1.4.8", True), ("SandBox Core", "v1.4.8", True), ("Birth and Aging Options", "v1.4.8", False),
        ("Custom Battle", "v1.4.8", True), ("Sandbox", "v1.4.8", True), ("Story Mode", "v1.4.8", True),
        ("Colored Political Regions & Borders", "v" + VERSION, False)]
MODS_TAB = (LAUNCHER[0] + 160, LAUNCHER[1] + 150, LAUNCHER[0] + 260, LAUNCHER[1] + 186)
PLAY = (LAUNCHER[2] - 330, LAUNCHER[3] - 110, LAUNCHER[2] - 50, LAUNCHER[3] - 40)
ROW0 = LAUNCHER[1] + 250


def launcher_frame(tab_mods, ticked, hover_play):
    img = desk()
    x0, y0, x1, y1 = LAUNCHER
    v.shadow(img, LAUNCHER, 2, 22)
    art = ImageEnhance.Brightness(shot("overview.jpg").resize((x1 - x0, y1 - y0)).filter(ImageFilter.GaussianBlur(5))).enhance(0.3)
    img.paste(art, (x0, y0))
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle(LAUNCHER, outline=(120, 95, 55), width=2)
    text(d, (x0 + 50, y0 + 46), "MOUNT & BLADE II", title_font(22), (200, 180, 140), "lm")
    text(d, (x0 + 50, y0 + 84), "BANNERLORD", title_font(44), (236, 210, 150), "lm")
    for i, (label, on) in enumerate((("SINGLEPLAYER", True), ("MULTIPLAYER", False))):
        cx = x0 + 760 + i * 260
        text(d, (cx, y0 + 70), label, ui_bold(22), (240, 220, 170) if on else (160, 150, 130), "mm")
        if on:
            d.line((cx - 80, y0 + 92, cx + 80, y0 + 92), fill=(220, 180, 100), width=3)
    for i, label in enumerate(("NEWS", "MODS")):
        on = (label == "MODS") == tab_mods
        rb = (x0 + 50 + i * 110, y0 + 150, x0 + 150 + i * 110, y0 + 186)
        text(d, ((rb[0] + rb[2]) // 2, (rb[1] + rb[3]) // 2), label, ui_bold(19), (240, 220, 170) if on else (170, 160, 140), "mm")
        if on:
            d.line((rb[0] + 10, rb[3] + 4, rb[2] - 10, rb[3] + 4), fill=(220, 180, 100), width=2)
    panel = (x0 + 50, y0 + 210, x1 - 50, y1 - 140)
    d.rectangle(panel, fill=(10, 9, 8, 190), outline=(110, 90, 55))
    if tab_mods:
        text(d, (panel[0] + 24, panel[1] + 22), "Name", ui_bold(16), (190, 175, 145), "lm")
        text(d, (panel[2] - 160, panel[1] + 22), "Version", ui_bold(16), (190, 175, 145), "lm")
        for i, (name, ver, on) in enumerate(MODS):
            ry = ROW0 + i * 48
            ours = name.startswith("Colored")
            if ours:
                d.rectangle((panel[0] + 4, ry - 6, panel[2] - 4, ry + 34), fill=(80, 64, 36, 160))
            v.checkbox(d, (panel[0] + 24, ry), on or (ours and ticked), name, dark=True, size=26, fnt=ui(19))
            text(d, (panel[2] - 160, ry + 13), ver, ui(17), (200, 190, 170), "lm")
    else:
        text(d, (panel[0] + 24, panel[1] + 40), "News", ui_bold(22), (220, 205, 175), "lm")
        for i, w in enumerate((800, 900, 650, 720)):
            d.rectangle((panel[0] + 24, panel[1] + 90 + i * 40, panel[0] + 24 + w, panel[1] + 104 + i * 40), fill=(60, 55, 48, 200))
    d.rectangle(PLAY, fill=(150, 112, 50) if hover_play else (122, 90, 40), outline=(230, 195, 120), width=2)
    text(d, ((PLAY[0] + PLAY[2]) // 2, (PLAY[1] + PLAY[3]) // 2), "PLAY", title_font(34), (250, 235, 200), "mm")
    return img


CAUTION_BOX = (520, 270, 1400, 690)
CONFIRM = (1000, 700, 1110, 740)


def caution(img, hover_confirm):
    x0, y0, x1, y1 = CAUTION_BOX
    glow = Image.new("RGBA", (x1 - x0 + 120, y1 - y0 + 120), (0, 0, 0, 0))
    ImageDraw.Draw(glow).rectangle((40, 40, x1 - x0 + 80, y1 - y0 + 80), outline=(196, 150, 70, 255), width=26)
    glow = glow.filter(ImageFilter.GaussianBlur(10))
    img.paste(glow, (x0 - 60, y0 - 60), glow)
    d = ImageDraw.Draw(img)
    d.rectangle(CAUTION_BOX, fill=(4, 4, 4))
    text(d, ((x0 + x1) // 2, y0 + 80), "CAUTION", ui(26), (240, 220, 170), "mm")
    lines = ["You're loading unverified code from:", "Colored Political Regions & Borders", "",
             "TaleWorlds is not responsible for an unstable experience if it occurs.", "Are you sure?"]
    for i, s in enumerate(lines):
        text(d, ((x0 + x1) // 2, y0 + 160 + i * 34), s, ui(19), (205, 195, 175), "mm")
    text(d, (930, 720), "Cancel", ui(24), (190, 140, 70), "mm")
    text(d, ((CONFIRM[0] + CONFIRM[2]) // 2, 720), "Confirm", ui(24), (240, 190, 100) if hover_confirm else (200, 150, 80), "mm")


def s_launcher(t):
    our_row = ROW0 + 6 * 48
    cb = (LAUNCHER[0] + 50 + 24 + 13, our_row + 13)
    path = Path([(0, 1100, 600), (0.8, (MODS_TAB[0] + MODS_TAB[2]) // 2 - 4, 250), (1.5, (MODS_TAB[0] + MODS_TAB[2]) // 2 - 4, 250),
                 (2.6, cb[0], cb[1]), (3.2, cb[0], cb[1]), (4.3, (PLAY[0] + PLAY[2]) // 2, (PLAY[1] + PLAY[3]) // 2),
                 (4.9, (PLAY[0] + PLAY[2]) // 2, (PLAY[1] + PLAY[3]) // 2), (6.3, 1052, 720), (8.5, 1052, 720)],
                clicks=[1.1, 2.9, 4.6, 6.6])
    tab_mods = t >= 1.2
    ticked = t >= 3.0
    hover_play = 4.2 <= t < 4.8
    img = cache.get(("launcher", tab_mods, ticked, hover_play), lambda: launcher_frame(tab_mods, ticked, hover_play))
    if t >= 4.75:
        hover_c = 6.1 <= t < 6.8
        img = cache.get(("caution", hover_c), lambda: _with_caution(img, hover_c))
    path.draw(img, t)
    if t >= 6.9:
        img = Image.blend(img, Image.new("RGB", (W, H)), ramp(t, 6.9, 7.6))
    if t < 4.75:
        caption(img, "5", "Launcher: Singleplayer > Mods > tick Colored Political Regions & Borders > Play",
                "Launcher: Singleplayer > Mods > Colored Political Regions & Borders'ı işaretle > Play")
    else:
        caption(img, "5", "This CAUTION window appears for every mod with its own code: click Confirm",
                "Bu CAUTION penceresi kendi kodu olan her modda çıkar: Confirm'e tıkla")
    return img


def _with_caution(base, hover_c):
    img = ImageEnhance.Brightness(base).enhance(0.45)
    caution(img, hover_c)
    return img


# ---- scene 6: in game -----------------------------------------------------------------------------

def s_game(t):
    if t < 5.0:
        img = zoomed(shot("medium_border.jpg"), 1.0 + 0.05 * smooth(t / 5.0))
        en, tr = ("Start or load a campaign: after a few seconds the map is ready",
                  "Sefer başlat ya da kaydını yükle: birkaç saniyede harita hazır")
        if 0.4 <= t < 4.8:
            game_message(img, "CPR&B: political map ready. Ctrl + 1: colour strength.", ramp(t, 0.4, 0.8) * (1 - ramp(t, 4.4, 4.8)))
    elif t < 10.3:
        k = min(1.0, (t - 5.0) / 5.0)
        img = zoomed(shot("overview.jpg"), 1.14 - 0.14 * smooth(k))
        if t >= 10.0:
            close = zoomed(shot("border_close.jpg"), 1.0, 0.45, 0.4)
            img = Image.blend(img, close, smooth((t - 10.0) / 0.6))
        if t < 5.6:
            img = Image.blend(zoomed(shot("medium_border.jpg"), 1.05), img, smooth((t - 5.0) / 0.6))
        if t >= 5.3:
            en, tr = "Zoom out: every kingdom in its own colour", "Uzaklaş: her krallık kendi renginde"
        else:
            en, tr = ("Start or load a campaign: after a few seconds the map is ready",
                      "Sefer başlat ya da kaydını yükle: birkaç saniyede harita hazır")
    else:
        k = (t - 10.0) / 5.0
        img = zoomed(shot("border_close.jpg"), 1.0 + 0.06 * smooth(k), 0.45, 0.4)
        if t < 10.6:
            img = Image.blend(shot("overview.jpg"), img, smooth((t - 10.0) / 0.6))
        en, tr = ("Zoom in: normal terrain, dark borders and each side's glow",
                  "Yakınlaş: normal arazi, koyu sınırlar ve iki yanda kendi renginde hüzme")
    if t < 0.5:
        img = Image.blend(Image.new("RGB", (W, H)), img, smooth(t / 0.5))
    caption(img, "6", en, tr)
    return img


# ---- scene 7: Ctrl + 1 ----------------------------------------------------------------------------

POPUP = (640, 300, 1280, 700)
OK = (POPUP[0] + 150, POPUP[3] - 86, POPUP[0] + 300, POPUP[3] - 40)


def keycap(d, box, label, pressed):
    x0, y0, x1, y1 = box
    off = 5 if pressed else 0
    d.rounded_rectangle((x0, y0 + 8, x1, y1 + 8), 12, fill=(120, 120, 128))
    d.rounded_rectangle((x0, y0 + off, x1, y1 + off), 12, fill=(235, 235, 240) if not pressed else (210, 212, 220), outline=(150, 150, 158), width=2)
    text(d, ((x0 + x1) // 2, (y0 + y1) // 2 + off), label, ui_bold(34), (40, 40, 48), "mm")


def popup(img, typed, caret, hover_ok):
    x0, y0, x1, y1 = POPUP
    v.shadow(img, POPUP, 4, 20, (0, 10), 160)
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle(POPUP, fill=(22, 18, 14, 245), outline=(176, 140, 80), width=3)
    d.rectangle((x0 + 8, y0 + 8, x1 - 8, y1 - 8), outline=(110, 88, 52), width=1)
    text(d, ((x0 + x1) // 2, y0 + 56), "Colored Political Regions & Borders", serif(28), (232, 200, 130), "mm")
    text(d, ((x0 + x1) // 2, y0 + 112), "Map colour strength in percent (0 - 100).", ui(21), (225, 215, 195), "mm")
    text(d, ((x0 + x1) // 2, y0 + 144), "Now: 60 %", ui(21), (225, 215, 195), "mm")
    field = (x0 + 140, y0 + 190, x1 - 140, y0 + 240)
    d.rectangle(field, fill=(10, 8, 6), outline=(150, 120, 70), width=2)
    tw = d.textlength(typed, font=ui(24))
    text(d, ((field[0] + field[2]) // 2 - tw / 2, (field[1] + field[3]) // 2), typed, ui(24), (245, 235, 215), "lm")
    if caret:
        cx = (field[0] + field[2]) // 2 + tw / 2 + 3
        d.line((cx, field[1] + 12, cx, field[3] - 12), fill=(245, 235, 215), width=2)
    for box, label, hov in ((OK, "OK", hover_ok), ((x1 - 300, OK[1], x1 - 150, OK[3]), "Cancel", False)):
        d.rectangle(box, fill=(70, 54, 30) if hov else (40, 32, 22), outline=(190, 150, 85), width=2)
        text(d, ((box[0] + box[2]) // 2, (box[1] + box[3]) // 2), label, ui_bold(20), (240, 220, 175), "mm")


def s_ctrl1(t):
    base = shot("overview.jpg")
    strong = cache.get("strong", lambda: ImageEnhance.Color(shot("overview.jpg")).enhance(1.35))
    img = Image.blend(base, strong, ramp(t, 4.7, 5.3)) if t >= 4.7 else base.copy()
    d = ImageDraw.Draw(img, "RGBA")
    if t < 1.6:
        a = ramp(t, 0.2, 0.5) * (1 - ramp(t, 1.4, 1.6))
        if a > 0:
            layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            ld = ImageDraw.Draw(layer)
            pressed = 0.65 <= t < 1.2
            keycap(ld, (W // 2 - 190, 150, W // 2 - 20, 250), "Ctrl", pressed)
            text(ld, (W // 2 + 10, 200), "+", ui_bold(48), (255, 255, 255), "mm")
            keycap(ld, (W // 2 + 40, 150, W // 2 + 140, 250), "1", pressed)
            layer.putalpha(layer.getchannel("A").point(lambda p: int(p * a)))
            img.paste(layer, (0, 0), layer)
    if 1.3 <= t < 4.75:
        img = ImageEnhance.Brightness(img).enhance(0.75)
        typed = "60" if t < 2.3 else "" if t < 2.6 else "8" if t < 2.9 else "85"
        popup(img, typed, int(t * 2.2) % 2 == 0, 4.0 <= t < 4.75)
    if t >= 4.85:
        game_message(img, "CPR&B: Map colour strength: 85 %", ramp(t, 4.85, 5.2))
    path = Path([(0, 1500, 900), (3.0, 1500, 900), (4.1, (OK[0] + OK[2]) // 2, (OK[1] + OK[3]) // 2), (9, (OK[0] + OK[2]) // 2, (OK[1] + OK[3]) // 2)],
                clicks=[4.5])
    if t >= 2.9:
        path.draw(img, t)
    caption(img, "7", "Ctrl + 1 on the campaign map: colour strength 0\u2013100 %, saved for every game",
            "Sefer haritasında Ctrl + 1: renk gücü %0\u2013100, her oyun için kaydedilir")
    return img


# ---- scene 8: map follows conquests ------------------------------------------------------------------

def s_conquest(t):
    before = shot("overview.jpg")
    after = shot("conquest_vlandia.jpg")
    img = Image.blend(before, after, ramp(t, 2.6, 3.6)) if t >= 2.6 else before.copy()
    d = ImageDraw.Draw(img, "RGBA")
    label = ("Before / Önce" if t < 3.1 else "After: Poros taken by Vlandia / Sonra: Poros Vlandia'nın")
    tw = d.textlength(label, font=ui_bold(26))
    d.rounded_rectangle((40, 40, 40 + tw + 40, 96), 10, fill=(0, 0, 0, 170))
    text(d, (60, 68), label, ui_bold(26), (250, 240, 220), "lm")
    caption(img, "", "When towns and castles change hands, the map recolours itself",
            "Şehir ve kaleler el değiştirince harita kendiliğinden yeniden boyanır")
    return img


# ---- scene 9: outro ----------------------------------------------------------------------------------

def s_outro(t):
    img = cache.get("title", card_background)
    d = ImageDraw.Draw(img)
    text(d, (W // 2, 230), "Tested on Bannerlord v1.4.8 (Steam, Sandbox campaign)", ui_bold(40), GOLD, "mm")
    text(d, (W // 2, 290), "Bannerlord v1.4.8'de test edildi (Steam, Sandbox seferi)", ui(32), (215, 205, 185), "mm")
    text(d, (W // 2, 400), "Other versions, War Sails and other mods are not tested yet \u2013 feedback welcome!", ui(32), PARCHMENT, "mm")
    text(d, (W // 2, 450), "Diğer sürümler, War Sails ve diğer modlar henüz test edilmedi \u2013 yorumlarını bekliyorum!", ui(28), (210, 200, 180), "mm")
    text(d, (W // 2, 560), "Uninstall: untick the mod in the launcher  /  Kaldırma: launcher'da işaretini kaldır", ui(28), PARCHMENT, "mm")
    text(d, (W // 2, 690), REPO, ui(30), (200, 215, 235), "mm")
    text(d, (W // 2, 740), "Nexus Mods: Colored Political Regions & Borders", ui(30), (200, 215, 235), "mm")
    if t > 5.0:
        img = Image.blend(img, Image.new("RGB", (W, H)), ramp(t, 5.0, 6.0))
    return img


SCENES = [(s_title, 3.6), (s_download, 7.0), (s_extract, 9.0), (s_modules, 9.5), (s_copy, 9.0),
          (s_launcher, 7.8), (s_game, 15.0), (s_ctrl1, 8.0), (s_conquest, 7.0), (s_outro, 6.0)]


def frames():
    """(scene index, local time) for every frame, with a short cross-fade between scenes."""
    out = []
    for i, (fn, dur) in enumerate(SCENES):
        n = int(round(dur * FPS))
        for f in range(n):
            out.append((i, f / FPS))
    return out


def render(i, t):
    fn, dur = SCENES[i]
    img = fn(t)
    if i + 1 < len(SCENES) and t > dur - FADE and i not in (5,):
        nxt = SCENES[i + 1][0](0.0)
        img = Image.blend(img, nxt, smooth((t - (dur - FADE)) / FADE))
    return img.convert("RGB")


def main():
    global REPO
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        REPO = args[0]
    os.makedirs(OUT, exist_ok=True)
    if "--stills" in sys.argv:
        stills = os.path.join(OUT, "stills")
        os.makedirs(stills, exist_ok=True)
        picks = {0: [1.5], 1: [1.8, 3.0, 5.2], 2: [1.0, 2.2, 3.8, 4.9, 7.0], 3: [2.4, 3.0, 4.5, 6.5], 4: [1.8, 3.5, 6.5],
                 5: [0.5, 3.5, 5.5], 6: [2.0, 7.0, 12.0], 7: [1.0, 3.2, 6.0], 8: [1.0, 5.0], 9: [2.0]}
        for i, times in picks.items():
            for t in times:
                render(i, t).save(os.path.join(stills, "s%d_%04.1f.jpg" % (i, t)), quality=88)
        print("stills in", stills)
        return
    import imageio.v2 as imageio
    path = os.path.join(OUT, "cprb_bannerlord_install.mp4")
    writer = imageio.get_writer(path, fps=FPS, codec="libx264", quality=8, pixelformat="yuv420p",
                                macro_block_size=8, ffmpeg_log_level="error")
    all_frames = frames()
    for n, (i, t) in enumerate(all_frames):
        writer.append_data(__import__("numpy").asarray(render(i, t)))
        if n % 300 == 0:
            print("frame", n, "/", len(all_frames))
    writer.close()
    print(path, os.path.getsize(path), "bytes,", len(all_frames) / FPS, "s")


if __name__ == "__main__":
    main()
