"""Composes the YouTube / itch.io thumbnail from a still rendered with the -stills mode.
usage: python Tools/make_thumbnail.py [still.png] [out.png]
"""
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

src = sys.argv[1] if len(sys.argv) > 1 else "Builds/Windows/stills/cast.png"
out = sys.argv[2] if len(sys.argv) > 2 else "Builds/UmbraShift-thumbnail.png"
W, H = 1280, 720
FONTS = "C:/Windows/Fonts/"

def font(name, size):
    return ImageFont.truetype(FONTS + name, size)

# background: the game still, nudged left and slightly zoomed
still = Image.open(src).convert("RGB").resize((W, H), Image.LANCZOS)
bg = still.getpixel((W - 4, H // 2))
img = Image.new("RGB", (W, H), bg)
img.paste(still, (-45, 0))
ImageDraw.Draw(img).rectangle((W - 46, 0, W, H), fill=bg)

# soft violet glow behind the level + darkening on the text side
glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse((40, 40, 720, 700), fill=(110, 50, 200, 70))
glow = glow.filter(ImageFilter.GaussianBlur(120))
shade = Image.new("RGBA", (W, H), (0, 0, 0, 0))
sd = ImageDraw.Draw(shade)
for x in range(W):
    a = max(0, min(1, (x - 780) / 260))
    sd.line([(x, 0), (x, H)], fill=(10, 6, 22, int(170 * a)))
img = Image.alpha_composite(img.convert("RGBA"), glow)
img = Image.alpha_composite(img, shade)

# vignette
vig = Image.new("L", (W, H), 0)
ImageDraw.Draw(vig).ellipse((-200, -160, W + 200, H + 160), fill=255)
vig = vig.filter(ImageFilter.GaussianBlur(140))
dark = Image.new("RGBA", (W, H), (4, 2, 10, 255))
img = Image.composite(img, dark, vig)

def glow_text(base, xy, text, fnt, fill, glow_rgb, radius=14, strength=2, anchor="la"):
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.text(xy, text, font=fnt, fill=glow_rgb + (255,), anchor=anchor)
    blurred = layer.filter(ImageFilter.GaussianBlur(radius))
    for _ in range(strength):
        base = Image.alpha_composite(base, blurred)
    shadow = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).text((xy[0] + 4, xy[1] + 6), text, font=fnt, fill=(0, 0, 0, 200), anchor=anchor)
    base = Image.alpha_composite(base, shadow.filter(ImageFilter.GaussianBlur(3)))
    ImageDraw.Draw(base).text(xy, text, font=fnt, fill=fill, anchor=anchor)
    return base

title = font("seguibl.ttf", 150)
tx = 1225
img = glow_text(img, (tx, 70), "UMBRA", title, (232, 222, 255), (150, 90, 255), anchor="ra")
img = glow_text(img, (tx, 225), "SHIFT", title, (232, 222, 255), (150, 90, 255), anchor="ra")

line = font("seguibl.ttf", 50)
img = glow_text(img, (tx, 440), "WALLS \u2192 FLOORS", line, (110, 255, 220), (40, 200, 170), radius=10, strength=1, anchor="ra")
img = glow_text(img, (tx, 505), "SHADOWS \u2192 PLATFORMS", line, (205, 140, 255), (140, 60, 255), radius=10, strength=1, anchor="ra")

# tag pill
tag = font("segoeuib.ttf", 26)
d = ImageDraw.Draw(img)
text = "GAME OFF 2026  \u00b7  THEME: SHIFT"
tw = d.textlength(text, font=tag)
px, py = tx - tw - 28, 612
d.rounded_rectangle((px, py, tx, py + 50), radius=25, fill=(255, 200, 80, 255))
d.text((px + 14, py + 25), text, font=tag, fill=(30, 18, 40), anchor="lm")

img.convert("RGB").save(out)
print("wrote", out)
