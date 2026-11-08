# Gallery for Windows (legacy Electron version)

A fast, modern photo gallery for Windows, built with Electron.

## Features
- Clean, responsive photo grid with rounded corners, lazy loading and staggered fade-in animations
- Translucent, blurred sidebar (Windows 11 acrylic window material, with CSS blur fallback)
- Lightbox viewer: smooth open animation, arrow-key navigation, click to zoom, favorite, show in Explorer
- Light / Dark / Auto (follows Windows) theme
- Favorites, Recents, per-folder views, search, sorting, thumbnail size slider
- Your library folders, favorites and theme are remembered between launches

## Install (icon + shortcuts)
Double-click `install.bat`: it runs `npm install` and adds a **Gallery** shortcut with the app icon to your Desktop and Start Menu.

## Run it
Requires [Node.js](https://nodejs.org) 18+.

```powershell
cd "D:\#AI workflows\Claude work\Gallary Windows"
npm install
npm start
```

On first launch it shows your **Pictures** folder. Use **＋ Add Folder** to add more
(subfolders are scanned too). Right-click a folder in the sidebar to remove it.

## Shortcuts (lightbox)
`←` / `→` navigate · `F` favorite · `Esc` close · click image to zoom

## Build an installer
```powershell
npm run dist
```
Output goes to `dist/`.

## Defaults chosen
Electron (no browser setup needed, native folder access), formats: jpg, png, gif, webp, bmp, avif, svg;
scan depth 6 levels; the app never modifies or uploads your images.
