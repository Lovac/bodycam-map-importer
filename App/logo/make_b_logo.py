# The Bodycam importer's logo: the Split Studio ball, its band map untouched, with a B brushed in the S's style.
# Reads E:/Dev/Studio_product/logo (read only). Writes to the importer repo: App/logo/ and Web/wwwroot/logo/.
import os, sys, math
from PIL import Image, ImageDraw, ImageFilter, ImageChops
SRC = 'E:/Dev/Studio_product/logo/'
OUT = sys.argv[1] if len(sys.argv) > 1 else 'E:/Dev/bodycam-map-importer/App/logo/'
RAMP = {0:(120,72,0),36:(176,112,0),72:(222,156,0),108:(255,203,5),144:(255,220,70),180:(255,236,140),216:(255,250,214)}
S_COL, SHADOW = (34,16,4), (150,78,10)
SS = 4                                        # supersample, as the S (4x)
BASE = 512

def bez(p0, p1, p2, p3, n=60):
    return [((1-t)**3*p0[0]+3*(1-t)**2*t*p1[0]+3*(1-t)*t*t*p2[0]+t**3*p3[0],
             (1-t)**3*p0[1]+3*(1-t)**2*t*p1[1]+3*(1-t)*t*t*p2[1]+t**3*p3[1]) for t in [i/(n-1) for i in range(n)]]

def brush(d, pts, w, k):
    """a stroke whose half-width follows w(0..1): pointed ends, fat belly, like the S"""
    n = len(pts); L, R = [], []
    for i, (x, y) in enumerate(pts):
        a = pts[max(0, i-1)]; b = pts[min(n-1, i+1)]
        dx, dy = b[0]-a[0], b[1]-a[1]; m = math.hypot(dx, dy) or 1
        h = w(i/(n-1)) / 2
        L.append(((x - dy/m*h)*k, (y + dx/m*h)*k)); R.append(((x + dy/m*h)*k, (y - dx/m*h)*k))
    d.polygon(L + R[::-1], fill=255)

def bump(lo, hi, peak=0.5, power=0.8):
    def f(t):
        u = t/peak if t < peak else (1-t)/(1-peak)
        return lo + (hi-lo)*max(0, math.sin(u*math.pi/2))**power
    return f

def glyph(size):
    k = size*SS/BASE
    im = Image.new('L', (size*SS, size*SS), 0); d = ImageDraw.Draw(im)
    # the stem: leans like the S, pointed at both ends, its foot flicks out to the lower left like the S's tail
    brush(d, bez((166,70),(150,104),(136,150),(92,194)), bump(1,30,0.42), k)
    # top: a flag from the upper left, over the top, round the small bowl, back into the waist
    ss = lambda t: t*t*(3-2*t)
    brush(d, bez((104,102),(136,66),(206,58),(206,92)), lambda t: 1 + 17*ss(min(1, t/0.8)), k)
    brush(d, bez((206,92),(206,114),(172,126),(142,128)), lambda t: 18 - 13*ss(t), k)
    # bottom: out of the waist, round the big bowl, sweeping back under to a point past the stem's foot
    brush(d, bez((142,128),(196,114),(232,134),(208,164)), lambda t: 5 + 21*ss(t), k)
    brush(d, bez((208,164),(192,184),(142,204),(96,190)), lambda t: 26*(1-ss(t))**0.8 + 1, k)
    return im.filter(ImageFilter.GaussianBlur(SS*0.35)).point(lambda v: 255 if v > 127 else 0)

def make_map(n):
    src = Image.open(SRC + f'logo_map_{n}.png').convert('RGBA')
    r, g, b, a = src.split()
    # the S comes off: its pixels kept their ball band in R, so the ball underneath is already whole
    r = r.point(lambda v: min(216, round(v/36)*36))
    big = glyph(n)
    off = (round(7*n/BASE)*SS, round(8*n/BASE)*SS)
    drop = ImageChops.offset(big, off[0], off[1])
    shadow = ImageChops.subtract(drop, big)
    G = big.resize((n, n), Image.LANCZOS); B = shadow.resize((n, n), Image.LANCZOS)
    G = ImageChops.multiply(G, a); B = ImageChops.multiply(B, a)
    return Image.merge('RGBA', (r, G, B, a))

def still(map_img):
    """the finished logo, flat: the ball's bands, the B, its drop edge (as sunflower_logo_*.png)"""
    n = map_img.width; out = Image.new('RGBA', (n, n)); px = out.load(); m = map_img.load()
    for y in range(n):
        for x in range(n):
            r, g, b, a = m[x, y]
            if not a: continue
            band = RAMP[min(216, round(r/36)*36)]
            s, h = g/255, b/255; o = max(0, 1-s-h)
            px[x, y] = tuple(round(band[c]*o + SHADOW[c]*h + S_COL[c]*s) for c in range(3)) + (a,)
    return out

os.makedirs(OUT, exist_ok=True)
maps = {}
for n in (64, 256, 512, 1024):
    maps[n] = make_map(n)
    maps[n].save(OUT + f'logo_map_{n}.png', optimize=True)
# (the Studio's own 128 map is an old format it no longer uses: the app's size comes from the 256)
r, g, b, a = maps[256].split()
small = Image.merge('RGBA', (r.resize((128, 128), Image.NEAREST), g.resize((128, 128), Image.LANCZOS), b.resize((128, 128), Image.LANCZOS), a.resize((128, 128), Image.LANCZOS)))
small.save(OUT + 'logo_map_128.png', optimize=True)
s512 = still(maps[512]); s512.save(OUT + 'logo_512.png', optimize=True)
still(maps[1024]).save(OUT + 'logo_1024.png', optimize=True)
still(maps[256]).save(OUT + 'logo_256.png', optimize=True)
big = still(maps[1024])
big.save(OUT + '../app.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print('maps', sorted(maps), 'written to', OUT)
