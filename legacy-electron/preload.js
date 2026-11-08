const { contextBridge, ipcRenderer, webFrame } = require('electron');
contextBridge.exposeInMainWorld('api', {
  getSettings: () => ipcRenderer.invoke('settings:get'),
  setSettings: (p) => ipcRenderer.invoke('settings:set', p),
  pickFolder: () => ipcRenderer.invoke('folder:pick'),
  scan: (d, recursive = true) => ipcRenderer.invoke('folder:scan', d, recursive),
  full: (p) => ipcRenderer.invoke('full', p),
  thumb: (p, m, s) => ipcRenderer.invoke('thumb', p, m, s),
  copyImage: (p) => ipcRenderer.invoke('copy-image', p),
  copyText: (t) => ipcRenderer.invoke('copy-text', t),
  recover: (p) => ipcRenderer.invoke('recover', p),
  saveEdited: (p, d, e) => ipcRenderer.invoke('save-edited', p, d, e),
  onMaxState: (cb) => ipcRenderer.on('maxstate', (_e, on) => cb(on)),
  trim: () => { try { webFrame.clearCache(); } catch {} },
  toggleMaximize: () => ipcRenderer.invoke('toggle-maximize'),
  trash: (p, m) => ipcRenderer.invoke('trash', p, m),
  lockZoom: () => { try { webFrame.setVisualZoomLevelLimits(1, 1); } catch {} },
  initialFile: () => ipcRenderer.invoke('initial-file'),
  onOpenFile: (cb) => ipcRenderer.on('open-file', (_e, p) => cb(p)),
  defaultFolder: () => ipcRenderer.invoke('default-folder'),
  reveal: (p) => ipcRenderer.invoke('reveal', p)
});
