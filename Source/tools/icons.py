#!/usr/bin/env python3
"""Generates every app icon and splash artwork used by the three shells
(Android mipmap set, iOS CFBundleIconFiles set + launch image, and the
Windows .ico), from one procedural master painting. Uses Pillow only -
no content pipeline, mirroring the book's zero-asset rule.

Run:  python3 tools/icons.py
"""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, ".."))

# -- palette ----------------------------------------------------------------
NAVY      = (24, 26, 38)      # backbuffer clear colour, ties brand to game
NAVY_EDGE = (16, 17, 26)
CREAM     = (240, 233, 214)   # light squares
BRONZE    = (110, 88, 60)     # dark squares
GOLD      = (214, 178, 94)
GOLD_DK   = (158, 128, 62)
WHITE     = (245, 244, 240)
SHADOW    = (12, 12, 18)


def master(size=1024):
    """The brand mark: a pawn silhouette on the game's navy plate with a
    bronze checker strip at the base - the whole identity in one read."""
    s = size
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    # rounded square plate
    r = int(s * 0.22)
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=r,
                        fill=NAVY, outline=NAVY_EDGE, width=max(2, s // 128))

    # subtle inner rim
    inset = int(s * 0.035)
    d.rounded_rectangle((inset, inset, s - 1 - inset, s - 1 - inset),
                        radius=int(r * 0.86),
                        outline=GOLD_DK, width=max(2, s // 256))

    # pawn head
    cx, head_r = s * 0.50, s * 0.20
    head_y = s * 0.34
    d.ellipse((cx - head_r, head_y - head_r, cx + head_r, head_y + head_r),
              fill=GOLD)
    # collar
    d.rounded_rectangle((cx - head_r * 0.95, head_y + head_r * 0.65,
                         cx + head_r * 0.95, head_y + head_r * 1.15),
                        radius=int(head_r * 0.35), fill=GOLD)
    # neck wedge
    neck_top = head_y + head_r * 0.95
    neck_bot = s * 0.66
    wedge_w0, wedge_w1 = head_r * 0.80, head_r * 1.30
    d.polygon([(cx - wedge_w0, neck_top), (cx + wedge_w0, neck_top),
               (cx + wedge_w1, neck_bot), (cx - wedge_w1, neck_bot)],
              fill=GOLD)
    # base bar
    d.rounded_rectangle((cx - s * 0.24, s * 0.66, cx + s * 0.24, s * 0.715),
                        radius=int(s * 0.02), fill=GOLD)

    # checker strip: four bronze/cream pads along the base
    pad_y0, pad_y1 = s * 0.78, s * 0.88
    pad_w = s * 0.17
    gap = s * 0.055
    n = 4
    x0 = cx - (n * pad_w + (n - 1) * gap) / 2
    for k in range(n):
        col = CREAM if k % 2 == 0 else BRONZE
        p0 = x0 + k * (pad_w + gap)
        d.rounded_rectangle((p0, pad_y0, p0 + pad_w, pad_y1),
                            radius=int(s * 0.015), fill=col)

    return im


def rounded(im, radius_factor=0.225):
    s = im.size[0]
    m = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(m)
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=int(s * radius_factor),
                        fill=(255, 255, 255, 255))
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    out.paste(im, (0, 0), m)
    return out


def splash(size):
    """Portrait launch image: brand plate centered over the game's navy with
    a faint checkerboard fading toward the edges."""
    w, h = size
    im = Image.new("RGB", size, NAVY)
    d = ImageDraw.Draw(im)
    # faint checkerboard
    cell = max(24, h // 42)
    for y in range(0, h, cell):
        for x in range(0, w, cell):
            if (x // cell + y // cell) % 2 == 0:
                d.rectangle((x, y, x + cell, y + cell), fill=(26, 28, 41))
    # vignette: simple top/bottom shade bands
    for y in range(0, h // 6):
        a = 1 - y / (h / 6)
    # brand plate
    plate = int(min(w, h) * 0.42)
    pl = master(plate).resize((plate, plate), Image.LANCZOS)
    # navy-out the transparent corners of the master for the splash centre
    bg = Image.new("RGBA", (plate, plate), (*NAVY, 255))
    pl_rgba = Image.alpha_composite(bg, pl)
    px = (w - plate) // 2
    py = (h - plate) // 2
    im.paste(pl_rgba, (px, py), pl)
    return im


def emit(dst, im, size):
    imc = im.resize((size, size), Image.LANCZOS) if im.size[0] != size else im
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    imc.save(dst)
    print("wrote", dst, size)


def main():
    brand = os.path.join(SRC, "Assets", "Branding")
    os.makedirs(brand, exist_ok=True)

    m = master(1024)

    # master + rounded full-bleed variant for stores
    m.save(os.path.join(brand, "icon-1024.png"))
    rb = rounded(master(1024))
    rb.save(os.path.join(brand, "icon-1024-rounded.png"))

    # ---- iOS icons (CFBundleIconFiles set) ------------------------------
    ios = os.path.join(SRC, "platforms", "iOS", "Assets")
    os.makedirs(ios, exist_ok=True)
    iphone = m  # iOS masks corners itself
    for size, name in ((180, "AppIcon60@3x"), (120, "AppIcon60@2x"),
                       (152, "AppIcon76@2x"), (167, "ProIcon83.5@2x")):
        iphone.resize((size, size), Image.LANCZOS).save(
            os.path.join(ios, name + ".png"))
        print("wrote", name, size)

    # ---- iOS launch (splash) image: 430x932 pt @3x, i.e. the largest phone.
    # UILaunchScreen centres the image at its natural point size, so a @3x
    # asset sized for the biggest screen renders full-bleed and crops evenly
    # (invisibly) on smaller ones - no white letterboxing.
    sp = splash((1290, 2796))
    sp.save(os.path.join(ios, "Splash-Portrait@3x.png"))
    print("wrote Splash-Portrait@3x 1290x2796")

    # ---- Android mipmaps ------------------------------------------------
    res = os.path.join(SRC, "platforms", "Android", "Resources")
    for dpi, px in (("mdpi", 48), ("hdpi", 72), ("xhdpi", 96),
                    ("xxhdpi", 144), ("xxxhdpi", 192)):
        os.makedirs(os.path.join(res, "mipmap-" + dpi), exist_ok=True)
        rounded(m).resize((px, px), Image.LANCZOS).save(
            os.path.join(res, "mipmap-" + dpi, "ic_launcher.png"))
        print("wrote mipmap-" + dpi, px)

    # ---- Android launch screen art (centred by a layer-list) --------------
    art = os.path.join(res, "drawable-nodpi")
    os.makedirs(art, exist_ok=True)
    plate = int(min(m.size) * 0.62)
    m.resize((plate, plate), Image.LANCZOS).save(os.path.join(art, "splash_art.png"))
    print("wrote drawable-nodpi/splash_art", plate)

    # ---- Windows .ico ----------------------------------------------------
    ifo = os.path.join(brand, "Chess.Desktop.ico")
    sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    m_r = rounded(m)
    m_r.resize((256, 256), Image.LANCZOS).save(
        ifo, sizes=[(256, 256)], append_images=[
            m_r.resize(sz, Image.LANCZOS) for sz in sizes[1:]])
    print("wrote", ifo)


if __name__ == "__main__":
    main()
