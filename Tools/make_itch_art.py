"""Builds itch.io page art into Builds/itch/: a 630x500 cover and 1920x1080 screenshots.
Inputs: stills from the -stills mode (Builds/Windows/stills) and the trailer MP4.
usage: python Tools/make_itch_art.py
"""
import os, subprocess
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = "Builds/itch"
STILLS = "Builds/Windows/stills"
FONTS = "C:/Windows/Fonts/"
os.makedirs(OUT, exist_ok=True)


def font(name, size):
    return ImageFont.truetype(FONTS + name, size)


def glow_text(base, xy, text, fnt, fill, glow_rgb, radius=10, strength=2, anchor="mm"):
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).text(xy, text, font=fnt, fill=glow_rgb + (255,), anchor=anchor)
    blurred = layer.filter(ImageFilter.GaussianBlur(radius))
    for _ in range(strength):
        base = Image.alpha_composite(base, blurred)
    sh = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).text((xy[0] + 3, xy[1] + 4), text, font=fnt, fill=(0, 0, 0, 210), anchor=anchor)
    base = Image.alpha_composite(base, sh.filter(ImageFilter.GaussianBlur(2)))
    ImageDraw.Draw(base).text(xy, text, font=fnt, fill=fill, anchor=anchor)
    return base


# ---------------- cover 630x500 ----------------
CW, CH = 630, 500
still = Image.open(f"{STILLS}/umbra.png").convert("RGB")      # 2560x1440, level on the left
sw, sh = still.size
# crop around the level (left ~55% of the frame), keep cover aspect
crop_h = int(sh * 0.98)
crop_w = int(crop_h * CW / CH)
cx = int(sw * 0.30)
box = (max(0, cx - crop_w // 2), (sh - crop_h) // 2, max(0, cx - crop_w // 2) + crop_w, (sh - crop_h) // 2 + crop_h)
cover = still.crop(box).resize((CW, CH), Image.LANCZOS).convert("RGBA")

shade = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
d = ImageDraw.Draw(shade)
for y in range(CH):
    top = max(0, 1 - y / 190)
    bot = max(0, (y - 330) / 170)
    d.line([(0, y), (CW, y)], fill=(8, 5, 18, int(235 * max(top, bot))))
cover = Image.alpha_composite(cover, shade)
cover = glow_text(cover, (CW // 2, 78), "UMBRA SHIFT", font("seguibl.ttf", 78), (236, 226, 255), (150, 90, 255), radius=12)
cover = glow_text(cover, (CW // 2, 410), "WALLS \u2192 FLOORS", font("seguibl.ttf", 34), (110, 255, 220), (40, 200, 170), radius=8, strength=1)
cover = glow_text(cover, (CW // 2, 455), "SHADOWS \u2192 PLATFORMS", font("seguibl.ttf", 34), (205, 140, 255), (140, 60, 255), radius=8, strength=1)
cover.convert("RGB").save(f"{OUT}/cover-630x500.png")

# ---------------- screenshots 1920x1080 ----------------
def still_shot(name, out):
    im = Image.open(f"{STILLS}/{name}.png").convert("RGB")
    w, h = im.size
    # stills are framed left for the thumbnail; re-centre the level
    shift = int(w * 0.17)
    bg = im.getpixel((w - 4, h // 2))
    c = Image.new("RGB", (w, h), bg)
    c.paste(im, (shift, 0))
    ImageDraw.Draw(c).rectangle((0, 0, shift, h), fill=bg)
    c.resize((1920, 1080), Image.LANCZOS).save(f"{OUT}/{out}")

still_shot("twin", "screenshot-2-twin-lamps.png")
still_shot("umbra", "screenshot-3-umbra.png")
still_shot("undercroft", "screenshot-4-undercroft.png")

def frame(t, out):
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-ss", str(t), "-i", "Builds/UmbraShift-trailer.mp4",
                    "-frames:v", "1", f"{OUT}/{out}"], check=True)

frame(15.0, "screenshot-1-shadows-are-solid.png")
frame(54.0, "screenshot-5-title.png")
print(sorted(os.listdir(OUT)))
