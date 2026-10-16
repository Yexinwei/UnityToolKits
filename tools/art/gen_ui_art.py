#!/usr/bin/env python3
"""生成 Curve Motion 测试场景的配套 UI 素材（休闲游戏风格，与 ic_coin 一致：饱和暖色、渐变、底部加厚边）。

输出到 Packages/com.limbo.curvemotion/Editor/TestScene/Art/：
  ic_wallet.png  钱包（金币栏图标）
  ic_gem.png     宝石
  ic_star.png    星星
  ui_panel.png   奖励弹窗底板（九宫格）
  ui_bar.png     资源栏胶囊底（九宫格）
每张图先按 4 倍尺寸绘制再缩小，边缘抗锯齿。需要 Pillow：python3 tools/art/gen_ui_art.py
已有 .meta 的不会改动 GUID；新文件的 .meta 由本脚本写入（Sprite、九宫格边距）。
"""
import math
import os
import uuid

from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 4  # 超采样倍数
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                   "Packages", "com.limbo.curvemotion", "Editor", "TestScene", "Art")


def hex_rgb(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def gradient(size, top, bottom):
    """竖直渐变。"""
    w, h = size
    img = Image.new("RGBA", size)
    px = img.load()
    t, b = hex_rgb(top), hex_rgb(bottom)
    for y in range(h):
        k = y / max(1, h - 1)
        c = tuple(int(t[i] + (b[i] - t[i]) * k) for i in range(4))
        for x in range(w):
            px[x, y] = c
    return img


def fill(canvas, mask, top, bottom=None):
    """在 mask 区域里填纯色或渐变（渐变按 mask 的包围盒）。"""
    box = mask.getbbox()
    if box is None:
        return
    if bottom is None:
        layer = Image.new("RGBA", canvas.size, hex_rgb(top))
    else:
        layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        g = gradient((box[2] - box[0], box[3] - box[1]), top, bottom)
        layer.paste(g, box[:2])
    canvas.alpha_composite(Image.composite(layer, Image.new("RGBA", canvas.size, (0, 0, 0, 0)), mask))


def shape(size, draw_fn):
    m = Image.new("L", size, 0)
    draw_fn(ImageDraw.Draw(m))
    return m


def offset(mask, dx, dy):
    return ImageChops.offset(mask, dx, dy)


def shrink(mask, px):
    """向内收缩 px（用模糊后提高阈值近似）。"""
    return mask.filter(ImageFilter.MinFilter(px * 2 + 1)) if px < 20 else mask.filter(ImageFilter.GaussianBlur(px)).point(lambda v: 255 if v > 230 else 0)


def round_mask(mask, radius):
    """把尖角磨圆。"""
    return mask.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: 255 if v >= 128 else 0)


def gloss(canvas, box, within, alpha=90, blur=18):
    """左上的柔和高光，只画在 within 形状里面。"""
    m = shape(canvas.size, lambda d: d.ellipse(box, fill=255)).filter(ImageFilter.GaussianBlur(blur * S / 4))
    m = ImageChops.multiply(m, within)
    layer = Image.new("RGBA", canvas.size, (255, 255, 255, alpha))
    canvas.alpha_composite(Image.composite(layer, Image.new("RGBA", canvas.size, (0, 0, 0, 0)), m))


def clip(layer_mask, region):
    return ImageChops.multiply(layer_mask, region)


def save(canvas, name, size):
    img = canvas.resize(size, Image.LANCZOS)
    img.save(os.path.join(OUT, name), optimize=True)
    print("写入", name, size)


# ---------------------------------------------------------------- 金币（钱包里露出来的）

def coin(canvas, cx, cy, r):
    size = canvas.size
    rim = shape(size, lambda d: d.ellipse((cx - r, cy - r + r * 0.12, cx + r, cy + r + r * 0.12), fill=255))
    fill(canvas, rim, "#F08A00")
    face = shape(size, lambda d: d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=255))
    fill(canvas, face, "#FFD23A", "#FFAE0A")
    inner = shape(size, lambda d: d.ellipse((cx - r * 0.7, cy - r * 0.7, cx + r * 0.7, cy + r * 0.7), fill=255))
    ring = ImageChops.subtract(shape(size, lambda d: d.ellipse((cx - r * 0.78, cy - r * 0.78, cx + r * 0.78, cy + r * 0.78), fill=255)), inner)
    fill(canvas, ring, "#F59A0A")
    gloss(canvas, (cx - r * 0.8, cy - r * 0.85, cx + r * 0.1, cy - r * 0.1), face, alpha=90, blur=10)


# ---------------------------------------------------------------- 钱包

def wallet():
    W, H = 200 * S, 170 * S
    c = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    # 露出的两枚金币
    coin(c, 120 * S, 52 * S, 34 * S)
    coin(c, 82 * S, 44 * S, 30 * S)

    body_box = (10 * S, 52 * S, 190 * S, 152 * S)
    r = 26 * S
    rim = shape(c.size, lambda d: d.rounded_rectangle((body_box[0], body_box[1] + 8 * S, body_box[2], body_box[3] + 8 * S), r, fill=255))
    fill(c, rim, "#6E3311")
    body = shape(c.size, lambda d: d.rounded_rectangle(body_box, r, fill=255))
    fill(c, body, "#E08A3C", "#B2582A")
    # 缝线：内缩的一圈浅色虚线
    stitch = Image.new("L", c.size, 0)
    sd = ImageDraw.Draw(stitch)
    inset = 9 * S
    x0, y0, x1, y1 = body_box[0] + inset, body_box[1] + inset, body_box[2] - inset, body_box[3] - inset
    dash, gap = 7 * S, 5 * S
    for (ax, ay, bx, by) in [(x0 + r * 0.5, y0, x1 - r * 0.5, y0), (x0 + r * 0.5, y1, x1 - r * 0.5, y1),
                             (x0, y0 + r * 0.5, x0, y1 - r * 0.5)]:
        length = math.hypot(bx - ax, by - ay)
        n = int(length // (dash + gap))
        for i in range(n + 1):
            s0 = i * (dash + gap)
            s1 = min(length, s0 + dash)
            sd.line((ax + (bx - ax) * s0 / length, ay + (by - ay) * s0 / length,
                     ax + (bx - ax) * s1 / length, ay + (by - ay) * s1 / length), fill=200, width=int(2.2 * S))
    fill(c, stitch, "#FFD8A8")
    gloss(c, (16 * S, 46 * S, 130 * S, 84 * S), body, alpha=55, blur=12)

    # 右侧搭扣
    flap_box = (118 * S, 78 * S, 198 * S, 128 * S)
    fr = 22 * S
    frim = shape(c.size, lambda d: d.rounded_rectangle((flap_box[0], flap_box[1] + 6 * S, flap_box[2], flap_box[3] + 6 * S), fr, fill=255))
    fill(c, frim, "#5E2A0E")
    flap = shape(c.size, lambda d: d.rounded_rectangle(flap_box, fr, fill=255))
    fill(c, flap, "#C8692C", "#94461C")
    coin(c, 150 * S, 103 * S, 13 * S)
    save(c, "ic_wallet.png", (200, 170))


# ---------------------------------------------------------------- 宝石

def gem():
    N = 140 * S
    c = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    k = S
    top_l, top_r = (40 * k, 22 * k), (100 * k, 22 * k)
    left, right = (10 * k, 52 * k), (130 * k, 52 * k)
    bottom = (70 * k, 128 * k)
    outline = [top_l, top_r, right, bottom, left]
    rim = round_mask(shape(c.size, lambda d: d.polygon([(x, y + 7 * k) for x, y in outline], fill=255)), 3 * S)
    fill(c, rim, "#0A3F8A")
    whole = round_mask(shape(c.size, lambda d: d.polygon(outline, fill=255)), 3 * S)
    fill(c, whole, "#1E8FE0")

    mid_l, mid_r = (52 * k, 52 * k), (88 * k, 52 * k)
    facets = [
        ([top_l, top_r, mid_r, mid_l], "#9BF0FF", "#62DBFF"),        # 台面
        ([left, top_l, mid_l], "#5CD3FF", None),                      # 左冠
        ([top_r, right, mid_r], "#3FB8F5", None),                     # 右冠
        ([left, mid_l, bottom], "#36A9F0", "#1D7FD8"),                # 左亭
        ([mid_l, mid_r, bottom], "#2A9AEB", "#156CC9"),               # 中亭
        ([mid_r, right, bottom], "#1878D2", "#0F55AE"),               # 右亭
    ]
    for poly, top, bot in facets:
        m = clip(shape(c.size, lambda d, p=poly: d.polygon(p, fill=255)), whole)
        fill(c, m, top, bot)
    # 闪光
    sx, sy, sr = 46 * k, 34 * k, 10 * k
    spark = shape(c.size, lambda d: d.polygon([(sx, sy - sr), (sx + sr * 0.25, sy - sr * 0.25), (sx + sr, sy),
                                               (sx + sr * 0.25, sy + sr * 0.25), (sx, sy + sr), (sx - sr * 0.25, sy + sr * 0.25),
                                               (sx - sr, sy), (sx - sr * 0.25, sy - sr * 0.25)], fill=255))
    fill(c, spark, "#FFFFFF")
    save(c, "ic_gem.png", (140, 140))


# ---------------------------------------------------------------- 星星

def star_points(cx, cy, ro, ri, n=5):
    pts = []
    for i in range(n * 2):
        a = -math.pi / 2 + i * math.pi / n
        r = ro if i % 2 == 0 else ri
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def star():
    N = 140 * S
    c = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    cx, cy = 70 * S, 74 * S
    pts = star_points(cx, cy, 64 * S, 30 * S)
    rim = round_mask(shape(c.size, lambda d: d.polygon([(x, y + 7 * S) for x, y in pts], fill=255)), 5 * S)
    fill(c, rim, "#E07000")
    face = round_mask(shape(c.size, lambda d: d.polygon(pts, fill=255)), 5 * S)
    fill(c, face, "#FFE45C", "#FFAA00")
    inner = round_mask(shape(c.size, lambda d: d.polygon(star_points(cx, cy + 2 * S, 40 * S, 19 * S), fill=255)), 4 * S)
    fill(c, inner, "#FFD23A", "#FFB81A")
    gloss(c, (30 * S, 14 * S, 84 * S, 64 * S), face, alpha=80, blur=10)
    save(c, "ic_star.png", (140, 140))


# ---------------------------------------------------------------- 九宫格底板

def panel():
    N = 160 * S
    c = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    r = 36 * S
    rim = shape(c.size, lambda d: d.rounded_rectangle((0, 6 * S, N - 1, N - 1), r, fill=255))
    fill(c, rim, "#B85A16")
    outer = shape(c.size, lambda d: d.rounded_rectangle((0, 0, N - 1, N - 7 * S), r, fill=255))
    fill(c, outer, "#FFB54A", "#EF8A26")
    inner = shape(c.size, lambda d: d.rounded_rectangle((10 * S, 10 * S, N - 1 - 10 * S, N - 17 * S), r - 10 * S, fill=255))
    fill(c, inner, "#FFF8E8", "#FBE6BE")
    save(c, "ui_panel.png", (160, 160))


def bar():
    W, H = 160 * S, 80 * S
    c = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    r = 34 * S
    rim = shape(c.size, lambda d: d.rounded_rectangle((0, 8 * S, W - 1, H - 1), r, fill=255))
    fill(c, rim, "#141B36")
    body = shape(c.size, lambda d: d.rounded_rectangle((0, 0, W - 1, H - 9 * S), r, fill=255))
    fill(c, body, "#3B4C86", "#26335F")
    shine = shape(c.size, lambda d: d.rounded_rectangle((14 * S, 6 * S, W - 1 - 14 * S, 20 * S), 7 * S, fill=255))
    layer = Image.new("RGBA", c.size, (255, 255, 255, 40))
    c.alpha_composite(Image.composite(layer, Image.new("RGBA", c.size, (0, 0, 0, 0)), shine))
    save(c, "ui_bar.png", (160, 80))


# ---------------------------------------------------------------- .meta

def write_meta(name, border):
    meta = os.path.join(OUT, name + ".meta")
    if os.path.exists(meta):
        return
    template = open(os.path.join(OUT, "ic_coin.png.meta"), encoding="utf-8").read()
    lines = []
    for line in template.splitlines():
        if line.startswith("guid: "):
            line = "guid: " + uuid.uuid4().hex
        elif line.strip().startswith("spriteBorder:"):
            line = "  spriteBorder: {x: %d, y: %d, z: %d, w: %d}" % border
        elif line.strip().startswith("spriteID:"):
            line = "    spriteID: " + uuid.uuid4().hex
        lines.append(line)
    with open(meta, "w", newline="\n", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("写入", name + ".meta")


if __name__ == "__main__":
    wallet()
    gem()
    star()
    panel()
    bar()
    for name, border in [("ic_wallet.png", (0, 0, 0, 0)), ("ic_gem.png", (0, 0, 0, 0)), ("ic_star.png", (0, 0, 0, 0)),
                         ("ui_panel.png", (48, 52, 48, 48)), ("ui_bar.png", (40, 38, 40, 38))]:
        write_meta(name, border)
