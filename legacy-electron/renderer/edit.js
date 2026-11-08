// Photo editor (Photos-style), loaded lazily on first use. Works on a downscaled preview; full resolution is only rendered at save time.
window.Editor = (() => {
  const G = window.G, { state, $, toast, toUrl } = G;
  const PRESETS = {
    Original: {}, Vivid: { saturate: [1, 1.45], contrast: [1, 1.1] }, 'Vivid Warm': { saturate: [1, 1.4], sepia: [0, 0.22] }, 'Vivid Cool': { saturate: [1, 1.4], 'hue-rotate': [0, 14] },
    Dramatic: { contrast: [1, 1.4], saturate: [1, 0.8], brightness: [1, 0.9] }, 'Dramatic Warm': { contrast: [1, 1.4], saturate: [1, 0.85], brightness: [1, 0.92], sepia: [0, 0.25] },
    'Dramatic Cool': { contrast: [1, 1.4], saturate: [1, 0.85], brightness: [1, 0.92], 'hue-rotate': [0, 14] }, Mono: { grayscale: [0, 1] }, Silvertone: { grayscale: [0, 1], contrast: [1, 1.1], brightness: [1, 1.06] },
    Noir: { grayscale: [0, 1], contrast: [1, 1.45], brightness: [1, 0.85] }, Fade: { contrast: [1, 0.85], brightness: [1, 1.1], saturate: [1, 0.8] }, Chrome: { contrast: [1, 1.2], saturate: [1, 1.3], brightness: [1, 1.05] },
    Instant: { sepia: [0, 0.25], contrast: [1, 0.9], brightness: [1, 1.1], saturate: [1, 1.3], 'hue-rotate': [0, -8] }
  };
  const ASPECT = { Freeform: 0, Original: -1, Square: 1, '16:9': 16 / 9, '4:3': 4 / 3, '3:2': 3 / 2, '5:4': 5 / 4 };
  const SECTIONS = [
    { k: 'light', t: 'Light', open: true, rows: [['ex', 'Exposure'], ['b', 'Brightness'], ['hl', 'Highlights'], ['sh', 'Shadows'], ['c', 'Contrast'], ['bp', 'Black Point']] },
    { k: 'color', t: 'Color', rows: [['sat', 'Saturation'], ['vib', 'Vibrance'], ['warm', 'Warmth'], ['tint', 'Tint']] },
    { k: 'bw', t: 'Black & White', rows: [['bw', 'Intensity', 1]] },
    { k: 'sharp', t: 'Sharpen', rows: [['sharp', 'Amount', 1]] },
    { k: 'vig', t: 'Vignette', rows: [['vig', 'Strength', 1]] }
  ];
  const PARAMS = {}; SECTIONS.forEach((sec) => sec.rows.forEach((r) => (PARAMS[r[0]] = sec.k)));
  const css = document.createElement('style');
  css.textContent = `
  #editor { --win:#1c1c1e; --text:#f5f5f7; --muted:#98989d; --sep:rgba(255,255,255,.1); --seg:rgba(255,255,255,.1); --segsel:#636366; --pop:#2c2c2e; --card:#2a2a2c; --hover:rgba(255,255,255,.08);
    position:fixed; inset:0; z-index:90; background:#161617; color:var(--text); display:flex; flex-direction:column; animation:fade .2s var(--ease); }
  #ed-top { height:52px; flex:none; display:grid; grid-template-columns:1fr auto 1fr; align-items:center; padding:0 150px 0 16px; -webkit-app-region:drag; background:rgba(28,28,30,.72); backdrop-filter:blur(24px); border-bottom:1px solid var(--sep); position:relative; z-index:3; }
  #ed-top > * { -webkit-app-region:no-drag; } #ed-top .r { display:flex; justify-content:flex-end; align-items:center; gap:6px; }
  .ed-text { border:0; background:none; color:var(--text); padding:5px 8px; border-radius:7px; font-size:13px; transition:background .15s; } .ed-text:hover { background:var(--hover); }
  .ed-link { color:#ffd60a; font-size:12px; margin-right:6px; } .ed-link[hidden] { display:none; }
  .ed-ic { width:30px; height:28px; border:0; background:none; color:var(--text); border-radius:7px; padding:4px 6px; transition:background .15s; } .ed-ic:hover, .ed-ic:active { background:var(--hover); }
  .ed-done { border:0; background:#ffd60a; color:#1c1c1e; font-weight:600; font-size:13px; padding:5px 16px; border-radius:7px; margin-left:4px; transition:filter .15s, opacity .15s; } .ed-done:hover { filter:brightness(1.08); } .ed-done:disabled { opacity:.55; }
  #ed-warn { position:absolute; left:50%; top:56px; transform:translateX(-50%); background:var(--pop); border:1px solid var(--sep); border-radius:10px; padding:8px 12px; display:flex; gap:10px; align-items:center; box-shadow:0 8px 28px rgba(0,0,0,.5); z-index:5; font-size:13px; }
  #ed-warn button { border:0; border-radius:7px; padding:4px 10px; background:var(--seg); color:var(--text); } #ed-warn button.d { background:#ff3b30; color:#fff; }
  #ed-body { flex:1; min-height:0; display:flex; }
  #ed-stage { flex:1; min-width:0; display:flex; align-items:center; justify-content:center; position:relative; overflow:hidden; padding:40px; }
  #ed-box { position:relative; line-height:0; } #ed-cv { display:block; box-shadow:0 8px 40px rgba(0,0,0,.5); }
  #ed-crop { position:absolute; box-shadow:0 0 0 9999px rgba(0,0,0,.55); border:1.5px solid #fff; cursor:move; touch-action:none;
    background-image:linear-gradient(rgba(255,255,255,.5),rgba(255,255,255,.5)),linear-gradient(rgba(255,255,255,.5),rgba(255,255,255,.5)),linear-gradient(90deg,rgba(255,255,255,.5),rgba(255,255,255,.5)),linear-gradient(90deg,rgba(255,255,255,.5),rgba(255,255,255,.5));
    background-size:100% 1px,100% 1px,1px 100%,1px 100%; background-position:0 33.33%,0 66.66%,33.33% 0,66.66% 0; background-repeat:no-repeat; }
  #ed-crop i { position:absolute; width:14px; height:14px; background:#fff; border-radius:50%; box-shadow:0 0 0 1px rgba(0,0,0,.4); touch-action:none; }
  #ed-crop i.nw { left:-7px; top:-7px; cursor:nwse-resize; } #ed-crop i.ne { right:-7px; top:-7px; cursor:nesw-resize; } #ed-crop i.sw { left:-7px; bottom:-7px; cursor:nesw-resize; } #ed-crop i.se { right:-7px; bottom:-7px; cursor:nwse-resize; }
  #ed-panel { width:260px; flex:none; background:#2a2a2c; border-left:1px solid var(--sep); padding:10px 12px 20px; overflow-y:auto; font-size:12.5px; }
  #ed-panel::-webkit-scrollbar { width:6px; } #ed-panel::-webkit-scrollbar-thumb { background:rgba(255,255,255,.18); border-radius:3px; }
  .sec { border-bottom:1px solid var(--sep); } .sec-h { display:flex; align-items:center; gap:6px; height:34px; cursor:pointer; font-weight:600; }
  .sec-h .chev { width:14px; height:14px; transition:transform .18s var(--ease); color:var(--muted); } .sec.open .chev { transform:rotate(90deg); }
  .sec-h .tgl { margin-left:auto; width:16px; height:16px; border-radius:50%; border:1.5px solid var(--muted); display:grid; place-items:center; transition:background .15s, border-color .15s; }
  .sec-h .tgl::after { content:''; width:6px; height:6px; border-radius:50%; background:#1c1c1e; opacity:0; } .sec.on .tgl { background:#fff; border-color:#fff; } .sec.on .tgl::after { opacity:1; }
  .sec-b { display:none; padding:0 2px 8px; } .sec.open .sec-b { display:block; } .sec:not(.on) .sec-b { opacity:.45; }
  .sl { padding:5px 0; } .sl .lr { display:flex; justify-content:space-between; color:var(--muted); font-size:12px; margin-bottom:2px; } .sl .lr em { font-style:normal; color:var(--text); font-variant-numeric:tabular-nums; }
  .sl .tw { position:relative; height:16px; } .sl .tw.bi::after { content:''; position:absolute; left:50%; top:3px; width:1px; height:10px; background:rgba(255,255,255,.4); pointer-events:none; }
  #editor input[type=range] { -webkit-appearance:none; appearance:none; width:100%; height:16px; margin:0; background:transparent; position:relative; z-index:1; }
  #editor input[type=range]::-webkit-slider-runnable-track { height:2px; border-radius:2px; background:var(--tr, rgba(255,255,255,.22)); }
  #editor input[type=range]::-webkit-slider-thumb { -webkit-appearance:none; width:12px; height:12px; border-radius:50%; background:#fff; margin-top:-5px; box-shadow:0 1px 3px rgba(0,0,0,.5); }
  #ed-fgrid { display:grid; grid-template-columns:repeat(3,1fr); gap:8px; padding-top:4px; }
  #ed-fgrid button { border:2px solid transparent; background:none; padding:2px; border-radius:9px; font-size:11px; color:var(--muted); transition:border-color .15s; } #ed-fgrid button.on { border-color:#ffd60a; color:var(--text); }
  #ed-fgrid canvas { width:100%; aspect-ratio:1; object-fit:cover; border-radius:6px; display:block; margin-bottom:3px; }
  .pills { display:flex; flex-wrap:wrap; gap:6px; margin:6px 0 10px; } .pill { border:0; background:var(--seg); color:var(--text); padding:5px 11px; border-radius:14px; font-size:12px; transition:background .15s; } .pill.on { background:#ffd60a; color:#1c1c1e; }
  .ph { margin:12px 0 4px; font-size:11px; font-weight:600; color:var(--muted); text-transform:uppercase; letter-spacing:.04em; } .ic-row { display:flex; gap:6px; margin:6px 0; } .ic-row .ed-ic { background:var(--seg); width:38px; height:30px; }`;
  document.head.append(css);
  let root, s, cv, ctx, cropEl, stage, onKey, onKeyUp, warmed = false;
  const NEUTRAL = () => ({ rot: 0, flipH: false, flipV: false, angle: 0, ex: 0, b: 0, hl: 0, sh: 0, c: 0, bp: 0, sat: 0, vib: 0, warm: 0, tint: 0, bw: 0, sharp: 0, vig: 0, filter: 'Original', inten: 100, ratio: 0, crop: null, aspKey: null, swapped: false });
  const eff = (k) => (s.en[PARAMS[k]] ? s[k] : 0);
  const dirty = () => { const n = NEUTRAL(); return Object.keys(n).some((k) => !['crop', 'ratio', 'aspKey', 'swapped', 'inten'].includes(k) && s[k] !== n[k]) || !!s.crop; };
  const ico = (n) => G.svg(n);
  const IC = { wand: '<path d="M5 19L15 9"/><path d="M14 4v3M12.5 5.5h3M19 9v2M18 10h2M18 15v2M17 16h2"/>', compare: '<rect x="4" y="5" width="7" height="14" rx="1.5"/><rect x="13" y="5" width="7" height="14" rx="1.5"/>', flip: '<path d="M12 4v16"/><path d="M9 7L4 17h5zM15 7l5 10h-5z"/>', flipv: '<path d="M4 12h16"/><path d="M7 9L17 4v5zM7 15l10 5v-5z"/>', chev: '<path d="M9.5 6l6 6-6 6"/>' };
  const raw = (n) => '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" width="18" height="18">' + IC[n] + '</svg>';

  function open(p) {
    if (root) return;
    s = { p, prev: null, tab: 'adjust', compare: false, raf: 0, en: { light: true, color: true, bw: true, sharp: true, vig: true }, ...NEUTRAL() };
    root = document.createElement('div'); root.id = 'editor';
    const secHtml = SECTIONS.map((sec) => `<div class="sec on${sec.open ? ' open' : ''}" data-s="${sec.k}"><div class="sec-h"><span class="chev">${raw('chev')}</span>${sec.t}<span class="tgl" title="Toggle ${sec.t}"></span></div><div class="sec-b">${sec.rows.map(([id, lab, one]) =>
      `<div class="sl"><div class="lr"><span>${lab}</span><em id="v-${id}">0</em></div><div class="tw${one ? '' : ' bi'}"><input type="range" id="r-${id}" min="${one ? 0 : -100}" max="100" value="0"></div></div>`).join('')}</div></div>`).join('');
    root.innerHTML = `<div id="ed-top"><div><button class="ed-text" id="ed-cancel">Cancel</button></div>
      <div class="seg" id="ed-tabs"><i class="seg-thumb"></i><button data-t="adjust" class="on">Adjust</button><button data-t="filters">Filters</button><button data-t="crop">Crop</button></div>
      <div class="r"><button class="ed-text ed-link" id="ed-revert" hidden>Revert to Original</button><button class="ed-ic" id="ed-compare" title="Hold to compare (C)">${raw('compare')}</button><button class="ed-ic" id="ed-auto" title="Auto-enhance">${raw('wand')}</button><button class="ed-done" id="ed-save" title="Save as a new copy (Ctrl+S)">Done</button></div></div>
    <div id="ed-body"><div id="ed-stage"><div id="ed-box"><canvas id="ed-cv"></canvas><div id="ed-crop" hidden><i class="nw"></i><i class="ne"></i><i class="sw"></i><i class="se"></i></div></div></div>
    <div id="ed-panel"><div id="t-adjust">${secHtml}</div>
      <div id="t-filters" hidden><div id="ed-fgrid"></div><div id="ed-inten" hidden><div class="sl"><div class="lr"><span>Intensity</span><em id="v-inten">100</em></div><div class="tw"><input type="range" id="r-inten" min="0" max="100" value="100"></div></div></div></div>
      <div id="t-crop" hidden><div class="ph">Aspect</div><div class="pills" id="ed-asp"><button class="pill" data-a="none">None</button>${Object.keys(ASPECT).map((k) => '<button class="pill" data-a="' + k + '">' + k + '</button>').join('')}<button class="pill" id="ed-swap" title="Portrait / landscape">⇄</button></div>
        <div class="ph">Orientation</div><div class="ic-row"><button class="ed-ic" data-rot="-1" title="Rotate left">${G.svg('rotL')}</button><button class="ed-ic" data-rot="1" title="Rotate right">${G.svg('rotR')}</button><button class="ed-ic" id="ed-fh" title="Flip horizontal">${raw('flip')}</button><button class="ed-ic" id="ed-fv" title="Flip vertical">${raw('flipv')}</button></div>
        <div class="ph">Straighten</div><div class="sl"><div class="lr"><span>Angle</span><em id="v-angle">0°</em></div><div class="tw bi"><input type="range" id="r-angle" min="-45" max="45" value="0"></div></div></div>
      <p style="font-size:11px;color:var(--muted);margin:16px 2px 0">Done saves a new copy next to the original (“name (edited)”). The original is never changed.</p></div></div>`;
    document.body.append(root);
    cv = $('#ed-cv'); ctx = cv.getContext('2d'); cropEl = $('#ed-crop'); stage = $('#ed-stage');
    $('#ed-cancel').onclick = tryClose; $('#ed-save').onclick = save; $('#ed-revert').onclick = revert; $('#ed-auto').onclick = auto;
    const cmp = $('#ed-compare'); cmp.onpointerdown = () => { s.compare = true; sched(); }; cmp.onpointerup = cmp.onpointerleave = () => { if (s.compare) { s.compare = false; sched(); } };
    root.querySelectorAll('#ed-tabs button').forEach((b) => (b.onclick = () => setTab(b.dataset.t)));
    root.querySelectorAll('.sec').forEach((el) => {
      el.querySelector('.sec-h').onclick = (e) => { if (e.target.closest('.tgl')) { s.en[el.dataset.s] = !s.en[el.dataset.s]; el.classList.toggle('on', s.en[el.dataset.s]); sched(); } else el.classList.toggle('open'); };
    });
    const bind = (id, k, one, fmt) => {
      const r = $('#r-' + id), v = $('#v-' + id), def = k === 'inten' ? 100 : 0;
      const upd = () => { const val = +r.value; v.textContent = fmt ? fmt(val) : (!one && val > 0 ? '+' : '') + val; const lo = +r.min, hi = +r.max, pc = ((val - lo) / (hi - lo)) * 100, c0 = one ? 0 : 50;
        r.style.setProperty('--tr', `linear-gradient(to right, rgba(255,255,255,.22) 0 ${Math.min(pc, c0)}%, rgba(255,255,255,.75) ${Math.min(pc, c0)}% ${Math.max(pc, c0)}%, rgba(255,255,255,.22) ${Math.max(pc, c0)}% 100%)`); };
      r.oninput = () => { s[k] = +r.value; upd(); sched(); }; r.ondblclick = () => { r.value = def; r.oninput(); }; upd(); r._upd = upd;
    };
    Object.keys(PARAMS).forEach((k) => bind(k, k, ['bw', 'sharp', 'vig'].includes(k))); bind('inten', 'inten', true, (v) => v + '%'); bind('angle', 'angle', false, (v) => v + '°');
    root.querySelectorAll('[data-rot]').forEach((b) => (b.onclick = () => { s.rot = ((s.rot + 90 * +b.dataset.rot) % 360 + 360) % 360; applyAspect(); }));
    $('#ed-fh').onclick = () => { s.flipH = !s.flipH; sched(); }; $('#ed-fv').onclick = () => { s.flipV = !s.flipV; sched(); };
    root.querySelectorAll('#ed-asp [data-a]').forEach((b) => (b.onclick = () => {
      root.querySelectorAll('#ed-asp [data-a]').forEach((x) => x.classList.toggle('on', x === b));
      if (b.dataset.a === 'none') { s.crop = null; s.ratio = 0; s.aspKey = null; s.eff = 0; sched(); } else { s.aspKey = b.dataset.a; s.ratio = ASPECT[b.dataset.a]; applyAspect(); }
    }));
    $('#ed-swap').onclick = () => { if (s.aspKey === 'Original') s.swapped = !s.swapped; else if (s.ratio > 0) s.ratio = 1 / s.ratio; applyAspect(); };
    wireCrop(); setTab('adjust');
    onKey = (e) => {
      e.stopPropagation(); const typing = e.target && e.target.tagName === 'INPUT' && e.target.type !== 'range';
      if (e.key === 'Escape') { e.preventDefault(); tryClose(); } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); save(); }
      else if (!typing && e.key.toLowerCase() === 'c' && !e.ctrlKey && !s.compare) { s.compare = true; sched(); }
    };
    onKeyUp = (e) => { if (e.key.toLowerCase() === 'c' && s && s.compare) { s.compare = false; sched(); } };
    window.addEventListener('keydown', onKey, true); window.addEventListener('keyup', onKeyUp, true); window.addEventListener('resize', layout);
    const img = new Image();
    img.onload = () => {
      const k = Math.min(1, 1600 / Math.max(img.naturalWidth, img.naturalHeight));
      s.prev = document.createElement('canvas'); s.prev.width = Math.round(img.naturalWidth * k); s.prev.height = Math.round(img.naturalHeight * k);
      s.prev.getContext('2d').drawImage(img, 0, 0, s.prev.width, s.prev.height); img.onload = img.onerror = null; img.removeAttribute('src'); draw();
    };
    img.onerror = () => { toast('Could not open this image for editing'); close(); };
    G.fullUrl(p.path).then((u) => { if (u) img.src = u; else img.onerror(); });
  }
  function setTab(t) {
    s.tab = t; const on = root.querySelector('#ed-tabs button[data-t="' + t + '"]');
    root.querySelectorAll('#ed-tabs button').forEach((b) => b.classList.toggle('on', b === on));
    const th = root.querySelector('#ed-tabs .seg-thumb'); th.style.width = on.offsetWidth + 'px'; th.style.transform = 'translateX(' + on.offsetLeft + 'px)';
    ['adjust', 'filters', 'crop'].forEach((x) => ($('#t-' + x).hidden = x !== t)); if (t === 'filters' && !warmed && s.prev) buildFilterThumbs(); showCrop();
  }
  function revert() {
    Object.assign(s, NEUTRAL()); Object.keys(s.en).forEach((k) => { s.en[k] = true; }); root.querySelectorAll('.sec').forEach((el) => el.classList.add('on'));
    root.querySelectorAll('#ed-panel input[type=range]').forEach((r) => { r.value = r.id === 'r-inten' ? 100 : 0; r._upd(); });
    root.querySelectorAll('#ed-asp .pill, #ed-fgrid button').forEach((x) => x.classList.remove('on')); const o = $('#ed-fgrid [data-f="Original"]'); if (o) o.classList.add('on'); $('#ed-inten').hidden = true; draw();
  }
  function auto() {
    const c = document.createElement('canvas'); c.width = c.height = 128; const x = c.getContext('2d', { willReadFrequently: true }); x.drawImage(s.prev, 0, 0, 128, 128);
    const d = x.getImageData(0, 0, 128, 128).data, h = new Array(256).fill(0); let sum = 0;
    for (let i = 0; i < d.length; i += 4) { const l = Math.round(0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2]); h[l]++; sum += l; }
    const n = d.length / 4, mean = sum / n; let a = 0, lo = 0, hi = 255; for (let i = 0; i < 256; i++) { a += h[i]; if (a > n * 0.01) { lo = i; break; } } a = 0; for (let i = 255; i >= 0; i--) { a += h[i]; if (a > n * 0.01) { hi = i; break; } }
    const set = (k, v) => { s[k] = Math.round(Math.max(-100, Math.min(100, v))); const r = $('#r-' + k); r.value = s[k]; r._upd(); s.en[PARAMS[k]] = true; root.querySelector('.sec[data-s="' + PARAMS[k] + '"]').classList.add('on'); };
    set('c', Math.min(40, Math.max(0, (255 / Math.max(60, hi - lo) - 1) * 70))); set('b', ((118 - mean) / 118) * 40); set('sat', 12); set('bp', lo > 8 ? Math.min(25, lo / 3) : 0); c.width = 0; sched();
  }
  const dims = () => (s.rot % 180 ? [s.prev.height, s.prev.width] : [s.prev.width, s.prev.height]);
  function chain() {
    const f = [`brightness(${(1 + eff('b') / 200) * Math.pow(2, eff('ex') / 100)})`, `contrast(${1 + eff('c') / 200})`, `saturate(${1 + eff('sat') / 100})`];
    if (eff('bw')) f.push(`grayscale(${eff('bw') / 100})`);
    const t = s.inten / 100, pr = PRESETS[s.filter] || {};
    Object.keys(pr).forEach((fn) => { const v = pr[fn][0] + (pr[fn][1] - pr[fn][0]) * t; f.push(fn + '(' + (fn === 'hue-rotate' ? v + 'deg' : v) + ')'); }); return f.join(' ');
  }
  function pixelPass(c2, w, h) {
    const hl = eff('hl'), sh = eff('sh'), bp = eff('bp'), vib = eff('vib'), tint = eff('tint'), sharp = eff('sharp');
    if (!(hl || sh || bp || vib || tint || sharp)) return;
    const im = c2.getImageData(0, 0, w, h), d = im.data; let lut = null;
    if (hl || sh || bp) {
      lut = new Uint8ClampedArray(256);
      for (let i = 0; i < 256; i++) { let x = i / 255; const b0 = (bp / 100) * 0.25; if (b0) x = Math.max(0, (x - b0) / (1 - b0)); x += (sh / 100) * 0.35 * 4 * x * (1 - x) * (1 - x); x += (hl / 100) * 0.35 * 4 * x * x * (1 - x); lut[i] = Math.round(x * 255); }
    }
    let bl = null;
    if (sharp) { const t = document.createElement('canvas'); t.width = w; t.height = h; const tx = t.getContext('2d'); tx.filter = `blur(${Math.max(1, w / 1600)}px)`; tx.drawImage(c2.canvas, 0, 0); bl = tx.getImageData(0, 0, w, h).data; t.width = 0; }
    const sa = (sharp / 100) * 1.2, va = vib / 100, ti = (tint / 100) * 14;
    for (let i = 0; i < d.length; i += 4) {
      let r = d[i], g = d[i + 1], b = d[i + 2];
      if (lut) { r = lut[r]; g = lut[g]; b = lut[b]; }
      if (va) { const mx = Math.max(r, g, b), mn = Math.min(r, g, b), sat = mx ? (mx - mn) / mx : 0, am = va * (1 - sat), gr = (r + g + b) / 3; r += (r - gr) * am; g += (g - gr) * am; b += (b - gr) * am; }
      if (ti) { g -= ti; r += ti * 0.5; b += ti * 0.5; }
      if (bl) { r += sa * (r - bl[i]); g += sa * (g - bl[i + 1]); b += sa * (b - bl[i + 2]); }
      d[i] = r; d[i + 1] = g; d[i + 2] = b;
    }
    c2.putImageData(im, 0, 0);
  }
  function paint(c2, src, srcW, srcH, w, h, compare) {
    c2.save(); c2.clearRect(0, 0, w, h); c2.filter = compare ? 'none' : chain();
    c2.translate(w / 2, h / 2); const th = (s.angle * Math.PI) / 180; c2.rotate((s.rot * Math.PI) / 180 + (compare ? 0 : th));
    if (!compare && s.angle) { const k = Math.cos(Math.abs(th)) + Math.sin(Math.abs(th)) * Math.max(w / h, h / w); c2.scale(k, k); }
    if (!compare) c2.scale(s.flipH ? -1 : 1, s.flipV ? -1 : 1);
    c2.drawImage(src, -srcW / 2, -srcH / 2, srcW, srcH); c2.restore(); if (compare) return;
    c2.save(); c2.filter = 'none'; const wm = eff('warm'), vg = eff('vig');
    if (wm) { c2.globalCompositeOperation = 'soft-light'; c2.fillStyle = wm > 0 ? `rgba(255,140,0,${wm / 200})` : `rgba(0,110,255,${-wm / 200})`; c2.fillRect(0, 0, w, h); c2.globalCompositeOperation = 'source-over'; }
    if (vg) { const g = c2.createRadialGradient(w / 2, h / 2, Math.min(w, h) * 0.3, w / 2, h / 2, Math.hypot(w, h) / 2); g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, `rgba(0,0,0,${(vg / 100) * 0.7})`); c2.fillStyle = g; c2.fillRect(0, 0, w, h); }
    c2.restore(); pixelPass(c2, w, h);
  }
  const sched = () => { if (!s) return; if (!s.raf) s.raf = requestAnimationFrame(() => { if (s) draw(); }); };
  function draw() { if (!s || !s.prev) return; s.raf = 0; const [w, h] = dims(); if (cv.width !== w || cv.height !== h) { cv.width = w; cv.height = h; } paint(ctx, s.prev, s.prev.width, s.prev.height, w, h, s.compare); $('#ed-revert').hidden = !dirty(); layout(); }
  function layout() {
    if (!s || !s.prev) return; const [w, h] = dims(), k = Math.min((stage.clientWidth - 80) / w, (stage.clientHeight - 80) / h);
    cv.style.width = Math.round(w * k) + 'px'; cv.style.height = Math.round(h * k) + 'px'; showCrop();
  }
  function applyAspect() {
    const [w, h] = dims(); let r = s.ratio; if (s.aspKey === 'Original') r = s.swapped ? h / w : w / h; s.eff = r; if (s.aspKey == null) { draw(); return; }
    const ia = w / h; let cw, ch; if (!r) { cw = 0.8; ch = 0.8; } else if (r >= ia) { cw = 0.9; ch = (0.9 * ia) / r; } else { ch = 0.9; cw = (0.9 * r) / ia; }
    s.crop = { x: (1 - cw) / 2, y: (1 - ch) / 2, w: cw, h: ch }; draw();
  }
  function showCrop() { if (!s.crop || s.tab !== 'crop') { cropEl.hidden = true; return; } cropEl.hidden = false; const c = s.crop; Object.assign(cropEl.style, { left: c.x * 100 + '%', top: c.y * 100 + '%', width: c.w * 100 + '%', height: c.h * 100 + '%' }); }
  function wireCrop() {
    const drag = (e, mode) => {
      e.preventDefault(); e.stopPropagation(); const el = e.currentTarget; el.setPointerCapture(e.pointerId);
      const r0 = cv.getBoundingClientRect(), c0 = { ...s.crop }, sx = e.clientX, sy = e.clientY, [rw, rh] = dims();
      const mv = (ev) => {
        const dx = (ev.clientX - sx) / r0.width, dy = (ev.clientY - sy) / r0.height; let c = { ...c0 };
        if (mode === 'move') { c.x = Math.min(1 - c.w, Math.max(0, c0.x + dx)); c.y = Math.min(1 - c.h, Math.max(0, c0.y + dy)); }
        else {
          const west = mode.includes('w'), north = mode.includes('n');
          let x1 = west ? c0.x + dx : c0.x, x2 = west ? c0.x + c0.w : c0.x + c0.w + dx, y1 = north ? c0.y + dy : c0.y, y2 = north ? c0.y + c0.h : c0.y + c0.h + dy;
          x1 = Math.max(0, Math.min(x1, 1)); x2 = Math.max(0, Math.min(x2, 1)); y1 = Math.max(0, Math.min(y1, 1)); y2 = Math.max(0, Math.min(y2, 1));
          let w = Math.max(0.05, Math.abs(x2 - x1)), h = Math.max(0.05, Math.abs(y2 - y1)); const r = s.eff || 0;
          if (r) { h = (w * rw) / (rh * r); if (h > 1) { h = 1; w = (h * rh * r) / rw; } }
          const ax = west ? c0.x + c0.w : c0.x, ay = north ? c0.y + c0.h : c0.y; c = { x: west ? ax - w : ax, y: north ? ay - h : ay, w, h };
          if (c.x < 0) c.x = 0; if (c.y < 0) c.y = 0; if (c.x + c.w > 1) c.x = 1 - c.w; if (c.y + c.h > 1) c.y = 1 - c.h;
        }
        s.crop = c; showCrop(); $('#ed-revert').hidden = false;
      };
      const up = () => { el.removeEventListener('pointermove', mv); el.removeEventListener('pointerup', up); };
      el.addEventListener('pointermove', mv); el.addEventListener('pointerup', up);
    };
    cropEl.onpointerdown = (e) => drag(e, 'move'); cropEl.querySelectorAll('i').forEach((h) => (h.onpointerdown = (e) => drag(e, h.className)));
  }
  function buildFilterThumbs() {
    warmed = true; const g = $('#ed-fgrid'), tiny = document.createElement('canvas'), k = 64 / Math.max(s.prev.width, s.prev.height);
    tiny.width = Math.round(s.prev.width * k); tiny.height = Math.round(s.prev.height * k); tiny.getContext('2d').drawImage(s.prev, 0, 0, tiny.width, tiny.height);
    Object.keys(PRESETS).forEach((name) => {
      const b = document.createElement('button'); b.dataset.f = name; if (name === s.filter) b.classList.add('on');
      const c = document.createElement('canvas'); c.width = tiny.width; c.height = tiny.height; const x = c.getContext('2d'), pr = PRESETS[name];
      x.filter = Object.keys(pr).map((fn) => fn + '(' + (fn === 'hue-rotate' ? pr[fn][1] + 'deg' : pr[fn][1]) + ')').join(' ') || 'none'; x.drawImage(tiny, 0, 0);
      b.append(c, Object.assign(document.createElement('div'), { textContent: name }));
      b.onclick = () => { s.filter = name; g.querySelectorAll('button').forEach((y) => y.classList.toggle('on', y === b)); $('#ed-inten').hidden = name === 'Original'; sched(); }; g.append(b);
    }); tiny.width = 0;
  }
  async function save() {
    const btn = $('#ed-save'); if (btn.disabled) return; if (!dirty()) { toast('No changes to save'); return; }
    btn.textContent = 'Saving…'; btn.disabled = true;
    try {
      const png = /\.png$/i.test(s.p.path), ext = png ? '.png' : '.jpg', full = new Image(); full.src = await G.fullUrl(s.p.path); await full.decode();
      const sw = s.rot % 180, w = sw ? full.naturalHeight : full.naturalWidth, h = sw ? full.naturalWidth : full.naturalHeight;
      let out = document.createElement('canvas'); out.width = w; out.height = h; paint(out.getContext('2d', { willReadFrequently: true }), full, full.naturalWidth, full.naturalHeight, w, h, false);
      if (s.crop) {
        const c = s.crop, cw = Math.max(1, Math.round(c.w * w)), ch = Math.max(1, Math.round(c.h * h)), c2 = document.createElement('canvas'); c2.width = cw; c2.height = ch;
        c2.getContext('2d').drawImage(out, Math.round(c.x * w), Math.round(c.y * h), cw, ch, 0, 0, cw, ch); out.width = out.height = 0; out = c2;
      }
      const data = out.toDataURL(png ? 'image/png' : 'image/jpeg', 0.92); out.width = out.height = 0; full.removeAttribute('src');
      const r = await window.api.saveEdited(s.p.path, data, ext);
      if (!r) { toast("Couldn't save the edited photo"); btn.textContent = 'Done'; btn.disabled = false; return; }
      state.photos.push({ path: r.path, name: r.name, mtime: r.mtime, size: r.size }); close(); G.render();
      const i = state.list.findIndex((x) => x.path === r.path); if (i >= 0) { state.idx = i; stripHi = -1; G.showLb(); }
      toast('Saved as ' + r.name);
    } catch (e) { toast("Couldn't save the edited photo"); btn.textContent = 'Done'; btn.disabled = false; }
  }
  function tryClose() {
    if (!dirty()) { close(); return; } if ($('#ed-warn')) return;
    const w = document.createElement('div'); w.id = 'ed-warn'; w.innerHTML = 'Discard changes? <button id="w-k">Keep editing</button><button class="d" id="w-d">Discard</button>';
    $('#ed-top').append(w); $('#w-k').onclick = () => w.remove(); $('#w-d').onclick = close;
  }
  function close() {
    if (!root) return; window.removeEventListener('keydown', onKey, true); window.removeEventListener('keyup', onKeyUp, true); window.removeEventListener('resize', layout);
    if (s && s.prev) s.prev.width = s.prev.height = 0; if (s && s.raf) cancelAnimationFrame(s.raf); root.querySelectorAll('canvas').forEach((c) => { c.width = c.height = 0; }); root.innerHTML = ''; root.remove(); root = s = cv = ctx = cropEl = stage = null; warmed = false;
    const free = () => { try { window.api && window.api.trim && window.api.trim(); window.gc && window.gc(); } catch {} }; setTimeout(free, 800); setTimeout(free, 3500); setTimeout(free, 9000);
  }
  return { open, isOpen: () => !!root };
})();