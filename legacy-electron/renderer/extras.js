// Loaded lazily on first use (context menu, albums, slideshow, Recently Deleted).
window.Extras = (() => {
  const { state, $, svg, toast, persist, render, renderSide, base, dirOf, deletePhotos, hidePhotos, toggleFav, rotate, selected } = window.G;
  const place = (m, x, y) => { document.body.append(m); const r = m.getBoundingClientRect(); m.style.left = Math.min(x, innerWidth - r.width - 8) + 'px'; m.style.top = Math.min(y, innerHeight - r.height - 8) + 'px'; };
  function popup(items, x, y) {
    const old = $('#ctx'); if (old) old.remove();
    const m = document.createElement('div'); m.id = 'ctx';
    items.forEach((it) => { if (it === '-') { m.append(document.createElement('hr')); return; } const b = document.createElement('button'); b.textContent = it[0]; if (it[2]) b.className = 'red'; b.onclick = (e) => { e.stopPropagation(); m.remove(); it[1](); }; m.append(b); });
    m.oncontextmenu = (e) => e.preventDefault(); place(m, x, y);
  }
  function dialog(html, wire) {
    const d = document.createElement('div'); d.className = 'dlg'; d.innerHTML = '<div>' + html + '</div>'; document.body.append(d);
    d.onclick = (e) => { if (e.target === d) d.remove(); }; wire(d); return d;
  }
  function askName(title, init, cb) {
    dialog('<h4>' + title + '</h4><input id="dn" spellcheck="false"><div class="btns"><button class="btn-soft" id="dc" style="width:auto;margin:0">Cancel</button><button class="btn-accent" id="dk">OK</button></div>', (d) => {
      const i = d.querySelector('#dn'); i.value = init || ''; i.focus(); i.select();
      const ok = () => { const v = i.value.trim(); if (v) { d.remove(); cb(v); } };
      d.querySelector('#dk').onclick = ok; d.querySelector('#dc').onclick = () => d.remove(); i.onkeydown = (e) => { e.stopPropagation(); if (e.key === 'Enter') ok(); if (e.key === 'Escape') d.remove(); };
    });
  }
  function addToAlbum(ps) {
    const names = Object.keys(state.albums);
    dialog('<h4>Add to Album</h4>' + names.map((n) => '<button class="opt" data-n="' + n.replace(/"/g, '&quot;') + '"></button>').join('') + '<button class="opt" id="na">New Album…</button>', (d) => {
      d.querySelectorAll('.opt[data-n]').forEach((b) => { b.textContent = b.dataset.n; b.onclick = () => { d.remove(); put(b.dataset.n, ps); }; });
      d.querySelector('#na').onclick = () => { d.remove(); askName('New Album', '', (n) => { state.albums[n] = state.albums[n] || []; put(n, ps); }); };
    });
  }
  function put(n, ps) { const s = new Set(state.albums[n] || []); ps.forEach((p) => s.add(p.path)); state.albums[n] = [...s]; persist(); renderSide(); toast('Added to ' + n); }
  function menu(e) {
    const ps = selected(); if (!ps.length) return; const one = ps.length === 1 ? ps[0] : null;
    const allFav = ps.every((p) => state.favs.has(p.path)), hid = state.view === 'hidden';
    popup([
      ...(one ? [['Open', () => window.G.state.list.indexOf(one) >= 0 && openFrom(one)], '-'] : []),
      [allFav ? 'Unfavorite' : 'Favorite', () => ps.forEach((p) => { if (state.favs.has(p.path) === allFav) toggleFav(p); })],
      ['Add to Album…', () => addToAlbum(ps)],
      [hid ? 'Unhide' : 'Hide', () => hidePhotos(ps, !hid)],
      ...(one ? [['Edit…', () => window.G.openEdit(one)], ['Rotate Clockwise', () => { state.rot[one.path] = ((state.rot[one.path] || 0) + 90) % 360; persist(); toast('Rotated (view only)'); }], '-',
        ['Copy', () => { window.api.copyImage(one.path); toast('Copied'); }], ['Copy Path', () => { window.api.copyText(one.path); toast('Path copied'); }], ['Show in Explorer', () => window.api.reveal(one.path)]] : []),
      '-', ['Move to Recycle Bin', () => deletePhotos(ps), true]
    ], e.clientX, e.clientY);
  }
  function openFrom(p) { const i = state.list.indexOf(p); if (i < 0) return; const el = document.querySelector('#grid .t[data-path="' + CSS.escape(p.path) + '"]'); el ? el.click() : window.G.openAt(i); }
  function addMenu() { const r = $('#add-folder').getBoundingClientRect(); popup([['New Album…', () => askName('New Album', '', (n) => { state.albums[n] = state.albums[n] || []; persist(); renderSide(); })], ['Add Folder…', () => window.G.addFolder()]], r.left, r.bottom + 4); }
  function albumMenu(e, name) {
    popup([['Rename…', () => askName('Rename Album', name, (n) => { if (n !== name) { state.albums[n] = state.albums[name]; delete state.albums[name]; if (state.view === 'a:' + name) state.view = 'a:' + n; persist(); renderSide(); } })],
      ['Delete Album', () => { delete state.albums[name]; if (state.view === 'a:' + name) state.view = 'lib'; persist(); renderSide(); render(); }, true]], e.clientX, e.clientY);
  }
  function renderDeleted() {
    const g = $('#grid'); g.innerHTML = ''; g.className = 'mode-days'; $('#title').textContent = 'Recently Deleted'; $('#back').hidden = true;
    $('#empty').hidden = state.trashed.length > 0;
    const hdr = document.createElement('div'); hdr.className = 'grp-h'; hdr.innerHTML = 'Items you moved to the Recycle Bin<small>Recover restores them from the Recycle Bin</small>'; g.append(hdr);
    const b = document.createElement('div'); b.className = 'grp-b'; g.append(b);
    state.trashed.forEach((it) => {
      const t = document.createElement('div'); t.className = 't in'; t.style.cssText = 'width:var(--row);flex:0 0 var(--row);cursor:default;'; t.title = it.path;
      const im = document.createElement('img'); if (it.thumb) im.src = it.thumb; t.append(im);
      const bt = document.createElement('button'); bt.className = 'act'; bt.style.cssText = 'left:6px;right:6px;top:auto;bottom:6px;width:auto;height:26px;border-radius:8px;opacity:1;font-size:12px;padding:0;'; bt.textContent = 'Recover';
      bt.onclick = async () => { bt.textContent = '…'; const ok = await window.api.recover(it.path); if (ok) { state.trashed = state.trashed.filter((x) => x !== it); persist(); toast('Recovered ' + it.name); window.G.state.view = 'deleted'; renderDeleted(); window.api.scan; setTimeout(() => location.reload(), 600); } else { bt.textContent = 'Recover'; toast('Could not recover — restore it from the Recycle Bin'); } };
      t.append(bt); b.append(t);
    });
  }
  function stopSlide() { clearInterval(slideT); slideT = 0; $('#lightbox').classList.remove('slide'); $('#lb-img').style.opacity = ''; }
  function toggleSlide() {
    if (slideT) { stopSlide(); return; }
    $('#lightbox').classList.add('slide'); toast('Slideshow — Esc to stop');
    slideT = setInterval(() => { const im = $('#lb-img'); im.style.opacity = 0; setTimeout(() => { if (!slideT) return; window.G.state.idx = (window.G.state.idx + 1) % window.G.state.list.length; window.G.showLb(); im.style.opacity = 1; }, 260); }, 3000);
  }
  return { menu, addMenu, albumMenu, renderDeleted, toggleSlide, stopSlide };
})();