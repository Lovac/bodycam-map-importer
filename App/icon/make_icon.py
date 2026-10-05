"""Draws the app icon.

    py -3 make_icon.py <out.ico> <preview.png>
"""
import os
import sys

from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
WINDOW = (0x16, 0x17, 0x1B, 255)
RAISED = (0x26, 0x28, 0x2F, 255)
BORDER = (0x3A, 0x3D, 0x47, 255)
GREEN = (0x4C, 0xC3, 0x8A, 255)
SS = 8


def draw(size):
    big = size * SS
    im = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    u = big / 64.0

    small = size < 48
    margin = (1.0 if small else 2.0) * u
    radius = (12 if small else 13) * u
    d.rounded_rectangle([margin, margin, big - margin - 1, big - margin - 1], radius=radius, fill=RAISED if small else WINDOW,
                        outline=BORDER, width=max(1, int(round((0 if small else 1.5) * u))) if not small else 0)

    if not small:

        inset = 9 * u
        x0, y0, x1, y1 = inset, inset, big - inset, big - inset
        dash, gap, w = 5.0 * u, 3.5 * u, max(1, int(round(2.2 * u)))
        for (ax, ay, bx, by) in [(x0, y0, x1, y0), (x1, y0, x1, y1), (x1, y1, x0, y1), (x0, y1, x0, y0)]:
            length = abs(bx - ax) + abs(by - ay)
            t = 4 * u
            while t < length - 4 * u:
                e = min(t + dash, length - 4 * u)
                fx = ax + (bx - ax) * (t / length)
                fy = ay + (by - ay) * (t / length)
                gx = ax + (bx - ax) * (e / length)
                gy = ay + (by - ay) * (e / length)
                d.line([fx, fy, gx, gy], fill=BORDER, width=w)
                t += dash + gap

    stroke = (7.0 if small else 5.0) * u
    cx = big / 2.0
    top = (13 if small else 17) * u
    tip = (38 if small else 37) * u
    wing = (11 if small else 10) * u
    tray_y = (51 if small else 47) * u
    tray_half = (20 if small else 16) * u

    def cap_line(ax, ay, bx, by):
        d.line([ax, ay, bx, by], fill=GREEN, width=int(round(stroke)))
        r = stroke / 2.0
        for (px, py) in [(ax, ay), (bx, by)]:
            d.ellipse([px - r, py - r, px + r, py + r], fill=GREEN)

    cap_line(cx, top, cx, tip)
    cap_line(cx - wing, tip - wing, cx, tip)
    cap_line(cx + wing, tip - wing, cx, tip)
    cap_line(cx - tray_half, tray_y, cx + tray_half, tray_y)
    return im.resize((size, size), Image.LANCZOS)


def main(argv):
    if len(argv) != 3:
        print("usage: py -3 make_icon.py <out.ico> <preview.png>")
        return 2
    out_ico, out_png = argv[1], argv[2]
    for p in (out_ico, out_png):
        if os.path.exists(p):
            print("refused: %s already exists (move it aside first; nothing is overwritten)" % p)
            return 3
    images = [draw(s) for s in SIZES]
    biggest = images[-1]
    biggest.save(out_ico, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    draw(256).save(out_png, format="PNG")
    back = Image.open(out_ico)
    got = sorted(back.info.get("sizes", set()))
    print("wrote %s (%d bytes) sizes %s" % (out_ico, os.path.getsize(out_ico), " ".join("%dx%d" % s for s in got)))

    for s in (16, 32, 48):
        back.size = (s, s)
        frame = back.copy().convert("RGBA")
        same = frame.tobytes() == images[SIZES.index(s)].tobytes()
        print("  %dx%d frame equals its own %d px drawing: %s" % (s, s, s, same))
    print("wrote %s (%d bytes)" % (out_png, os.path.getsize(out_png)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
