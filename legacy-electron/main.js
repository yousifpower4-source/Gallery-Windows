const { app, BrowserWindow, ipcMain, dialog, nativeTheme, shell, nativeImage, clipboard, screen } = require('electron');
const crypto = require('crypto');

app.commandLine.appendSwitch('disable-features', 'CalculateNativeWinOcclusion,Translate,MediaRouter');
app.commandLine.appendSwitch('js-flags', '--max-old-space-size=128 --expose-gc');
// Measured: in-process GPU + software compositing drops private memory ~250 -> ~140 MB and one process.
if (process.env.GAL_EXTRA) process.env.GAL_EXTRA.split(';').forEach((s) => { const [k, ...v] = s.split('='); if (k) app.commandLine.appendSwitch(k.trim(), v.join('=')); });
const GPU = process.env.GAL_GPU || 'soft'; // soft = in-process GPU + software compositing (lowest RAM); inproc = in-process GPU with GPU compositing; full = default Chromium GPU process
if (GPU !== 'full') app.commandLine.appendSwitch('in-process-gpu');
if (GPU === 'soft') app.commandLine.appendSwitch('disable-gpu-compositing');
app.commandLine.appendSwitch('enable-features', 'NetworkServiceInProcess2'); // measured: drops the utility process, ~-15 MB private
app.commandLine.appendSwitch('disk-cache-size', '1');
app.commandLine.appendSwitch('media-cache-size', '1');
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');

const NATIVE = new Set(['.jpg', '.jpeg', '.jpe', '.jfif', '.png', '.apng', '.gif', '.webp', '.bmp', '.dib', '.ico', '.avif', '.svg']);
// Not decodable by Chromium: converted on demand in main (Windows imaging for heic/heif/tiff)
const CONV = new Set(['.heic', '.heif', '.tif', '.tiff']);
const EXT = new Set([...NATIVE, ...CONV]);
const settingsFile = () => path.join(app.getPath('userData'), 'settings.json');

function loadSettings() {
  try { return JSON.parse(fs.readFileSync(settingsFile(), 'utf8')); } catch { return {}; }
}
function saveSettings(s) {
  try { fs.writeFileSync(settingsFile(), JSON.stringify(s)); } catch {}
}

async function scan(dir, recursive = true, depth = 0, out = []) {
  let entries;
  try { entries = await fs.promises.readdir(dir, { withFileTypes: true }); } catch { return out; }
  const files = [];
  for (const e of entries) {
    if (e.name.startsWith('.')) continue;
    const full = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (recursive && depth < 6) await scan(full, recursive, depth + 1, out);
    } else if (EXT.has(path.extname(e.name).toLowerCase())) files.push([full, e.name]);
  }
  for (let i = 0; i < files.length; i += 64) {
    await Promise.all(files.slice(i, i + 64).map(async ([full, name]) => {
      try {
        const st = await fs.promises.stat(full);
        out.push({ path: full, name, folder: dir, mtime: st.mtimeMs, size: st.size });
      } catch {}
    }));
  }
  return out;
}


const fullDir = () => path.join(app.getPath('userData'), 'full');
async function makeFull(filePath) {
  const ext = path.extname(filePath).toLowerCase();
  if (!CONV.has(ext)) return null;
  try {
    const st = await fs.promises.stat(filePath);
    const out = path.join(fullDir(), crypto.createHash('sha1').update(filePath + st.mtimeMs + st.size).digest('hex') + '.jpg');
    if (fs.existsSync(out)) return pathToFileURL(out).href;
    let buf = null;
    { // Windows imaging (HEIF needs the free "HEIF Image Extensions"; TIFF is built in)
      try { const img = await nativeImage.createThumbnailFromPath(filePath, { width: 4096, height: 4096 }); if (img && !img.isEmpty()) buf = img.toJPEG(92); } catch {}
    }
    if (!buf) return null;
    await fs.promises.mkdir(fullDir(), { recursive: true });
    await fs.promises.writeFile(out, buf);
    return pathToFileURL(out).href;
  } catch { return null; }
}
ipcMain.handle('full', (_e, p) => makeFull(p));
// Thumbnail cache (~360px JPEG), max 4 concurrent decodes
const thumbDir = () => path.join(app.getPath('userData'), 'thumbs');
let thumbActive = 0, gcT; const thumbQueue = [];
function runThumb(fn) {
  return new Promise((res) => { thumbQueue.push({ fn, res }); pump(); });
}
function pump() {
  while (thumbActive < 4 && thumbQueue.length) {
    const { fn, res } = thumbQueue.pop(); thumbActive++; // newest request first: those tiles are the ones on screen now
    fn().then(res, () => res(null)).finally(() => { thumbActive--; pump(); });
  }
  if (!thumbActive && !thumbQueue.length) { clearTimeout(gcT); gcT = setTimeout(() => { try { global.gc && global.gc(); } catch {} }, 2500); }
}
async function makeThumb(filePath, mtime, sz) {
  sz = sz === 256 ? 256 : 360;
  const orig = pathToFileURL(filePath).href;
  const ext = path.extname(filePath).toLowerCase();
  if (ext === '.svg' || ext === '.gif') return orig;
  try {
    const st = await fs.promises.stat(filePath);
    const key = crypto.createHash('sha1').update(filePath + (mtime || st.mtimeMs) + st.size + (sz === 256 ? '' : '')).digest('hex');
    const out = path.join(thumbDir(), key + (sz === 256 ? '-256' : '') + '.jpg');
    if (fs.existsSync(out)) return pathToFileURL(out).href;
    let img;
    try { img = await nativeImage.createThumbnailFromPath(filePath, { width: sz, height: sz }); } catch {}
    if (!img || img.isEmpty()) {
      img = nativeImage.createFromPath(filePath);
      if (img.isEmpty()) return orig;
      img = img.resize({ width: sz, quality: 'good' });
    }
    await fs.promises.mkdir(thumbDir(), { recursive: true });
    await fs.promises.writeFile(out, img.toJPEG(80));
    return pathToFileURL(out).href;
  } catch { return orig; }
}
ipcMain.handle('trash', async (_e, filePath, mtime) => {
  try {
    let key = null;
    try { const st = await fs.promises.stat(filePath); key = crypto.createHash('sha1').update(filePath + (mtime || st.mtimeMs) + st.size).digest('hex'); } catch {}
    await shell.trashItem(filePath);
    return key && fs.existsSync(path.join(thumbDir(), key + '.jpg')) ? pathToFileURL(path.join(thumbDir(), key + '.jpg')).href : true;
  } catch { return false; }
});
ipcMain.handle('copy-image', (_e, p) => { try { const img = nativeImage.createFromPath(p); if (img.isEmpty()) return false; clipboard.writeImage(img); return true; } catch { return false; } });
ipcMain.handle('copy-text', (_e, t) => { clipboard.writeText(String(t)); return true; });
ipcMain.handle('recover', (_e, orig) => new Promise((resolve) => {
  const ps = "$o=$env:GAL_PATH; $d=Split-Path $o -Parent; $n=Split-Path $o -Leaf; $b=[IO.Path]::GetFileNameWithoutExtension($n); $sh=New-Object -ComObject Shell.Application; $rb=$sh.NameSpace(10); $ok=$false; foreach($i in $rb.Items()){ $od=$rb.GetDetailsOf($i,1); if($od.TrimEnd('\\') -ieq $d.TrimEnd('\\') -and ($i.Name -ieq $n -or $i.Name -ieq $b)){ $i.InvokeVerb('undelete'); $ok=$true; break } }; if($ok){'OK'}";
  require('child_process').execFile('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', ps], { env: { ...process.env, GAL_PATH: String(orig) }, timeout: 20000, windowsHide: true }, (err, out) => {
    setTimeout(() => resolve(!err && /OK/.test(out || '') && fs.existsSync(orig)), 600);
  });
}));
ipcMain.handle('save-edited', async (_e, orig, dataUrl, ext) => {
  try {
    if (!EXT.has(path.extname(orig).toLowerCase()) || !/^data:image\/(png|jpeg);base64,/.test(dataUrl)) return null;
    const e2 = ext === '.png' ? '.png' : '.jpg', dir = path.dirname(orig), stem = path.basename(orig, path.extname(orig));
    let out = path.join(dir, stem + ' (edited)' + e2), n = 2;
    while (fs.existsSync(out)) out = path.join(dir, stem + ' (edited ' + n++ + ')' + e2);
    await fs.promises.writeFile(out, Buffer.from(dataUrl.slice(dataUrl.indexOf(',') + 1), 'base64'), { flag: 'wx' });
    const st = await fs.promises.stat(out);
    return { path: out, name: path.basename(out), mtime: st.mtimeMs, size: st.size };
  } catch { return null; }
});
ipcMain.handle('toggle-maximize', () => { if (!win) return; win.isMaximized() ? win.unmaximize() : win.maximize(); });
ipcMain.handle('thumb', (_e, filePath, mtime, sz) => runThumb(() => makeThumb(filePath, mtime, sz)));
let win = null;

function findImageArg(argv) {
  for (const a of argv.slice(1)) {
    if (!a || a.startsWith('-')) continue;
    if (EXT.has(path.extname(a).toLowerCase()) && fs.existsSync(a)) return path.resolve(a);
  }
  return null;
}

// Backdrop materials (acrylic/mica) + software compositing mispaint a maximized window (content stops short of the right edge), and the
// maximize-time material swap does not recover it. 'none' (solid window, opaque sidebar) paints correctly and keeps RAM low.
const MATERIAL = process.env.GAL_MAT || 'none';
const MIN_W = 760, MIN_H = 520;
const inter = (a, b) => Math.max(0, Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x)) * Math.max(0, Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y));
// Saved bounds are only reused if enough of the window is still on a connected display; otherwise centre on the primary work area.
function pickBounds(s) {
  const prim = screen.getPrimaryDisplay().workArea, w = s.win;
  if (w && [w.x, w.y, w.width, w.height].every(Number.isFinite)) {
    const r = { x: Math.round(w.x), y: Math.round(w.y), width: Math.max(MIN_W, Math.round(w.width)), height: Math.max(MIN_H, Math.round(w.height)) };
    const best = screen.getAllDisplays().map((d) => ({ d, a: inter(d.workArea, r) })).sort((p, q) => q.a - p.a)[0];
    if (best && best.a >= 120 * 120) {
      const wa = best.d.workArea; r.width = Math.min(r.width, wa.width); r.height = Math.min(r.height, wa.height);
      r.x = Math.min(Math.max(r.x, wa.x), wa.x + wa.width - r.width); r.y = Math.min(Math.max(r.y, wa.y), wa.y + wa.height - r.height);
      return { bounds: r, maximized: !!w.maximized };
    }
  }
  const width = Math.min(1280, prim.width), height = Math.min(820, prim.height);
  return { bounds: { x: prim.x + Math.round((prim.width - width) / 2), y: prim.y + Math.round((prim.height - height) / 2), width, height }, maximized: false };
}
let saveT;
function persistWin() {
  clearTimeout(saveT);
  saveT = setTimeout(() => {
    if (!win || win.isDestroyed() || win.isMinimized() || win.isFullScreen()) return;
    const max = win.isMaximized(), b = max ? win.getNormalBounds() : win.getBounds();
    const data = { ...loadSettings(), win: { x: b.x, y: b.y, width: b.width, height: b.height, maximized: max, displayId: screen.getDisplayMatching(win.getBounds()).id } };
    fs.promises.writeFile(settingsFile(), JSON.stringify(data)).catch(() => {});
  }, 500);
}
// Bring the window to the front even when Windows' foreground lock would keep a freshly launched app behind others.
function bringToFront() {
  if (!win || win.isDestroyed()) return;
  if (win.isMinimized()) win.restore();
  win.show(); win.moveTop(); win.focus();
  win.setAlwaysOnTop(true, 'screen-saver');
  setTimeout(() => { if (win && !win.isDestroyed()) { win.setAlwaysOnTop(false); win.focus(); } }, 200);
  // Windows' foreground lock can still refuse (e.g. launched from a dock that isn't the foreground app). Last resort: a synthetic Alt tap
  // grants this process foreground rights, then SetForegroundWindow. Runs a short PowerShell only when we are still not in front.
  setTimeout(() => {
    if (!win || win.isDestroyed() || win.isFocused() || process.platform !== 'win32') return;
    const hb = win.getNativeWindowHandle(), hwnd = hb.length >= 8 ? hb.readBigUInt64LE(0).toString() : String(hb.readUInt32LE(0));
    const ps = "Add-Type -Namespace G -Name W -MemberDefinition '[DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport(\"user32.dll\")] public static extern void keybd_event(byte v, byte s, uint f, UIntPtr e);'; " +
      "$h=[IntPtr][int64]$env:GAL_HWND; [G.W]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [G.W]::keybd_event(0x12,0,2,[UIntPtr]::Zero); [void][G.W]::SetForegroundWindow($h)";
    require('child_process').execFile('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', ps], { env: { ...process.env, GAL_HWND: hwnd }, windowsHide: true, timeout: 8000 }, () => {});
  }, 350);
}// If a monitor is unplugged or rearranged, pull a stranded (non-maximized) window back onto a visible display.
function ensureVisible() {
  if (!win || win.isDestroyed() || win.isMaximized() || win.isFullScreen() || win.isMinimized()) return;
  const b = win.getBounds();
  if (screen.getAllDisplays().some((d) => inter(d.workArea, b) >= 120 * 120)) return;
  win.setBounds(pickBounds({}).bounds);
}

function createWindow() {
  const settings = loadSettings();
  nativeTheme.themeSource = settings.theme || 'system';
  const initial = pickBounds(settings);
  win = new BrowserWindow({
    ...initial.bounds, minWidth: MIN_W, minHeight: MIN_H,
    title: 'Gallery',
    icon: path.join(__dirname, 'build', 'icon.ico'),
    show: false,
    backgroundColor: MATERIAL === 'none' ? (nativeTheme.shouldUseDarkColors ? '#1e1e1e' : '#ffffff') : '#00000000',
    ...(MATERIAL === 'none' ? {} : { backgroundMaterial: MATERIAL }), // Windows 11 translucent material; ignored elsewhere
    titleBarStyle: 'hidden',
    titleBarOverlay: { color: '#00000000', symbolColor: nativeTheme.shouldUseDarkColors ? '#ffffff' : '#000000', height: 52 },
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, spellcheck: false, backgroundThrottling: true, devTools: false }
  });
  win.on('page-title-updated', (e) => e.preventDefault());
  win.once('ready-to-show', () => { win.show(); bringToFront(); });
  const overlay = () => { try { win.setTitleBarOverlay({ color: '#00000000', symbolColor: nativeTheme.shouldUseDarkColors ? '#ffffff' : '#000000', height: 52 }); } catch {} };
  ['resize', 'moved', 'maximize', 'unmaximize', 'close'].forEach((ev) => win.on(ev, persistWin));
  // Software compositing + a backdrop material (acrylic/mica) leaves a maximized window mispainted (content stops short of the right edge).
  // So the material is only used while the window is normal-sized; maximized windows use a solid look (the renderer swaps to opaque colours).
  const maxState = (on) => {
    try { if (MATERIAL !== 'none') win.setBackgroundMaterial(on ? 'none' : MATERIAL); } catch {}
    if (!win.webContents.isDestroyed()) win.webContents.send('maxstate', on);
    setTimeout(() => { if (win && !win.isDestroyed()) win.webContents.invalidate(); }, 120);
  };
  win.on('maximize', () => maxState(true)); win.on('unmaximize', () => maxState(false));
  win.webContents.on('did-finish-load', () => win.webContents.send('maxstate', win.isMaximized()));
  if (initial.maximized) win.maximize();
  win.on('moved', ensureVisible);
  screen.on('display-removed', ensureVisible); screen.on('display-metrics-changed', ensureVisible);
  win.webContents.setVisualZoomLevelLimits(1, 1);
  win.setMenuBarVisibility(false);
  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));
  nativeTheme.on('updated', () => {
    try { win.setTitleBarOverlay({ color: '#00000000', symbolColor: nativeTheme.shouldUseDarkColors ? '#ffffff' : '#000000', height: 52 }); } catch {}
  });
}

ipcMain.handle('settings:get', () => loadSettings());
ipcMain.handle('settings:set', (_e, patch) => {
  const s = { ...loadSettings(), ...patch };
  saveSettings(s);
  if (patch.theme) nativeTheme.themeSource = patch.theme;
  return s;
});
ipcMain.handle('folder:pick', async () => {
  const r = await dialog.showOpenDialog({ properties: ['openDirectory'] });
  return r.canceled ? null : r.filePaths[0];
});
ipcMain.handle('folder:scan', async (_e, dir, recursive = true) => { const r = await scan(dir, recursive !== false); clearTimeout(gcT); gcT = setTimeout(() => { try { global.gc && global.gc(); } catch {} }, 2500); return r; });
ipcMain.handle('default-folder', () => app.getPath('pictures'));
ipcMain.handle('reveal', (_e, p) => shell.showItemInFolder(p));

const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  app.on('second-instance', (_e, argv) => {
    if (win) {
      bringToFront();
      const f = findImageArg(argv.slice(app.isPackaged ? 0 : 1));
      if (f) win.webContents.send('open-file', f);
    }
  });
  ipcMain.handle('initial-file', () => findImageArg(process.argv.slice(app.isPackaged ? 0 : 1)));
  app.whenReady().then(() => { createWindow(); try { app.focus({ steal: true }); } catch {} });
}
app.on('window-all-closed', () => app.quit());
