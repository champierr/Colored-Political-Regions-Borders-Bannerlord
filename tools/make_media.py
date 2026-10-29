"""Store images, from the screenshots in docs/screens/.

    python tools/make_media.py

  docs/images/cover.jpg              1920x1080 (Nexus main image, GitHub README)
  docs/images/workshop_preview.jpg   1024x1024, under 1 MB (Steam Workshop preview)
  docs/images/gallery_*.jpg          1920x1080 screenshots with a caption
"""
import os
from PIL import Image, ImageDraw, ImageFont

FONTS = r"C:\Windows\Fonts"
TITLE = "Colored Political Regions & Borders"
W, H = 1920, 1080
GOLD = (233, 196, 106)
PARCHMENT = (244, 232, 204)
INK = (20, 17, 14)


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def title_font(size):
    return font("BOOKOSB.TTF", size)


def body_font(size):
    return font("georgia.ttf", size)


def body_bold(size):
    return font("georgiab.ttf", size)


def cover_fit(img, size):
    """Scale and center-crop img to fill size."""
    w, h = size
    scale = max(w / img.width, h / img.height)
    img = img.resize((round(img.width * scale), round(img.height * scale)), Image.LANCZOS)
    left, top = (img.width - w) // 2, (img.height - h) // 2
    return img.crop((left, top, left + w, top + h))


def gradient_band(img, top, bottom, strength=200, from_top=False):
    """Vertical fade to black between top and bottom (for text over screenshots)."""
    band = Image.new("L", (1, bottom - top))
    for y in range(bottom - top):
        t = y / max(1, bottom - top - 1)
        band.putpixel((0, y), int(strength * ((1 - t) if from_top else t)))
    mask = Image.new("L", img.size, 0)
    mask.paste(band.resize((img.width, bottom - top)), (0, top))
    return Image.composite(Image.new("RGB", img.size, (0, 0, 0)), img, mask)


def text_center(draw, y, text, fnt, fill, width, shadow=True):
    x = (width - draw.textlength(text, font=fnt)) / 2
    if shadow:
        draw.text((x + 3, y + 3), text, font=fnt, fill=(0, 0, 0))
    draw.text((x, y), text, font=fnt, fill=fill)


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHOTS = os.path.join(ROOT, "docs", "screens")
IMAGES = os.path.join(ROOT, "docs", "images")


def shot(name, crop=(0, 0, 1920, 890)):
    """A screenshot without the bottom bar and the "paused" text."""
    img = Image.open(os.path.join(SHOTS, name)).convert("RGB")
    return img.crop(crop) if crop else img


def save(img, name):
    img.save(os.path.join(IMAGES, name), quality=90)


def make_cover():
    img = cover_fit(shot("overview.jpg"), (W, H))
    img = gradient_band(img, 0, 420, 230, from_top=True)
    img = gradient_band(img, 760, H, 235)
    d = ImageDraw.Draw(img)
    text_center(d, 70, TITLE, title_font(92), GOLD, W)
    text_center(d, 190, "A political map for Mount & Blade II: Bannerlord", body_font(46), PARCHMENT, W)
    text_center(d, 880, "Zoom out: kingdom colours   •   Zoom in: terrain with borders and glow", body_font(40), PARCHMENT, W)
    text_center(d, 950, "Follows every conquest live   •   Ctrl+1: colour strength   •   no other mods needed",
                body_font(30), (210, 200, 180), W)
    save(img, "cover.jpg")


def make_workshop_preview():
    size = 1024
    img = cover_fit(shot("overview.jpg"), (size, size))
    img = gradient_band(img, 0, 360, 235, from_top=True)
    img = gradient_band(img, 720, size, 220)
    d = ImageDraw.Draw(img)
    text_center(d, 60, "Colored Political", title_font(96), GOLD, size)
    text_center(d, 165, "Regions & Borders", title_font(96), GOLD, size)
    text_center(d, 880, "Political map for Bannerlord", body_font(46), PARCHMENT, size)
    path = os.path.join(IMAGES, "workshop_preview.jpg")
    for quality in (90, 85, 80, 75):
        img.save(path, quality=quality)
        if os.path.getsize(path) < 1000 * 1000:
            break


def side_by_side(name, left, right, title, left_label, right_label):
    img = Image.new("RGB", (W, H), INK)
    img.paste(cover_fit(left, (940, 880)), (13, 150))
    img.paste(cover_fit(right, (940, 880)), (967, 150))
    d = ImageDraw.Draw(img)
    text_center(d, 36, title, title_font(50), GOLD, W)
    for x, label in ((30, left_label), (984, right_label)):
        w = d.textlength(label, font=body_bold(30))
        d.rectangle((x - 6, 168, x + w + 14, 214), fill=(0, 0, 0))
        d.text((x + 4, 172), label, font=body_bold(30), fill=PARCHMENT)
    save(img, "gallery_%s.jpg" % name)


def gallery(name, img, caption):
    img = cover_fit(img, (W, H))
    img = gradient_band(img, 860, H, 235)
    d = ImageDraw.Draw(img)
    text_center(d, 960, caption, body_bold(44), PARCHMENT, W)
    save(img, "gallery_%s.jpg" % name)


def main():
    os.makedirs(IMAGES, exist_ok=True)
    make_cover()
    make_workshop_preview()
    side_by_side("zoom_levels", shot("overview.jpg"), shot("border_close.jpg"),
                 "Zoom out: political map   •   Zoom in: terrain, borders stay", "Zoomed out", "Zoomed in")
    side_by_side("conquest", shot("overview.jpg", (520, 100, 1440, 760)), shot("conquest_vlandia.jpg", (420, 180, 1340, 840)),
                 "The map follows every conquest", "Before: Poros (Southern Empire)", "After: Poros taken by Vlandia")
    gallery("overview", shot("overview.jpg"), "Every town, castle and village region in its owner's colour")
    gallery("border_glow", shot("border_close.jpg"), "Zoomed in: the terrain stays, borders keep a glow of each side's colour")
    gallery("new_owners", shot("conquest_sturgia.jpg"), "New owners get their colours at once – your own clan too")
    gallery("night", shot("night_far.jpg"), "At night the colours dim with the light")
    for f in sorted(os.listdir(IMAGES)):
        print(f, os.path.getsize(os.path.join(IMAGES, f)))


if __name__ == "__main__":
    main()
