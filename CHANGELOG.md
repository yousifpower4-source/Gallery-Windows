# Changelog

All notable changes to Gallery for Windows. Format follows [Keep a Changelog](https://keepachangelog.com/).

## [2.0.0] - WPF rewrite

Gallery was rewritten from Electron to native **WPF on .NET 8**. It is a single self-contained `Gallery.exe`, starts faster and uses much less memory than the Electron build.

### Added
- Native WPF app that reproduces the Electron UI (Inter font, rounded grid, light/dark/system theme).
- Library views: **Years, Months, Days, All Photos**, plus Favorites, Recents, Screenshots, Animated, Albums (one per library folder) and Hidden.
- Lightbox with arrow-key navigation, filmstrip, zoom (`+` / `-` / `0`), slideshow, info panel and Show in Explorer.
- **Rotation** in the lightbox (`R` clockwise, `Shift+R` counter-clockwise). The rotation is remembered per photo and the original file is not modified.
- Built-in **editor** (Adjust, Filters, Crop). Saving writes a new copy named `name (edited)`; the original is never changed.
- **Delete** moves photos to the **Recycle Bin** (`Delete`). **`Shift+Delete`** deletes permanently after a confirmation. Both work on a multi-selection, and the Recycle Bin move suppresses Windows' admin prompts.
- Multi-select with click, `Ctrl`, `Shift` and `Ctrl+A`; search with `Ctrl+F`.
- Opens HEIC/HEIF and TIFF through the Windows imaging codecs when the codec is installed.
- NSIS installer that registers Gallery machine-wide so it appears in Windows 11's **Open with** list for all common image types, adds Start Menu and Desktop shortcuts, and removes the old Electron build if it is installed.
- Single-instance behaviour: opening another image from Explorer reuses the running window.

### Changed
- **Memory and speed:** software rendering by default (avoids the GPU process and keeps RAM low), a persistent on-disk thumbnail cache (`%LOCALAPPDATA%GalleryWpf	humbs`) so folders open fast after the first scan, and no bundled Chromium. The installed footprint is a single ~140 MB self-contained exe (installer ~44 MB) instead of an Electron runtime.
- Settings are stored in `%APPDATA%\GalleryWpf\settings.json`, and existing settings from the Electron version are imported automatically on first run.

### Removed
- RAW format support (not planned).

## [1.0.0] - Electron version (legacy)

The original Electron app, now kept in [`legacy-electron/`](legacy-electron).

### Features
- Responsive photo grid with rounded corners, lazy loading and staggered fade-in.
- Lightbox viewer with arrow-key navigation, click-to-zoom, favourites and Show in Explorer.
- Light / Dark / Auto theme, favourites, recents, per-folder views, search, sorting and a thumbnail size slider.
- Library folders, favourites and theme remembered between launches.
- electron-builder NSIS installer and portable build.

### Fixed during 1.x
- Window mispainted when maximized (software compositing combined with an acrylic/mica backdrop). Fixed by using a solid sidebar.
- Window not coming to the foreground on launch or when opening a second image.
- Grid not filling the window width after maximize/restore; columns now follow the window width.
- Lower RAM use through in-process GPU and software compositing flags, a thumbnail cache, and clearing the Chromium cache after scrolling, the lightbox and the editor.
