const $ = (s) => document.querySelector(s);
const state = { folders: [], photos: [], favs: new Set(), hidden: new Set(), rot: {}, albums: {}, trashed: [], view: 'lib', mode: 'all',
  drill: { y: null, m: null }, theme: 'system', list: [], idx: -1, zoom: 220, sel: new Set(), anchor: null };
const MONTHS = ['January','February','March','April','May','June','July','August','September','October','November','December'];

const I = {
  photos: '<rect x="3.5" y="4.5" width="17" height="15" rx="3"/><circle cx="9" cy="10" r="1.5"/><path d="M4.5 17.5l4.5-4.5 3.5 3.5 2.5-2.5 4.5 4"/>',
  heart: '<path d="M12 19.5s-7-4.3-7-9.7A3.9 3.9 0 0 1 12 7.4a3.9 3.9 0 0 1 7 2.4c0 5.4-7 9.7-7 9.7z"/>',
  clock: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>',
  folder: '<path d="M3.5 7.5a2 2 0 0 1 2-2H10l2 2.5h6.5a2 2 0 0 1 2 2V17a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z"/>',
  plus: '<path d="M12 5v14M5 12h14"/>', minus: '<path d="M5 12h14"/>',
  sidebar: '<rect x="3.5" y="5" width="17" height="14" rx="2.5"/><path d="M9.5 5v14"/>',
  chevL: '<path d="M14.5 5.5L8 12l6.5 6.5"/>', chevR: '<path d="M9.5 5.5L16 12l-6.5 6.5"/>',
  search: '<circle cx="11" cy="11" r="6"/><path d="M20 20l-4.2-4.2"/>',
  trash: '<path d="M4.5 7h15M10 7V4.5h4V7M6.5 7l.8 12a1.5 1.5 0 0 0 1.5 1.4h6.4a1.5 1.5 0 0 0 1.5-1.4L17.5 7M10 11v6M14 11v6"/>',
  info: '<circle cx="12" cy="12" r="8.5"/><path d="M12 11v5.5M12 7.8v.2"/>',
  gear: '<circle cx="12" cy="12" r="3"/><path d="M12 3.5v2.2M12 18.3v2.2M3.5 12h2.2M18.3 12h2.2M6 6l1.6 1.6M16.4 16.4L18 18M6 18l1.6-1.6M16.4 7.6L18 6"/>',
  shot: '<rect x="4" y="5" width="16" height="14" rx="2"/><path d="M8 5v2M16 5v2M4 9h16"/>',
  anim: '<circle cx="12" cy="12" r="8.5"/><path d="M10 9l5 3-5 3z"/>',
  edit: '<path d="M4.5 19.5l1-4L16.5 4.5l3 3L8.5 18.5z"/><path d="M14.5 6.5l3 3"/>',
  rotL: '<path d="M5 12a7 7 0 1 0 2.2-5.1"/><path d="M5 4v4h4"/>', rotR: '<path d="M19 12a7 7 0 1 1-2.2-5.1"/><path d="M19 4v4h-4"/>', play: '<path d="M8.5 6.5l9 5.5-9 5.5z"/>',
  eyeoff: '<path d="M3.5 12s3-5.5 8.5-5.5S20.5 12 20.5 12s-3 5.5-8.5 5.5S3.5 12 3.5 12z"/><path d="M4 4l16 16"/>',
  album: '<rect x="4" y="6" width="16" height="13" rx="2.5"/><path d="M7 4h10"/>'
};
const svg = (n) => '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">' + I[n] + '</svg>';
const hydrate = (root) => (root || document).querySelectorAll('[data-ico]').forEach((el) => { if (!el.firstChild) el.innerHTML = svg(el.dataset.ico); });

const fmtSize = (b) => b > 1048576 ? (b / 1048576).toFixed(1) + ' MB' : Math.round(b / 1024) + ' KB';
const CONVX = /\.(heic|heif|tiff?)$/i;
const fullUrl = async (p) => { if (!CONVX.test(p)) return toUrl(p); const u = await api.full(p); if (!u) toast(/\.hei[cf]$/i.test(p) ? 'Cannot open HEIC: install "HEIF Image Extensions" from the Microsoft Store' : 'Cannot open this file'); return u; };
const toUrl = (p) => 'file:///' + encodeURI(p.replace(/\\/g, '/')).replace(/#/g, '%23').replace(/\?/g, '%3F');
const base = (p) => p.split(/[\\/]/).filter(Boolean).pop() || p;
const dirOf = (p) => p.replace(/[\\/][^\\/]*$/, '');
const dFull = new Intl.DateTimeFormat('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
const dTime = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit' });
const isShot = (p) => /screen ?shot|capture/i.test(p.name) || /[\\/]screenshots?[\\/]/i.test(p.path);
const isAnim = (p) => /\.gif$/i.test(p.name);
const slim = (r) => r.map((p) => ({ path: p.path, name: p.name, mtime: p.mtime, size: p.size }));

async function init() {
  hydrate();
  const s = await api.getSettings();
  state.folders = s.folders || [];
  state.favs = new Set(s.favs || []); state.hidden = new Set(s.hidden || []); state.rot = s.rot || {};
  state.albums = s.albums || {}; state.trashed = s.trashed || [];
  state.theme = s.theme || 'system'; state.zoom = s.zoom || 220; state.mode = s.mode || 'all';
  if (!state.folders.length) state.folders = [await api.defaultFolder()];
  $('#zoom').value = state.zoom; applyZoomCss(); applyTheme(state.theme); syncSeg();
  renderSide();
  await rescan();
  api.onOpenFile(openExternal);
  const f = await api.initialFile();
  if (f) await openExternal(f);
}
function persist() { api.setSettings({ folders: state.folders, favs: [...state.favs], hidden: [...state.hidden], rot: state.rot, albums: state.albums, trashed: state.trashed, theme: state.theme, zoom: state.zoom, mode: state.mode }); }
let colRaf = 0;
function layoutCols() {
  if (colRaf) return;
  colRaf = requestAnimationFrame(() => {
    colRaf = 0; const sc = $('#scroller'), g = $('#grid'), w = sc.clientWidth; if (w <= 0) return;
    const cols = Math.max(1, Math.round(w / state.zoom)); g.style.setProperty('--cols', cols);
    if (effMode() !== 'all') { VG.cols = cols; return; }
    // keep the same photos in view when tile size / column count changes (resize, maximize, zoom)
    const old = VG.rowH ? (sc.scrollTop / VG.rowH) * VG.cols : -1, rowH = (w - (cols - 1) * VG.gap) / cols + VG.gap;
    if (old >= 0 && Math.abs(rowH - VG.rowH) > 0.01) { g.style.paddingTop = '0px'; g.style.paddingBottom = Math.ceil(state.list.length / cols) * rowH + 'px'; sc.scrollTop = (old / cols) * rowH; }
    vgUpdate();
  });
}
/* ---------- virtual grid (All Photos): only the rows near the viewport are in the DOM ---------- */
const VG = { cols: 0, rowH: 0, map: new Map(), gap: 2, buf: 2 };
function vgReset() { VG.map.forEach((t) => io.unobserve(t)); VG.map.clear(); }
function vgUpdate(rebuild) {
  if (effMode() !== 'all') return;
  const sc = $('#scroller'), g = $('#grid'), list = state.list, n = list.length, w = sc.clientWidth;
  if (w <= 0) return;
  const cols = Math.max(1, Math.round(w / state.zoom)); VG.cols = cols; g.style.setProperty('--cols', cols);
  if (rebuild) vgReset();
  const rowH = (w - (cols - 1) * VG.gap) / cols + VG.gap, rows = Math.ceil(n / cols); VG.rowH = rowH;
  const r0 = Math.max(0, Math.floor(sc.scrollTop / rowH) - VG.buf), r1 = Math.min(rows - 1, Math.floor((sc.scrollTop + sc.clientHeight) / rowH) + VG.buf);
  const a = r0 * cols, b = Math.min(n - 1, (r1 + 1) * cols - 1);
  g.style.paddingTop = r0 * rowH + 'px'; g.style.paddingBottom = Math.max(0, rows - 1 - r1) * rowH + 'px';
  for (const [i, t] of VG.map) if (i < a || i > b || t._p !== list[i]) { io.unobserve(t); t.remove(); VG.map.delete(i); }
  let next = null;
  for (let i = b; i >= a; i--) { let t = VG.map.get(i); if (!t) { t = makeTile(list[i], true); VG.map.set(i, t); g.insertBefore(t, next); } next = t; }
}
function applyZoomCss() { layoutCols(); const r = document.documentElement.style; r.setProperty('--size', state.zoom + 'px'); r.setProperty('--row', Math.round(state.zoom * 0.7) + 'px'); }
function applyTheme(t) {
  state.theme = t;
  const r = document.documentElement;
  if (t === 'dark' || t === 'light') r.dataset.t = t; else delete r.dataset.t;
  document.querySelectorAll('#theme button').forEach((b) => b.classList.toggle('on', b.dataset.theme === t));
  api.setSettings({ theme: t });
}

async function rescan() {
  const seen = new Set(); state.photos = [];
  await Promise.all(state.folders.map(async (f) => {
    for (const p of slim(await api.scan(f))) if (!seen.has(p.path)) { seen.add(p.path); state.photos.push(p); }
    render();
  }));
  render(); renderSide();
}

async function openExternal(path) {
  const dir = dirOf(path);
  const photos = slim(await api.scan(dir, false)).sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
  if (!photos.length) return;
  state.list = photos; externalMode = true;
  openLb(Math.max(0, photos.findIndex((p) => p.path.toLowerCase() === path.toLowerCase())), document.body);
}

/* ---------- sidebar ---------- */
function row(view, ico, label, extra) {
  const b = document.createElement('button'); b.className = 'row' + (state.view === view ? ' active' : ''); b.dataset.view = view;
  b.innerHTML = (extra || '<span class="ic">' + svg(ico) + '</span>') + '<span class="lbl"></span>'; b.querySelector('.lbl').textContent = label;
  b.onclick = () => setView(view);
  return b;
}
function renderSide() {
  const nav = $('#nav'); nav.innerHTML = '';
  nav.append(row('lib', 'photos', 'Library'), row('fav', 'heart', 'Favorites'), row('recent', 'clock', 'Recents'));
  const media = [row('shots', 'shot', 'Screenshots'), row('anim', 'anim', 'Animated')];
  let box = $('#media'); if (!box) { box = document.createElement('div'); box.id = 'media'; $('.side-scroll').insertBefore(box, $('#nav').nextSibling); }
  box.innerHTML = '<div class="side-h">Media Types</div>'; box.append(...media);
  const fo = $('#folders'); fo.innerHTML = '';
  state.folders.forEach((f) => {
    const b = row('f:' + f, null, base(f), '<span class="alb"></span>'); b.title = f;
    const cover = state.photos.find((p) => p.path.startsWith(f));
    if (cover) api.thumb(cover.path, cover.mtime).then((u) => { const im = new Image(); im.src = u; b.querySelector('.alb').append(im); });
    b.oncontextmenu = (e) => { e.preventDefault(); if (confirm('Remove "' + base(f) + '" from Library?')) { state.folders = state.folders.filter((x) => x !== f); if (state.view === 'f:' + f) state.view = 'lib'; persist(); rescan(); } };
    fo.append(b);
  });
  Object.keys(state.albums).forEach((a) => {
    const b = row('a:' + a, 'album', a); b.querySelector('.lbl').after(Object.assign(document.createElement('span'), { className: 'cnt', textContent: state.albums[a].length }));
    b.oncontextmenu = (e) => { e.preventDefault(); ext().then((x) => x.albumMenu(e, a)); };
    fo.append(b);
  });
  let ut = $('#utils'); if (!ut) { ut = document.createElement('div'); ut.id = 'utils'; $('.side-scroll').append(ut); }
  ut.innerHTML = '<div class="side-h">Utilities</div>'; ut.append(row('hidden', 'eyeoff', 'Hidden'), row('deleted', 'trash', 'Recently Deleted'));
}
function setView(v) { state.view = v; state.drill = { y: null, m: null }; state.sel.clear(); renderSide(); render(); $('#scroller').scrollTop = 0; }

/* ---------- data ---------- */
function filtered() {
  const v = state.view; let l;
  if (v === 'hidden') l = state.photos.filter((p) => state.hidden.has(p.path));
  else {
    l = state.photos.filter((p) => !state.hidden.has(p.path));
    if (v === 'fav') l = l.filter((p) => state.favs.has(p.path));
    else if (v === 'shots') l = l.filter(isShot);
    else if (v === 'anim') l = l.filter(isAnim);
    else if (v.startsWith('f:')) { const f = v.slice(2); l = l.filter((p) => p.path.startsWith(f)); }
    else if (v.startsWith('a:')) { const s = new Set(state.albums[v.slice(2)] || []); l = l.filter((p) => s.has(p.path)); }
  }
  const q = $('#search').value.trim().toLowerCase();
  if (q) l = l.filter((p) => matches(p, q));
  l = l.slice().sort((a, b) => b.mtime - a.mtime);
  if (v === 'recent') l = l.slice(0, 100);
  const d = state.drill;
  if (d.y != null) l = l.filter((p) => { const x = new Date(p.mtime); return x.getFullYear() === d.y && (d.m == null || x.getMonth() === d.m); });
  return l;
}
function matches(p, q) {
  if (p.name.toLowerCase().includes(q) || p.path.toLowerCase().includes(q)) return true;
  const d = new Date(p.mtime);
  if (String(d.getFullYear()) === q || MONTHS[d.getMonth()].toLowerCase().startsWith(q)) return true;
  if (q.startsWith('screenshot')) return isShot(p); if (q === 'gif' || q === 'animated') return isAnim(p);
  if (q === 'favorite' || q === 'favourite') return state.favs.has(p.path);
  return Object.keys(state.albums).some((a) => a.toLowerCase().includes(q) && state.albums[a].includes(p.path));
}
const groupBy = (l, f) => { const m = new Map(); l.forEach((p) => { const k = f(new Date(p.mtime)); (m.get(k) || m.set(k, []).get(k)).push(p); }); return [...m.entries()]; };
const effMode = () => (state.view === 'recent' ? 'all' : state.mode);
const TITLES = { lib: 'Library', fav: 'Favorites', recent: 'Recents', shots: 'Screenshots', anim: 'Animated', hidden: 'Hidden', deleted: 'Recently Deleted' };

/* ---------- tiles (thumbs unload off-screen) ---------- */
const DAY_CHUNK = 72;
const thumbSz = () => (state.zoom <= 260 ? 256 : 360);
const io = new IntersectionObserver((es) => es.forEach((e) => {
  const t = e.target, im = t.firstChild;
  if (e.isIntersecting) {
    t.classList.add('in'); t._near = true;
    if (!im.getAttribute('src') && !t._busy) { t._busy = true; setTimeout(() => { if (!t._near || !t.isConnected) { t._busy = false; return; } api.thumb(t._p.path, t._p.mtime, thumbSz()).then((u) => { t._busy = false; if (t._near && t.isConnected && u) im.src = u; }); }, 90); }
  } else { t._near = false; if (im.getAttribute('src')) im.removeAttribute('src'); }
}), { rootMargin: '300px' });
const gio = new IntersectionObserver((es) => es.forEach((e) => { const s = e.target; if (e.isIntersecting) { if (!s._built) s._build(); } else if (s._built) s._drop(); }), { rootMargin: '1500px 0px' });
let hoverPath = null;

function makeTile(p, full) {
  const t = document.createElement('div'); t.className = 't' + (state.favs.has(p.path) ? ' isfav' : '') + (state.sel.has(p.path) ? ' sel' : '');
  t._p = p; t.dataset.path = p.path;
  const im = document.createElement('img'); im.decoding = 'async'; im.alt = ''; t.append(im);
  if (full) {
    const b = document.createElement('span'); b.className = 'badge'; b.innerHTML = svg('heart'); t.append(b);
    const f = document.createElement('button'); f.className = 'act fav'; f.innerHTML = svg('heart'); f.onclick = (e) => { e.stopPropagation(); toggleFav(p); };
    const d = document.createElement('button'); d.className = 'act del'; d.innerHTML = svg('trash'); d.onclick = (e) => { e.stopPropagation(); deletePhotos([p]); };
    t.append(f, d);
    t.onclick = (e) => tileClick(e, p);
    t.oncontextmenu = (e) => { e.preventDefault(); if (!state.sel.has(p.path)) { state.sel = new Set([p.path]); refreshSel(); } ext().then((x) => x.menu(e)); };
    t.onmouseenter = () => { hoverPath = p.path; }; t.onmouseleave = () => { if (hoverPath === p.path) hoverPath = null; };
  }
  io.observe(t);
  return t;
}
function tileClick(e, p) {
  if (e.ctrlKey || e.metaKey) { state.sel.has(p.path) ? state.sel.delete(p.path) : state.sel.add(p.path); state.anchor = p.path; }
  else if (e.shiftKey && state.anchor) {
    const a = state.list.findIndex((x) => x.path === state.anchor), b = state.list.indexOf(p);
    state.sel = new Set(state.list.slice(Math.min(a, b), Math.max(a, b) + 1).map((x) => x.path));
  } else { state.sel = new Set([p.path]); state.anchor = p.path; refreshSel(); openLb(state.list.indexOf(p)); return; }
  refreshSel();
}
function refreshSel() {
  document.querySelectorAll('#grid .t').forEach((t) => t.classList.toggle('sel', state.sel.has(t.dataset.path)));
  const n = state.sel.size; $('#title').textContent = n > 1 ? n + ' Selected' : titleText();
}
const tileFor = (path) => [...document.querySelectorAll('#grid .t')].filter((x) => x.dataset.path === path);

function titleText() {
  const d = state.drill;
  if (d.y != null) return d.m != null ? MONTHS[d.m] + ' ' + d.y : String(d.y);
  const v = state.view; if (TITLES[v]) return TITLES[v];
  if (v.startsWith('f:')) return base(v.slice(2)); if (v.startsWith('a:')) return v.slice(2); return 'Library';
}

/* ---------- views ---------- */
function render() {
  if (state.view === 'deleted') { ext().then((x) => x.renderDeleted()); return; }
  const list = state.list = filtered(), mode = effMode();
  io.disconnect(); gio.disconnect();
  const g = $('#grid'); g.innerHTML = ''; g.className = 'mode-' + mode; g.style.paddingTop = g.style.paddingBottom = ''; VG.map.clear();
  $('#title').textContent = titleText(); $('#back').hidden = state.drill.y == null;
  document.querySelectorAll('#modes button').forEach((b) => b.classList.toggle('on', b.dataset.mode === state.mode)); syncSeg();
  $('#empty').hidden = list.length > 0;
  const frag = document.createDocumentFragment();
  if (mode === 'all') { g.append(frag); VG.rowH = 0; vgUpdate(true); return; }
  else if (mode === 'days') groupBy(list, (d) => d.getFullYear() + '-' + d.getMonth() + '-' + d.getDate()).forEach(([, items]) => {
    const sec = document.createElement('section'), h = document.createElement('div');
    h.className = 'grp-h'; h.innerHTML = dFull.format(new Date(items[0].mtime)) + '<small>' + items.length + '</small>'; sec.append(h);
    const per = Math.max(1, Math.floor(($('#scroller').clientWidth - 32) / (state.zoom * 0.7 + 4)));
    // a day with thousands of photos is cut into 72-photo chunks so only the chunks near the viewport have DOM tiles
    for (let c = 0; c < items.length; c += DAY_CHUNK) {
      const chunk = items.slice(c, c + DAY_CHUNK), b = document.createElement('div'); b.className = 'grp-b' + (c + DAY_CHUNK < items.length ? ' mid' : '');
      b.style.minHeight = Math.ceil(chunk.length / per) * (state.zoom * 0.7 + 4) + 'px';
      b._drop = () => { b._built = false; b.style.minHeight = b.offsetHeight + 'px'; b.querySelectorAll('.t').forEach((t) => io.unobserve(t)); b.textContent = ''; };
      b._build = () => { b._built = true; b.style.minHeight = ''; const f = document.createDocumentFragment(); chunk.forEach((p) => { const t = makeTile(p, true); if (p._ar) setAR(t, p._ar); t.firstChild.onload = () => { if (!p._ar) { p._ar = t.firstChild.naturalWidth / t.firstChild.naturalHeight; setAR(t, p._ar); } }; f.append(t); }); b.append(f); };
      sec.append(b); gio.observe(b);
    }
    frag.append(sec);
  });
  else if (mode === 'months') groupBy(list, (d) => d.getFullYear() * 12 + d.getMonth()).forEach(([k, items]) => {
    const y = Math.floor(k / 12), m = k % 12, c = document.createElement('div'); c.className = 'mcard' + (items.length < 3 ? ' one' : '');
    items.slice(0, items.length < 3 ? 1 : 5).forEach((p) => c.append(makeTile(p, false)));
    c.insertAdjacentHTML('beforeend', '<div class="ttl"><b>' + MONTHS[m] + ' ' + y + '</b><span>' + items.length + ' photos</span></div>');
    c.onclick = () => { state.drill = { y, m }; state.mode = 'days'; render(); $('#scroller').scrollTop = 0; };
    frag.append(c);
  });
  else groupBy(list, (d) => d.getFullYear()).forEach(([y, items]) => {
    const c = document.createElement('div'); c.className = 'ycard'; c.append(makeTile(items[0], false));
    c.insertAdjacentHTML('beforeend', '<div class="ttl"><b>' + y + '</b><span>' + items.length + ' photos</span></div>');
    c.onclick = () => { state.drill = { y, m: null }; state.mode = 'months'; render(); $('#scroller').scrollTop = 0; };
    frag.append(c);
  });
  g.append(frag);
}
function setAR(t, ar) { t.style.width = (state.zoom * 0.7 * ar) + 'px'; t.style.flexGrow = ar; }
function syncSeg() {
  const on = document.querySelector('#modes button.on'), th = $('.seg-thumb'); if (!on) return;
  th.style.width = on.offsetWidth + 'px'; th.style.transform = 'translateX(' + on.offsetLeft + 'px)';
}

/* ---------- favorites / hide / delete ---------- */
function toggleFav(p) {
  state.favs.has(p.path) ? state.favs.delete(p.path) : state.favs.add(p.path); persist();
  tileFor(p.path).forEach((t) => t.classList.toggle('isfav', state.favs.has(p.path)));
  lbSync(); if (state.view === 'fav' && $('#lightbox').hidden) render();
}
let toastT;
function toast(msg) {
  let el = $('#toast'); if (!el) { el = document.createElement('div'); el.id = 'toast'; document.body.append(el); }
  el.textContent = msg; void el.offsetWidth; el.classList.add('on'); clearTimeout(toastT); toastT = setTimeout(() => el.classList.remove('on'), 2000);
}
function dropPhotos(paths) {
  const set = new Set(paths);
  state.photos = state.photos.filter((p) => !set.has(p.path));
  const cur = state.list[state.idx]; state.list = state.list.filter((p) => !set.has(p.path));
  paths.forEach((x) => { state.sel.delete(x); tileFor(x).forEach((t) => { io.unobserve(t); t.classList.add('out'); setTimeout(() => t.remove(), 200); }); });
  $('#empty').hidden = state.list.length > 0;
  if (effMode() === 'all') setTimeout(() => vgUpdate(true), 230);
  if (effMode() === 'years' || effMode() === 'months') setTimeout(() => { const y = $('#scroller').scrollTop; render(); $('#scroller').scrollTop = y; }, 220);
  return cur;
}
async function deletePhotos(ps) {
  const done = [];
  for (const p of ps) { const r = await api.trash(p.path, p.mtime); if (r) { done.push(p); state.favs.delete(p.path); state.hidden.delete(p.path); state.trashed.unshift({ path: p.path, name: p.name, mtime: p.mtime, when: Date.now(), thumb: typeof r === 'string' ? r : null }); } }
  if (state.trashed.length > 200) state.trashed.length = 200;
  if (!done.length) { toast("Couldn't delete"); return; }
  persist();
  const lbOpen = !$('#lightbox').hidden, wasIdx = state.idx;
  dropPhotos(done.map((p) => p.path));
  if (lbOpen) { if (!state.list.length) closeLb(); else { state.idx = Math.min(wasIdx, state.list.length - 1); showLb(); } }
  toast(done.length > 1 ? done.length + ' items moved to Recycle Bin' : 'Moved to Recycle Bin');
}
function hidePhotos(ps, hide) {
  ps.forEach((p) => (hide ? state.hidden.add(p.path) : state.hidden.delete(p.path))); persist();
  const lbOpen = !$('#lightbox').hidden, wasIdx = state.idx;
  const paths = ps.map((p) => p.path), set = new Set(paths);
  state.list = state.list.filter((p) => !set.has(p.path));
  paths.forEach((x) => tileFor(x).forEach((t) => { t.classList.add('out'); setTimeout(() => t.remove(), 200); }));
  if (effMode() === 'all') setTimeout(() => vgUpdate(true), 230);
  if (lbOpen) { if (!state.list.length) closeLb(); else { state.idx = Math.min(wasIdx, state.list.length - 1); showLb(); } }
  toast(hide ? 'Hidden' : 'Unhidden');
}

/* ---------- lightbox ---------- */
let externalMode = false, stripLo = 0, stripHi = -1;
function openLb(i, fromEl) {
  if (i < 0) return; state.idx = i;
  const lb = $('#lightbox'); lb.hidden = false;
  const el = fromEl || tileFor(state.list[i].path)[0];
  if (el) { const r = el.getBoundingClientRect(); $('#lb-wrap').style.transformOrigin = (r.left + r.width / 2) + 'px ' + (r.top + r.height / 2) + 'px'; }
  stripLo = 0; stripHi = -1; showLb();
}
function closeLb() {
  if (slideT) ext().then((x) => x.stopSlide());
  $('#lightbox').hidden = true; $('#lightbox').classList.remove('slide'); state.idx = -1; trimSoon(800);
  $('#lb-img').removeAttribute('src'); $('#lb-strip').innerHTML = ''; stripHi = -1; resetLbZoom();
  if (externalMode) { externalMode = false; render(); }
}
let slideT = 0;
function showLb() {
  const p = state.list[state.idx]; if (!p) return;
  const im = $('#lb-img'); resetLbZoom(); im.decoding = 'async'; if (CONVX.test(p.path)) { im.removeAttribute('src'); fullUrl(p.path).then((u) => { if (u && state.list[state.idx] === p) im.src = u; }); } else im.src = toUrl(p.path); applyRot();
  const d = new Date(p.mtime);
  $('#lb-date').textContent = dFull.format(d); $('#lb-time').textContent = dTime.format(d);
  $('#lb-name').textContent = p.name; $('#lb-pdate').textContent = dFull.format(d) + ', ' + dTime.format(d); $('#lb-psize').textContent = fmtSize(p.size);
  $('#lb-ppath').textContent = dirOf(p.path); $('#lb-pdim').textContent = '…';
  $('#lb-info').textContent = (state.idx + 1) + ' of ' + state.list.length + (isShot(p) ? ' · Screenshot' : '') + (isAnim(p) ? ' · Animated' : '');
  im.onload = () => { $('#lb-pdim').textContent = im.naturalWidth + ' × ' + im.naturalHeight; };
  lbSync(); buildStrip();
}
function lbSync() { const p = state.list[state.idx]; if (p) $('#lb-fav').classList.toggle('on', state.favs.has(p.path)); }
function applyRot() { const p = state.list[state.idx]; lbz.rot = (p && state.rot[p.path]) || 0; lbApply(); }
function buildStrip() {
  const s = $('#lb-strip'), i = state.idx;
  if (stripHi < 0 || i < stripLo + 3 || i > stripHi - 3) {
    stripLo = Math.max(0, i - 15); stripHi = Math.min(state.list.length - 1, i + 15); s.innerHTML = '';
    for (let k = stripLo; k <= stripHi; k++) {
      const p = state.list[k], d = document.createElement('div'); d.className = 's'; d._k = k; const im = new Image(); d.append(im);
      api.thumb(p.path, p.mtime).then((u) => { if (d.isConnected) im.src = u; });
      d.onclick = () => { state.idx = d._k; showLb(); }; s.append(d);
    }
  }
  [...s.children].forEach((d) => d.classList.toggle('cur', d._k === i));
  const c = s.children[i - stripLo]; if (c) s.scrollTo({ left: c.offsetLeft - s.clientWidth / 2 + 24, behavior: 'smooth' });
}
function step(d) { const n = state.idx + d; if (n >= 0 && n < state.list.length) { state.idx = n; showLb(); } }
function rotate(dir) { const p = state.list[state.idx]; if (!p) return; state.rot[p.path] = (((state.rot[p.path] || 0) + dir * 90) % 360 + 360) % 360; if (!state.rot[p.path]) delete state.rot[p.path]; persist(); applyRot(); }

/* zoom/pan */
const lbz = { s: 1, x: 0, y: 0, rot: 0, raf: 0 };
function lbApply() {
  lbz.raf = 0; const im = $('#lb-img'), sw = lbz.rot % 180 !== 0;
  const w = sw ? im.offsetHeight : im.offsetWidth, h = sw ? im.offsetWidth : im.offsetHeight;
  const mx = Math.max(0, (lbz.s * w - w) / 2), my = Math.max(0, (lbz.s * h - h) / 2);
  lbz.x = Math.min(mx, Math.max(-mx, lbz.x)); lbz.y = Math.min(my, Math.max(-my, lbz.y));
  const plain = lbz.s === 1 && !lbz.x && !lbz.y && !lbz.rot;
  im.style.transform = plain ? '' : `translate(${lbz.x}px, ${lbz.y}px) scale(${lbz.s}) rotate(${lbz.rot}deg)`;
  im.style.willChange = lbz.s === 1 ? '' : 'transform'; im.style.cursor = lbz.s > 1 ? 'grab' : 'default';
}
const lbSchedule = () => { if (!lbz.raf) lbz.raf = requestAnimationFrame(lbApply); };
function resetLbZoom() { lbz.s = 1; lbz.x = lbz.y = 0; lbApply(); }
function lbZoomAt(f, cx, cy) {
  const r = $('#lb-stage').getBoundingClientRect(), px = cx - (r.left + r.width / 2), py = cy - (r.top + r.height / 2);
  const ns = Math.min(8, Math.max(1, lbz.s * f)), k = ns / lbz.s;
  lbz.x = px - (px - lbz.x) * k; lbz.y = py - (py - lbz.y) * k; lbz.s = ns; if (ns === 1) lbz.x = lbz.y = 0; lbSchedule();
}
$('#lb-img').ondblclick = (e) => { if (lbz.s > 1) resetLbZoom(); else lbZoomAt(2.5, e.clientX, e.clientY); };
$('#lb-img').onpointerdown = (e) => {
  if (lbz.s <= 1 || e.button !== 0) return; e.preventDefault();
  let lx = e.clientX, ly = e.clientY; const im = e.target; im.setPointerCapture(e.pointerId); im.style.cursor = 'grabbing';
  const mv = (ev) => { lbz.x += ev.clientX - lx; lbz.y += ev.clientY - ly; lx = ev.clientX; ly = ev.clientY; lbSchedule(); };
  const up = () => { im.removeEventListener('pointermove', mv); im.removeEventListener('pointerup', up); im.removeEventListener('pointercancel', up); lbSchedule(); };
  im.addEventListener('pointermove', mv); im.addEventListener('pointerup', up); im.addEventListener('pointercancel', up);
};
let gridRaf = 0, gridT;
function setGridZoom(v) {
  state.zoom = Math.min(420, Math.max(120, v));
  if (!gridRaf) gridRaf = requestAnimationFrame(() => { gridRaf = 0; applyZoomCss(); $('#zoom').value = state.zoom; });
  clearTimeout(gridT); gridT = setTimeout(persist, 300);
}
document.addEventListener('wheel', (e) => {
  const lb = !$('#lightbox').hidden;
  if (e.ctrlKey) { e.preventDefault(); const f = Math.exp(-e.deltaY * 0.01); lb ? lbZoomAt(f, e.clientX, e.clientY) : setGridZoom(state.zoom * f); }
  else if (lb) { e.preventDefault(); if (lbz.s > 1) { lbz.x -= e.deltaX; lbz.y -= e.deltaY; lbSchedule(); } }
}, { passive: false });
if (api.lockZoom) api.lockZoom();
if (api.onMaxState) api.onMaxState((on) => document.documentElement.classList.toggle('max', on));

/* ---------- lazy extras (menus, albums, slideshow, deleted view) ---------- */
let extP; const ext = () => extP || (extP = new Promise((res) => { const s = document.createElement('script'); s.src = 'extras.js'; s.onload = () => res(window.Extras); document.head.append(s); }));
let edP; const editorMod = () => edP || (edP = new Promise((res) => { const s = document.createElement('script'); s.src = 'edit.js'; s.onload = () => res(window.Editor); document.head.append(s); }));
window.addEventListener('keydown', (e) => { if (window.Editor && window.Editor.isOpen()) e.stopImmediatePropagation(); }, true);
function openEditor() { const p = state.list[state.idx]; if (!p) return; if (slideT) ext().then((x) => x.stopSlide()); editorMod().then((E) => E.open(p)); }
const selected = () => state.list.filter((p) => state.sel.has(p.path));
window.G = { openEdit: (p) => { const i = state.list.indexOf(p); if (i >= 0) { openLb(i); openEditor(); } }, state, $, svg, hydrate, toast, persist, render, renderSide, toUrl, fullUrl, base, dirOf, fmtSize, deletePhotos, hidePhotos, toggleFav, rotate, selected, setView, refreshSel, tileFor, makeTile, openAt: (i) => openLb(i), dFull, dTime, MONTHS, step, showLb, closeLb };

/* ---------- wiring ---------- */
$('#lb-close').onclick = closeLb; $('#lb-bg').onclick = closeLb; $('#lb-stage').onclick = (e) => { if (e.target.id === 'lb-stage') closeLb(); };
$('#lb-prev').onclick = () => step(-1); $('#lb-next').onclick = () => step(1);
$('#lb-fav').onclick = () => toggleFav(state.list[state.idx]);
$('#lb-del').onclick = () => deletePhotos([state.list[state.idx]]);
$('#lb-edit').onclick = openEditor; $('#lb-rotl').onclick = () => rotate(-1); $('#lb-rotr').onclick = () => rotate(1); $('#lb-play').onclick = () => ext().then((x) => x.toggleSlide());
$('#lb-info-btn').onclick = () => $('#lightbox').classList.toggle('panel');
$('#lb-reveal').onclick = () => api.reveal(state.list[state.idx].path);
$('#lb-title').title = 'Click to start slideshow';
$('#side-toggle').onclick = () => { document.body.classList.toggle('collapsed'); setTimeout(syncSeg, 300); };
$('#back').onclick = () => { const d = state.drill; if (d.m != null) { state.drill = { y: d.y, m: null }; state.mode = 'months'; } else { state.drill = { y: null, m: null }; state.mode = 'years'; } render(); };
document.querySelectorAll('#modes button').forEach((b) => (b.onclick = () => { state.mode = b.dataset.mode; state.drill = { y: null, m: null }; persist(); render(); }));
document.querySelectorAll('#theme button').forEach((b) => (b.onclick = () => applyTheme(b.dataset.theme)));
$('#theme-btn').onclick = (e) => { e.stopPropagation(); const p = $('#theme-pop'); p.hidden = !p.hidden; };
document.addEventListener('click', (e) => { if (!e.target.closest('#theme-pop')) $('#theme-pop').hidden = true; const c = $('#ctx'); if (c) c.remove(); });
async function addFolder() { const f = await api.pickFolder(); if (f && !state.folders.includes(f)) { state.folders.push(f); persist(); await rescan(); } }
$('#add-folder').onclick = () => ext().then((x) => x.addMenu()); window.G.addFolder = addFolder;
$('#empty-add').onclick = addFolder;
let st; $('#search').oninput = () => { clearTimeout(st); st = setTimeout(render, 150); };
$('#zoom').oninput = (e) => setGridZoom(+e.target.value);
$('#scroller').oncontextmenu = (e) => { if (!e.target.closest('.t')) e.preventDefault(); };
window.addEventListener('resize', syncSeg);
new ResizeObserver(layoutCols).observe($('#scroller'));
let trimT; const trimSoon = (ms) => { clearTimeout(trimT); trimT = setTimeout(() => { api.trim && api.trim(); try { window.gc && window.gc(); } catch {} setTimeout(() => { try { window.gc && window.gc(); } catch {} }, 2500); setTimeout(() => { try { window.gc && window.gc(); } catch {} }, 8000); }, ms || 1500); };
let vgRaf = 0; $('#scroller').addEventListener('scroll', () => { trimSoon(4000); if (!vgRaf && effMode() === 'all') vgRaf = requestAnimationFrame(() => { vgRaf = 0; vgUpdate(); }); }, { passive: true });
$('#toolbar').addEventListener('dblclick', (e) => { if (!e.target.closest('button, input, .seg, .search, .zoom-wrap')) api.toggleMaximize(); });

document.addEventListener('keydown', (e) => {
  const tag = (e.target && e.target.tagName) || ''; const typing = tag === 'INPUT' || tag === 'TEXTAREA';
  if ((e.ctrlKey || e.metaKey) && ['+', '=', '-', '_', '0'].includes(e.key)) {
    e.preventDefault(); const k = e.key;
    if (!$('#lightbox').hidden) { const r = $('#lb-stage').getBoundingClientRect(); k === '0' ? resetLbZoom() : lbZoomAt(k === '-' || k === '_' ? 0.8 : 1.25, r.left + r.width / 2, r.top + r.height / 2); }
    else setGridZoom(k === '0' ? 220 : state.zoom + (k === '-' || k === '_' ? -30 : 30)); return;
  }
  if (typing) return;
  const lb = !$('#lightbox').hidden;
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'a' && !lb) { e.preventDefault(); state.sel = new Set(state.list.map((p) => p.path)); refreshSel(); return; }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'c') { const ps = lb ? [state.list[state.idx]] : selected(); if (ps[0]) { api.copyImage(ps[0].path); toast('Copied'); } return; }
  if (lb) {
    if (e.key === 'Escape') { slideT ? ext().then((x) => x.stopSlide()) : closeLb(); }
    else if (e.key === 'ArrowLeft') step(-1); else if (e.key === 'ArrowRight') step(1);
    else if (e.key === 'Delete') deletePhotos([state.list[state.idx]]);
    else if (e.key.toLowerCase() === 'f') toggleFav(state.list[state.idx]);
    else if (e.key.toLowerCase() === 'i') $('#lightbox').classList.toggle('panel');
    else if (e.key.toLowerCase() === 'e') openEditor();
    else if (e.key.toLowerCase() === 'r') rotate(e.shiftKey ? -1 : 1);
    else if (e.key.toLowerCase() === 'h') hidePhotos([state.list[state.idx]], !state.hidden.has(state.list[state.idx].path));
    return;
  }
  if (e.key === 'Delete') { const ps = state.sel.size ? selected() : state.list.filter((x) => x.path === hoverPath); if (ps.length) deletePhotos(ps); }
  else if (e.key === 'Escape' && state.sel.size) { state.sel.clear(); refreshSel(); }
});

init();