// The Bodycam Map Importer logo: a frosted-glass banded ball with a B, in the Split Studio logo's style.
// The colour turns round the arms of a swirl, the far side of the glass shows through turning the other way, slow sparkles rise off the glass.
// While a map is worked on the colour grows from grey out of the middle. Everything is a slow fade: nothing flashes.
(function (root) {
  const BANDS = [[120,72,0],[176,112,0],[222,156,0],[255,203,5],[255,220,70],[255,236,140],[255,250,214]], B = [34,16,4], SHADOW = [150,78,10];
  const SPIN = 0.35, WAVE = 0.30, TWIST = 5.5, BACK = 0.24, FPS = 24;
  const STILL = root.matchMedia && root.matchMedia("(prefers-reduced-motion: reduce)").matches;
  function along(u, out) {
    const x = Math.max(0, Math.min(1, u)) * 6, i = Math.min(5, x | 0), f = x - i, a = BANDS[i], c = BANDS[i + 1];
    out[0] = a[0] + (c[0] - a[0]) * f; out[1] = a[1] + (c[1] - a[1]) * f; out[2] = a[2] + (c[2] - a[2]) * f;
  }
  const ease = x => { x = Math.max(0, Math.min(1, x)); return x * x * (3 - 2 * x); };
  function prep(map) {
    const W = map.width, d = map.data, n = W * W, r = W * 0.46, c = W / 2 - 0.5;
    const L = [-0.45, -0.55, 0.70], m = Math.hypot(...L), lx = c + L[0] / m * r, ly = c + L[1] / m * r;
    const band = new Float32Array(n), seed = new Float32Array(n), sheen = new Float32Array(n), dn = new Float32Array(n), ca = new Float32Array(n), spots = [];
    let s = 12345; const rnd = () => (s = (s * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
    for (let i = 0; i < n; i++) {
      const x = i % W, y = (i / W) | 0;
      band[i] = Math.min(6, Math.round(d[i * 4] / 36)) / 6; seed[i] = rnd();
      const q = Math.hypot(x - lx, y - ly) / (r * 0.9); sheen[i] = Math.max(0, 1 - q) ** 3; dn[i] = Math.hypot(x - c, y - c) / r; ca[i] = Math.atan2(y - c, x - c);
      if (d[i * 4 + 3] && !d[i * 4 + 1] && !d[i * 4 + 2] && dn[i] < 0.92) spots.push(i);
    }
    return { band, seed, sheen, dn, ca, spots };
  }
  async function mount(canvas, base, size) {
    const px = Math.round(size * Math.max(1, root.devicePixelRatio || 1)), n = px > 128 ? 256 : px > 64 ? 128 : 64;
    const im = new Image(); im.src = base + "logo/logo_map_" + n + ".png"; await im.decode();
    const tmp = document.createElement("canvas"); tmp.width = tmp.height = n; const tc = tmp.getContext("2d", { willReadFrequently: true }); tc.drawImage(im, 0, 0);
    canvas.width = canvas.height = px; canvas.style.width = canvas.style.height = size + "px";
    const ctx = canvas.getContext("2d"); ctx.imageSmoothingEnabled = true; ctx.imageSmoothingQuality = "high";
    const map = tc.getImageData(0, 0, n, n), img = tc.createImageData(n, n), A = prep(map), sparks = [];
    let t0 = performance.now(), growT = 1.4, working = false, fin = null, last = 0, sparkT = 0, debt = 0;
    const f = [0, 0, 0], k = [0, 0, 0];
    function grow() {
      if (STILL) return 9;
      const now = performance.now();
      if (fin) { const t = (now - fin.t) / 600; if (t >= 1) { fin = null; growT = 0; return 9; } return fin.g + (1.3 - fin.g) * ease(t); }
      if (!growT) return 9;
      const g = 1.25 * (1 - Math.exp(-3 * ((now - t0) / 1000) / growT));
      if (!working && g > 1.24) { growT = 0; return 9; }
      return g;
    }
    function paint(t) {
      const src = map.data, d = img.data, ph = STILL ? 0 : t * SPIN, G = grow();
      const sx = STILL ? 0 : (0.5 + 0.5 * Math.sin(t * 0.25)) * 2, si = Math.min(1, sx | 0), sf = sx - si, S3 = [B, BANDS[0], BANDS[1]];
      const sc = [0, 1, 2].map(c => S3[si][c] + (S3[si + 1][c] - S3[si][c]) * sf);
      for (let i = 0, p = 0; i < src.length; i += 4, p++) {
        if (!src[i + 3]) { d[i + 3] = 0; continue; }
        const sw = A.ca[p] - A.dn[p] * TWIST;
        along(A.band[p] + WAVE * Math.sin(sw + ph), f);
        along(A.band[p] * 0.7 + 0.15 + WAVE * Math.sin(-sw - ph * 0.8), k);
        const s = src[i + 1] / 255, h = src[i + 2] / 255, o = Math.max(0, 1 - s - h), w = A.sheen[p] * 70;
        let r = (f[0] + (k[0] - f[0]) * BACK + w) * o + SHADOW[0] * h + sc[0] * s;
        let g = (f[1] + (k[1] - f[1]) * BACK + w) * o + SHADOW[1] * h + sc[1] * s;
        let b = (f[2] + (k[2] - f[2]) * BACK + w) * o + SHADOW[2] * h + sc[2] * s;
        if (G < 2) {
          const arm = (((sw + ph) / 6.2832) % 1 + 1) % 1, cm = Math.max(0, Math.min(1, (G - A.dn[p] - arm * 0.35 - A.seed[p] * 0.08) / 0.12));
          if (cm < 1) { const l = 0.299 * r + 0.587 * g + 0.114 * b; r = l + (r - l) * cm; g = l + (g - l) * cm; b = l + (b - l) * cm; }
        }
        d[i] = Math.min(255, r); d[i + 1] = Math.min(255, g); d[i + 2] = Math.min(255, b);
        d[i + 3] = src[i + 3] * Math.min(1, o * 0.80 + s + h);
      }
      tc.putImageData(img, 0, 0);
      ctx.clearRect(0, 0, px, px); ctx.drawImage(tmp, 0, 0, px, px);
      if (STILL || !A.spots.length) return;
      const now = performance.now(), dt = Math.min(0.1, sparkT ? (now - sparkT) / 1000 : 0), u = px / 64; sparkT = now; debt += dt * 5;
      while (debt >= 1 && sparks.length < 40) {
        debt -= 1; const q = A.spots[(Math.random() * A.spots.length) | 0], a = Math.random() * 6.2832, sp = 1.5 + Math.random() * 2.5;
        sparks.push({ x: (q % n + 0.5) * 64 / n, y: ((q / n | 0) + 0.5) * 64 / n, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, age: 0, life: 1.8 + Math.random(), c: BANDS[3 + (Math.random() * 4 | 0)] });
      }
      ctx.save(); ctx.globalCompositeOperation = "lighter"; ctx.lineWidth = Math.max(1, u * 0.45);
      for (let i = sparks.length - 1; i >= 0; i--) {
        const e = sparks[i]; e.age += dt; if (e.age >= e.life) { sparks.splice(i, 1); continue; }
        const dr = 1 - 0.3 * dt; e.vx *= dr; e.vy *= dr; e.x += e.vx * dt; e.y += e.vy * dt;
        const l = e.age / e.life, z = (2 - l) * u * 0.9, x = e.x * u, y = e.y * u;
        ctx.strokeStyle = `rgba(${e.c[0]},${e.c[1]},${e.c[2]},${0.85 * (1 - l)})`;
        ctx.beginPath(); ctx.moveTo(x - z, y); ctx.lineTo(x + z, y); ctx.moveTo(x, y - z); ctx.lineTo(x, y + z); ctx.stroke();
      }
      ctx.restore();
    }
    function tick(now) {
      requestAnimationFrame(tick);
      if (now - last < 1000 / FPS || document.hidden || !canvas.isConnected) return;
      last = now; paint(now / 1000);
    }
    paint(performance.now() / 1000);
    if (!STILL) requestAnimationFrame(tick);
    return {
      setWorking(on) {
        if (on === working) return; working = on;
        if (on) { fin = null; t0 = performance.now(); growT = 6; } else fin = { t: performance.now(), g: grow() };
      },
      paintAt: paint
    };
  }
  root.BodycamLogo = { mount };
  let one = null;
  root.bcmiLogo = {
    async start(id, size) { const c = document.getElementById(id); if (c) one = await mount(c, document.baseURI, size); },
    working(on) { if (one) one.setWorking(on); }
  };
})(window);
