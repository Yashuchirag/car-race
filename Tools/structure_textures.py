"""Draw the printed faces of the trackside structures: sponsor adverts for the billboards
and the braking distance boards.

    Tools/.venv/bin/python Tools/structure_textures.py OUT_DIR

The sponsors are made up, so no real brand appears in the game. Text is set in fonts
installed with the system (Lato, Roboto, EB Garamond; all under the SIL Open Font or Apache
licence, which allow using rendered text in images). Writes ad_0.png to ad_3.png (2048 x 716,
the billboards' 10 by 3.5 m) and board_150.png, board_100.png, board_50.png (512 x 384).
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

FONTS = Path("/usr/share/fonts/truetype")
W, H = 2048, 716


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), size)


def gradient(top, bottom):
    image = Image.new("RGB", (W, H))
    draw = ImageDraw.Draw(image)
    for y in range(H):
        t = y / (H - 1)
        draw.line([(0, y), (W, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)))
    return image


def centred(draw, y, text, face, fill, x=W // 2):
    box = draw.textbbox((0, 0), text, font=face)
    draw.text((x - (box[2] - box[0]) // 2 - box[0], y - box[1]), text, font=face, fill=fill)


def veloce():
    image = gradient((18, 18, 20), (38, 38, 42))
    draw = ImageDraw.Draw(image)
    draw.polygon([(0, H - 70), (W, H - 130), (W, H - 90), (0, H - 30)], fill=(255, 196, 0))
    centred(draw, 120, "VELOCE", font("lato/Lato-BlackItalic.ttf", 330), (255, 196, 0))
    centred(draw, 470, "PERFORMANCE TYRES", font("lato/Lato-Bold.ttf", 90), (235, 235, 235))
    return image


def northstar():
    image = gradient((8, 32, 92), (4, 12, 44))
    draw = ImageDraw.Draw(image)
    cx, cy, r = 240, H // 2, 160
    import math
    points = []
    for k in range(10):
        a = math.pi / 2 + k * math.pi / 5
        rr = r if k % 2 == 0 else r * 0.42
        points.append((cx + rr * math.cos(a), cy - rr * math.sin(a)))
    draw.polygon(points, fill=(255, 255, 255))
    centred(draw, 180, "NORTHSTAR", font("roboto/unhinted/RobotoTTF/Roboto-Black.ttf", 225), (255, 255, 255), x=1250)
    centred(draw, 470, "ENERGY  ·  SINCE 1968", font("roboto/unhinted/RobotoTTF/Roboto-Medium.ttf", 84), (150, 190, 255), x=1250)
    return image


def kestrel():
    image = gradient((200, 20, 24), (150, 10, 14))
    draw = ImageDraw.Draw(image)
    draw.rectangle([(0, 0), (W, 60)], fill=(255, 255, 255))
    draw.rectangle([(0, H - 60), (W, H)], fill=(255, 255, 255))
    centred(draw, 150, "KESTREL", font("lato/Lato-Black.ttf", 320), (255, 255, 255))
    centred(draw, 480, "RACING FUELS", font("lato/Lato-Bold.ttf", 90), (255, 225, 225))
    return image


def aurum():
    image = gradient((10, 48, 36), (4, 26, 20))
    draw = ImageDraw.Draw(image)
    gold = (212, 176, 96)
    draw.rectangle([(60, 60), (W - 60, H - 60)], outline=gold, width=6)
    centred(draw, 150, "AURUM", font("ebgaramond/EBGaramond12-Bold.ttf", 330), gold)
    centred(draw, 500, "SWISS CHRONOGRAPHS", font("lato/Lato-Regular.ttf", 76), (230, 220, 190))
    return image


def board(number):
    image = Image.new("RGB", (512, 384), (245, 245, 242))
    draw = ImageDraw.Draw(image)
    draw.rectangle([(0, 0), (511, 383)], outline=(20, 20, 20), width=18)
    face = font("roboto/unhinted/RobotoTTF/Roboto-Black.ttf", 230 if number >= 100 else 270)
    box = draw.textbbox((0, 0), str(number), font=face)
    draw.text((256 - (box[2] - box[0]) // 2 - box[0], 192 - (box[3] - box[1]) // 2 - box[1]), str(number),
              font=face, fill=(15, 15, 15))
    return image


def main(out_dir):
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    for k, make in enumerate((veloce, northstar, kestrel, aurum)):
        make().save(out / f"ad_{k}.png", optimize=True)
    for number in (150, 100, 50):
        board(number).save(out / f"board_{number}.png", optimize=True)
    print(f"4 adverts and 3 distance boards written to {out}")


if __name__ == "__main__":
    main(sys.argv[1])
