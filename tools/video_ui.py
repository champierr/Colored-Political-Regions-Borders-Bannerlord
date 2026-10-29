"""Drawing kit for the install videos: Windows 11 style windows, File Explorer, menus, icons, the mouse
cursor and small animation helpers. Everything is drawn with Pillow on 1920 x 1080 frames.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1920, 1080
FONTS = r"C:\Windows\Fonts"
_cache = {}


def font(name, size):
    key = (name, size)
    if key not in _cache:
        _cache[key] = ImageFont.truetype(os.path.join(FONTS, name), size)
    return _cache[key]


def ui(size):
    return font("segoeui.ttf", size)


def ui_bold(size):
    return font("segoeuib.ttf", size)


def ui_semi(size):
    return font("seguisb.ttf", size) if os.path.exists(os.path.join(FONTS, "seguisb.ttf")) else ui_bold(size)


def icons(size):
    return font("SegoeIcons.ttf", size)


GLYPH = {
    "back": "\ue72b", "forward": "\ue72a", "up": "\ue74a", "refresh": "\ue72c", "search": "\ue721",
    "chevron": "\ue76c", "add": "\ue710", "cut": "\ue8c6", "copy": "\ue8c8", "paste": "\ue77f",
    "rename": "\ue8ac", "share": "\ue72d", "delete": "\ue74d", "sort": "\ue8cb", "view": "\ue8a9",
    "more": "\ue712", "download": "\ue896", "folder": "\ue8b7", "home": "\ue80f", "pc": "\ue7f8",
    "minimize": "\ue921", "maximize": "\ue922", "close": "\ue8bb", "lock": "\ue72e", "star": "\ue734",
    "extract": "\ue8b5", "check": "\ue73e", "openfolder": "\ue838",
}


# ---- animation helpers --------------------------------------------------------------------------

def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else hi if v > hi else v


def smooth(t):
    t = clamp(t)
    return t * t * (3 - 2 * t)


def ramp(t, start, end):
    """0 before start, 1 after end, smooth in between."""
    if end <= start:
        return 1.0 if t >= end else 0.0
    return smooth((t - start) / (end - start))


def lerp(a, b, t):
    return a + (b - a) * t


class Path:
    """Mouse path: keyframes (time, x, y); clicks (time); presses (start, end) for dragging."""

    def __init__(self, points, clicks=(), right_clicks=(), double_clicks=()):
        self.points = points
        self.clicks = list(clicks)
        self.right = list(right_clicks)
        self.double = list(double_clicks)

    def at(self, t):
        pts = self.points
        if t <= pts[0][0]:
            return pts[0][1], pts[0][2]
        for (t0, x0, y0), (t1, x1, y1) in zip(pts, pts[1:]):
            if t0 <= t <= t1:
                k = smooth((t - t0) / max(1e-6, t1 - t0))
                return lerp(x0, x1, k), lerp(y0, y1, k)
        return pts[-1][1], pts[-1][2]

    def draw(self, img, t):
        x, y = self.at(t)
        d = ImageDraw.Draw(img, "RGBA")
        for times, colour in ((self.clicks, (40, 120, 255)), (self.right, (255, 140, 40)), (self.double, (40, 120, 255))):
            for c in times:
                for extra in ((0.0, 0.12) if times is self.double else (0.0,)):
                    k = (t - c - extra) / 0.35
                    if 0 <= k <= 1:
                        r = 8 + 26 * k
                        a = int(200 * (1 - k))
                        d.ellipse((x - r, y - r, x + r, y + r), outline=colour + (a,), width=4)
        cursor(img, x, y)


_ARROW = [(0, 0), (0, 21), (5, 16.5), (8.5, 24.5), (11.5, 23.3), (8.2, 15.5), (15, 15.5)]
_cursor_shadow = {}


def cursor(img, x, y, scale=1.6):
    if scale not in _cursor_shadow:
        layer = Image.new("RGBA", (60, 70), (0, 0, 0, 0))
        ImageDraw.Draw(layer).polygon([(10 + px * scale + 2, 10 + py * scale + 3) for px, py in _ARROW], fill=(0, 0, 0, 90))
        _cursor_shadow[scale] = layer.filter(ImageFilter.GaussianBlur(2))
    sh = _cursor_shadow[scale]
    img.paste(sh, (int(x) - 10, int(y) - 10), sh)
    poly = [(x + px * scale, y + py * scale) for px, py in _ARROW]
    d = ImageDraw.Draw(img)
    d.polygon(poly, fill=(255, 255, 255), outline=(0, 0, 0))
    d.line(poly + [poly[0]], fill=(0, 0, 0), width=2)


# ---- primitives ---------------------------------------------------------------------------------

def shadow(img, box, radius=10, blur=18, offset=(0, 8), alpha=110):
    x0, y0, x1, y1 = box
    pad = blur * 3
    layer = Image.new("RGBA", (x1 - x0 + 2 * pad, y1 - y0 + 2 * pad), (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle((pad, pad, pad + x1 - x0, pad + y1 - y0), radius, fill=(0, 0, 0, alpha))
    layer = layer.filter(ImageFilter.GaussianBlur(blur))
    img.paste(layer, (x0 - pad + offset[0], y0 - pad + offset[1]), layer)


def fit(d, s, fnt, width):
    """s shortened with ... so that it fits in width pixels."""
    if d.textlength(s, font=fnt) <= width:
        return s
    while s and d.textlength(s + "...", font=fnt) > width:
        s = s[:-1]
    return s + "..."


def text(d, xy, s, fnt, fill, anchor="la"):
    d.text(xy, s, font=fnt, fill=fill, anchor=anchor)


def glyph(d, xy, name, size, fill, anchor="mm"):
    d.text(xy, GLYPH[name], font=icons(size), fill=fill, anchor=anchor)


def desktop():
    """Windows 11 like wallpaper: deep blue with soft light blooms."""
    img = Image.new("RGB", (W, H), (12, 32, 78))
    blooms = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(blooms)
    d.ellipse((560, 260, 1500, 1180), fill=(70, 140, 255))
    d.ellipse((900, 420, 1700, 1300), fill=(40, 90, 220))
    d.ellipse((-200, 600, 700, 1400), fill=(30, 60, 160))
    blooms = blooms.filter(ImageFilter.GaussianBlur(180))
    img = Image.blend(img, blooms, 0.55)
    # taskbar
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle((0, H - 48, W, H), fill=(238, 241, 247, 235))
    for i, name in enumerate(("search", "folder", "star", "download")):
        cx = W // 2 - 90 + i * 60
        glyph(d, (cx, H - 24), name, 22, (40, 50, 70))
    text(d, (W - 24, H - 24), "20:45", ui(17), (30, 30, 30), "rm")
    return img


# ---- file icons ---------------------------------------------------------------------------------

def folder_icon(d, x, y, s, zip_=False):
    """Yellow folder (x, y = top-left, s = size)."""
    d.rounded_rectangle((x + s * 0.04, y + s * 0.18, x + s * 0.46, y + s * 0.34), s * 0.05, fill=(222, 160, 40))
    d.rounded_rectangle((x + s * 0.04, y + s * 0.26, x + s * 0.96, y + s * 0.86), s * 0.07, fill=(250, 196, 66))
    d.rectangle((x + s * 0.04, y + s * 0.32, x + s * 0.96, y + s * 0.36), fill=(255, 214, 110))
    if zip_:
        cx = x + s * 0.5
        for i in range(6):
            yy = y + s * (0.30 + i * 0.085)
            d.rectangle((cx - s * 0.05, yy, cx + s * 0.05, yy + s * 0.04), fill=(90, 90, 90) if i % 2 == 0 else (200, 200, 200))
        d.rounded_rectangle((cx - s * 0.08, y + s * 0.70, cx + s * 0.08, y + s * 0.82), s * 0.02, fill=(70, 70, 70))


def page_icon(d, x, y, s, label=None, colour=(90, 110, 140)):
    d.polygon([(x + s * 0.2, y + s * 0.08), (x + s * 0.62, y + s * 0.08), (x + s * 0.82, y + s * 0.28),
               (x + s * 0.82, y + s * 0.92), (x + s * 0.2, y + s * 0.92)], fill=(255, 255, 255), outline=(150, 150, 150))
    d.polygon([(x + s * 0.62, y + s * 0.08), (x + s * 0.62, y + s * 0.28), (x + s * 0.82, y + s * 0.28)], fill=(225, 225, 225), outline=(150, 150, 150))
    if label:
        text(d, (x + s * 0.51, y + s * 0.62), label, ui_bold(max(8, int(s * 0.22))), colour, "mm")
    else:
        for i in range(4):
            yy = y + s * (0.42 + i * 0.12)
            d.line((x + s * 0.3, yy, x + s * 0.72, yy), fill=(170, 170, 170), width=max(1, int(s * 0.04)))


def item_icon(d, kind, x, y, s):
    if kind == "folder":
        folder_icon(d, x, y, s)
    elif kind == "zip":
        folder_icon(d, x, y, s, zip_=True)
    elif kind == "xml":
        page_icon(d, x, y, s, "</>", (200, 100, 30))
    else:
        page_icon(d, x, y, s)


# ---- File Explorer ------------------------------------------------------------------------------

NAV = ["Home", "Gallery", "Desktop", "Downloads", "Documents", "Pictures", "Music", "Videos", "This PC"]


def explorer(img, box, title, crumbs, items, selected=(), hover=None, nav="Downloads", status=None, columns=True):
    """File Explorer window. items: list of (kind, name, date, type, size). Returns the item rows' boxes."""
    x0, y0, x1, y1 = box
    shadow(img, box, 10)
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle(box, 10, fill=(255, 255, 255), outline=(200, 200, 205))
    # title bar with tab
    d.rounded_rectangle((x0, y0, x1, y0 + 46), 10, fill=(232, 236, 242))
    d.rectangle((x0, y0 + 30, x1, y0 + 46), fill=(232, 236, 242))
    d.rounded_rectangle((x0 + 10, y0 + 8, x0 + 300, y0 + 46), 8, fill=(250, 250, 252))
    folder_icon(d, x0 + 20, y0 + 14, 22)
    text(d, (x0 + 50, y0 + 27), fit(d, title, ui(16), 220), ui(16), (30, 30, 30), "lm")
    glyph(d, (x0 + 285, y0 + 27), "close", 10, (90, 90, 90))
    glyph(d, (x0 + 320, y0 + 27), "add", 12, (90, 90, 90))
    for i, g in enumerate(("minimize", "maximize", "close")):
        glyph(d, (x1 - 130 + i * 46, y0 + 22), g, 12, (40, 40, 40))
    # toolbar
    ty = y0 + 46
    d.rectangle((x0 + 1, ty, x1 - 1, ty + 52), fill=(250, 250, 252))
    glyph(d, (x0 + 28, ty + 26), "add", 16, (0, 95, 184))
    text(d, (x0 + 46, ty + 26), "New", ui(16), (30, 30, 30), "lm")
    for i, g in enumerate(("cut", "copy", "paste", "rename", "share", "delete")):
        glyph(d, (x0 + 120 + i * 44, ty + 26), g, 18, (60, 60, 60))
    glyph(d, (x0 + 410, ty + 26), "sort", 18, (60, 60, 60))
    text(d, (x0 + 428, ty + 26), "Sort", ui(16), (30, 30, 30), "lm")
    glyph(d, (x0 + 498, ty + 26), "view", 18, (60, 60, 60))
    text(d, (x0 + 516, ty + 26), "View", ui(16), (30, 30, 30), "lm")
    d.line((x0 + 1, ty + 52, x1 - 1, ty + 52), fill=(225, 225, 230))
    # address bar
    ay = ty + 52
    for i, g in enumerate(("back", "forward", "up", "refresh")):
        glyph(d, (x0 + 26 + i * 38, ay + 26), g, 15, (70, 70, 70) if i != 1 else (180, 180, 180))
    sx = x1 - 290
    d.rounded_rectangle((x0 + 170, ay + 8, sx - 12, ay + 44), 6, fill=(255, 255, 255), outline=(205, 205, 210))
    cx = x0 + 184
    for i, part in enumerate(crumbs):
        if i:
            glyph(d, (cx + 8, ay + 26), "chevron", 10, (110, 110, 110))
            cx += 22
        text(d, (cx, ay + 26), part, ui(16), (30, 30, 30), "lm")
        cx += d.textlength(part, font=ui(16)) + 6
    d.rounded_rectangle((sx, ay + 8, x1 - 16, ay + 44), 6, fill=(255, 255, 255), outline=(205, 205, 210))
    text(d, (sx + 14, ay + 26), fit(d, "Search " + crumbs[-1], ui(15), x1 - sx - 70), ui(15), (120, 120, 120), "lm")
    glyph(d, (x1 - 36, ay + 26), "search", 14, (90, 90, 90))
    d.line((x0 + 1, ay + 52, x1 - 1, ay + 52), fill=(225, 225, 230))
    # navigation pane
    by = ay + 53
    navw = 210
    for i, name in enumerate(NAV):
        yy = by + 14 + i * 36
        if name == nav:
            d.rounded_rectangle((x0 + 8, yy - 2, x0 + navw - 8, yy + 32), 6, fill=(220, 232, 248))
        if name in ("Home",):
            glyph(d, (x0 + 30, yy + 15), "home", 16, (60, 90, 140))
        elif name == "This PC":
            glyph(d, (x0 + 30, yy + 15), "pc", 16, (60, 90, 140))
        else:
            folder_icon(d, x0 + 20, yy + 4, 22)
        text(d, (x0 + 50, yy + 15), name, ui(15), (30, 30, 30), "lm")
    d.line((x0 + navw, by, x0 + navw, y1 - 30), fill=(232, 232, 236))
    # content
    cx0 = x0 + navw + 12
    rows = []
    cy = by + 10
    name_w = (x1 - cx0) * 0.46
    cols = [cx0 + 44, cx0 + name_w, cx0 + name_w + (x1 - cx0) * 0.24, cx0 + name_w + (x1 - cx0) * 0.40]
    if columns:
        for label, cxx in zip(("Name", "Date modified", "Type", "Size"), cols):
            text(d, (cxx if label != "Name" else cx0 + 12, cy + 14), label, ui(14), (90, 90, 90), "lm")
        cy += 34
        d.line((cx0, cy - 4, x1 - 12, cy - 4), fill=(235, 235, 238))
    for i, it in enumerate(items):
        kind, name = it[0], it[1]
        rb = (cx0, cy, x1 - 12, cy + 34)
        if name in selected:
            d.rounded_rectangle(rb, 4, fill=(204, 232, 255), outline=(153, 209, 255))
        elif name == hover:
            d.rounded_rectangle(rb, 4, fill=(229, 243, 255))
        item_icon(d, kind, cx0 + 10, cy + 4, 26)
        text(d, (cx0 + 44, cy + 17), name, ui(15), (20, 20, 20), "lm")
        for value, cxx in zip(it[2:], cols[1:]):
            text(d, (cxx, cy + 17), value, ui(14), (100, 100, 100), "lm")
        rows.append(rb)
        cy += 36
    text(d, (x0 + 14, y1 - 16), status or ("%d items" % len(items)), ui(13), (90, 90, 90), "lm")
    return rows


def context_menu(img, xy, entries, hover=None, icons_row=True):
    """Windows 11 context menu at xy. entries: list of (glyph or None, label, has_submenu). Returns boxes by label."""
    x, y = xy
    w = 300
    h = (56 if icons_row else 8) + len(entries) * 36 + 8
    box = (x, y, x + w, y + h)
    shadow(img, box, 8, 14, (0, 6), 90)
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle(box, 8, fill=(249, 249, 249), outline=(210, 210, 212))
    cy = y + 8
    if icons_row:
        for i, g in enumerate(("cut", "copy", "rename", "share", "delete")):
            glyph(d, (x + 30 + i * 52, cy + 20), g, 18, (50, 50, 50))
        cy += 48
        d.line((x + 8, cy - 2, x + w - 8, cy - 2), fill=(225, 225, 228))
    boxes = {}
    for g, label, sub in entries:
        rb = (x + 6, cy, x + w - 6, cy + 34)
        if label == hover:
            d.rounded_rectangle(rb, 5, fill=(230, 232, 238))
        if g:
            glyph(d, (x + 30, cy + 17), g, 16, (50, 50, 50))
        text(d, (x + 56, cy + 17), label, ui(15), (25, 25, 25), "lm")
        if sub:
            glyph(d, (x + w - 26, cy + 17), "chevron", 10, (80, 80, 80))
        boxes[label] = rb
        cy += 36
    return boxes


def button(d, box, label, primary=False, hover=False, dark=False):
    if dark:
        fill = (70, 110, 160) if primary else (60, 66, 76)
        d.rounded_rectangle(box, 4, fill=fill if not hover else tuple(min(255, c + 25) for c in fill))
        text(d, ((box[0] + box[2]) // 2, (box[1] + box[3]) // 2), label, ui(15), (235, 240, 245), "mm")
        return
    fill = (0, 95, 184) if primary else (253, 253, 253)
    if hover:
        fill = (25, 115, 200) if primary else (245, 245, 247)
    d.rounded_rectangle(box, 4, fill=fill, outline=(0, 85, 170) if primary else (200, 200, 205))
    text(d, ((box[0] + box[2]) // 2, (box[1] + box[3]) // 2), label, ui(15), (255, 255, 255) if primary else (25, 25, 25), "mm")


def checkbox(d, xy, checked, label, dark=False, size=20, fnt=None):
    x, y = xy
    if checked:
        d.rounded_rectangle((x, y, x + size, y + size), 4, fill=(0, 95, 184) if not dark else (200, 160, 80))
        glyph(d, (x + size / 2, y + size / 2 + 1), "check", int(size * 0.7), (255, 255, 255) if not dark else (20, 16, 10))
    else:
        d.rounded_rectangle((x, y, x + size, y + size), 4, fill=(255, 255, 255) if not dark else (25, 22, 18), outline=(120, 120, 120) if not dark else (180, 150, 90), width=2)
    if label:
        text(d, (x + size + 12, y + size / 2), label, fnt or ui(15), (25, 25, 25) if not dark else (225, 215, 195), "lm")
