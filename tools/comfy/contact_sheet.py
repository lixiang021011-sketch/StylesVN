#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 Assets/Art 下的产物拼成两张总览图，方便人工挑选与验收。"""
import os, glob
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ART = os.path.join(ROOT, "Assets", "Resources", "Art")
OUT = os.path.join(ROOT, "ToolsOut")
os.makedirs(OUT, exist_ok=True)


def sheet(files, cols, cell, dst, label_h=26, bg=(16, 22, 20)):
    if not files:
        return None
    rows = (len(files) + cols - 1) // cols
    W = cols * cell[0]
    H = rows * (cell[1] + label_h)
    canvas = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(canvas)
    for i, f in enumerate(files):
        r, c = divmod(i, cols)
        im = Image.open(f)
        if im.mode == "RGBA":
            plate = Image.new("RGB", im.size, (210, 200, 180))
            plate.paste(im, mask=im.split()[3])
            im = plate
        else:
            im = im.convert("RGB")
        im.thumbnail(cell)
        x = c * cell[0] + (cell[0] - im.width) // 2
        y = r * (cell[1] + label_h) + (cell[1] - im.height) // 2
        canvas.paste(im, (x, y))
        d.text((c * cell[0] + 8, r * (cell[1] + label_h) + cell[1] + 5),
               os.path.basename(f).replace(".png", ""), fill=(201, 162, 39))
    canvas.save(dst)
    return dst


bg = sorted(glob.glob(os.path.join(ART, "Backgrounds", "*.png")))
ch = sorted(glob.glob(os.path.join(ART, "Characters", "*.png")))
print(sheet(bg, 4, (384, 216), os.path.join(OUT, "contact_backgrounds.png")))
print(sheet(ch, 7, (200, 292), os.path.join(OUT, "contact_characters.png")))
