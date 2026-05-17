#!/usr/bin/env python3
"""Generates the UsbEthUsb Windows client icon: src/UsbEthUsb.Client/app.ico

Design: a USB trident whose three prongs end in network-node dots — white,
with a soft shadow, on a blue->cyan gradient rounded square. The glyph reads
as 'USB' (the trident silhouette) and as 'network' (a hub wired to nodes).

Drawn at high resolution and downscaled with LANCZOS for clean antialiasing.
Re-run from anywhere:  python3 tools/make-icon.py
"""
import os
from PIL import Image, ImageDraw, ImageFilter

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT_ICO = os.path.join(REPO, "src", "UsbEthUsb.Client", "app.ico")
OUT_PNG = os.path.join(REPO, "tools", "icon-preview.png")

SS = 1024                       # supersampled master canvas
RADIUS = 224                    # rounded-square corner radius
C1 = (30, 136, 229)             # gradient: blue   (#1E88E5)
C2 = (0, 200, 198)              # gradient: teal-cyan

# glyph geometry (on the 1024 canvas)
STROKE = 76
NODE_R = 84
ARROW = [(512, 148), (412, 316), (612, 316)]
SHAFT = [(512, 250), (512, 560)]
JUNCTION = (512, 560)
NODES = [(272, 720), (752, 720), (512, 800)]


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def draw_glyph(draw, colour):
    """Draw the USB-trident-with-nodes glyph in a single colour."""
    draw.polygon(ARROW, fill=colour)
    draw.line(SHAFT, fill=colour, width=STROKE)
    for node in NODES:
        draw.line([JUNCTION, node], fill=colour, width=STROKE)
    # round the central junction
    jx, jy = JUNCTION
    draw.ellipse([jx - STROKE // 2, jy - STROKE // 2,
                  jx + STROKE // 2, jy + STROKE // 2], fill=colour)
    # node dots
    for cx, cy in NODES:
        draw.ellipse([cx - NODE_R, cy - NODE_R,
                      cx + NODE_R, cy + NODE_R], fill=colour)


# --- diagonal gradient (built small, scaled up smoothly) ---
small = Image.new("RGB", (64, 64))
for y in range(64):
    for x in range(64):
        small.putpixel((x, y), lerp(C1, C2, (x + y) / 126))
gradient = small.resize((SS, SS), Image.BILINEAR)

# --- rounded-square mask ---
mask = Image.new("L", (SS, SS), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, SS - 1, SS - 1], RADIUS, fill=255)

icon = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
icon.paste(gradient, (0, 0), mask)

# --- soft shadow beneath the glyph, for depth ---
shadow = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
draw_glyph(ImageDraw.Draw(shadow), (0, 0, 25, 115))
shadow = shadow.filter(ImageFilter.GaussianBlur(20))
icon = Image.alpha_composite(icon, shadow)

# --- white glyph on top ---
draw_glyph(ImageDraw.Draw(icon), (255, 255, 255, 255))

# --- clip to the rounded square and emit ---
final = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
final.paste(icon, (0, 0), mask)

sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)]
final.save(OUT_ICO, format="ICO", sizes=sizes)
final.resize((256, 256), Image.LANCZOS).save(OUT_PNG)
print("wrote", OUT_ICO)
print("wrote", OUT_PNG)
