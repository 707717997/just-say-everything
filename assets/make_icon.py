"""生成 app.ico：橙色圆角底 + 白色话筒 + 声波。小尺寸单独画，去掉声波、话筒放大。"""
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SS = 1024


def gradient(top, bottom):
    col = Image.new("RGB", (1, SS))
    for y in range(SS):
        t = y / (SS - 1)
        col.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return col.resize((SS, SS)).convert("RGBA")


def mic_mask(scale, cx, cy, waves):
    m = Image.new("L", (SS, SS), 0)
    d = ImageDraw.Draw(m)
    w, h = 214 * scale, 372 * scale            # 话筒头
    top = cy - 300 * scale
    d.rounded_rectangle([cx - w / 2, top, cx + w / 2, top + h], radius=w / 2, fill=255)
    lw = int(48 * scale)
    r = 186 * scale                             # 托架
    ay = top + h - 150 * scale
    d.arc([cx - r, ay - r + 40 * scale, cx + r, ay + r + 40 * scale], 0, 180, fill=255, width=lw)
    y0 = ay + r + 40 * scale
    d.rounded_rectangle([cx - lw / 2, y0 - 8, cx + lw / 2, y0 + 92 * scale], radius=lw / 3, fill=255)
    d.rounded_rectangle([cx - 118 * scale, y0 + 74 * scale, cx + 118 * scale, y0 + 74 * scale + lw],
                        radius=lw / 2, fill=255)
    if waves:
        for i, rr in enumerate((300, 392)):
            big_r = rr * scale
            yy = top + h / 2
            ww = int(40 * scale)
            shade = 255 if i == 0 else 190
            d.arc([cx - big_r, yy - big_r, cx + big_r, yy + big_r], -34, 34, fill=shade, width=ww)
            d.arc([cx - big_r, yy - big_r, cx + big_r, yy + big_r], 146, 214, fill=shade, width=ww)
    return m


def render(size):
    small = size <= 32
    pad = 0 if small else 44
    bg = gradient((255, 176, 84), (236, 84, 28))
    shape = Image.new("L", (SS, SS), 0)
    radius = 200 if small else 230
    ImageDraw.Draw(shape).rounded_rectangle([pad, pad, SS - pad, SS - pad], radius=radius, fill=255)

    # 顶部一层柔光，让底色不发平
    gloss = Image.new("L", (SS, SS), 0)
    ImageDraw.Draw(gloss).ellipse([-200, -640, SS + 200, 430], fill=46)
    gloss = gloss.filter(ImageFilter.GaussianBlur(70))
    white = Image.new("RGBA", (SS, SS), (255, 255, 255, 255))
    bg = Image.composite(white, bg, gloss)

    scale, waves = (1.32, False) if small else (1.0, True)
    cy = SS / 2 + (10 if small else 0)
    mic = mic_mask(scale, SS / 2, cy, waves)
    if not small:
        shadow = mic.filter(ImageFilter.GaussianBlur(20)).point(lambda v: int(v * 0.30))
        off = Image.new("L", (SS, SS), 0)
        off.paste(shadow, (0, 16))
        bg = Image.composite(Image.new("RGBA", (SS, SS), (120, 34, 0, 255)), bg, off)
    bg = Image.composite(white, bg, mic)
    bg.putalpha(shape)
    return bg.resize((size, size), Image.LANCZOS)


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = {s: render(s) for s in sizes}
    ico = os.path.join(HERE, "app.ico")
    imgs[256].save(ico, format="ICO", append_images=[imgs[s] for s in sizes[:-1]],
                   sizes=[(s, s) for s in sizes])
    imgs[256].save(os.path.join(HERE, "app-256.png"))
    # 预览：浅色和深色底上的各尺寸
    sheet = Image.new("RGB", (1000, 340), (255, 255, 255))
    ImageDraw.Draw(sheet).rectangle([500, 0, 1000, 340], fill=(32, 34, 40))
    for ox in (0, 500):
        x = ox + 14
        for s in (256, 64, 48, 32, 24, 16):
            im = imgs[s]
            sheet.paste(im, (x, 40), im)
            x += s + 12
    if len(sys.argv) > 1:
        sheet.save(sys.argv[1])
    print(ico, os.path.getsize(ico))


main()
