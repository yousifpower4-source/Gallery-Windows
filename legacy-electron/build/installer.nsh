; Machine-wide (HKLM) registration so Gallery appears in the Windows 11 "Open with" list and Settings > Default apps.
; It never overwrites an extension's default value, so the current default viewer is not changed silently.
!macro GalleryRegExt EXT
  WriteRegStr HKLM "Software\Classes\${EXT}\OpenWithProgids" "Gallery.Image" ""
  WriteRegStr HKLM "Software\Classes\Applications\Gallery.exe\SupportedTypes" "${EXT}" ""
  WriteRegStr HKLM "Software\Gallery\Capabilities\FileAssociations" "${EXT}" "Gallery.Image"
!macroend
!macro GalleryUnregExt EXT
  DeleteRegValue HKLM "Software\Classes\${EXT}\OpenWithProgids" "Gallery.Image"
  DeleteRegValue HKCU "Software\Classes\${EXT}\OpenWithProgids" "Gallery.Image"
  DeleteRegKey HKCU "Software\Classes\${EXT}\OpenWithList\Gallery.exe"
!macroend

!macro customInstall
  ; remove old per-user registrations (they would shadow the machine-wide ones with a stale path)
  DeleteRegKey HKCU "Software\Gallery"
  DeleteRegKey HKCU "Software\Classes\Gallery.Image"
  DeleteRegKey HKCU "Software\Classes\Applications\Gallery.exe"
  DeleteRegValue HKCU "Software\RegisteredApplications" "Gallery"
  WriteRegStr HKLM "Software\Classes\Gallery.Image" "" "Gallery Image"
  WriteRegStr HKLM "Software\Classes\Gallery.Image\DefaultIcon" "" "$INSTDIR\Gallery.exe,0"
  WriteRegStr HKLM "Software\Classes\Gallery.Image\shell\open\command" "" '"$INSTDIR\Gallery.exe" "%1"'
  WriteRegStr HKLM "Software\Classes\Applications\Gallery.exe" "FriendlyAppName" "Gallery"
  WriteRegStr HKLM "Software\Classes\Applications\Gallery.exe\DefaultIcon" "" "$INSTDIR\Gallery.exe,0"
  WriteRegStr HKLM "Software\Classes\Applications\Gallery.exe\shell\open\command" "" '"$INSTDIR\Gallery.exe" "%1"'
  WriteRegStr HKLM "Software\Gallery\Capabilities" "ApplicationName" "Gallery"
  WriteRegStr HKLM "Software\Gallery\Capabilities" "ApplicationDescription" "Fast, modern photo gallery and viewer"
  !insertmacro GalleryRegExt ".jpg"
  !insertmacro GalleryRegExt ".jpeg"
  !insertmacro GalleryRegExt ".jpe"
  !insertmacro GalleryRegExt ".jfif"
  !insertmacro GalleryRegExt ".png"
  !insertmacro GalleryRegExt ".apng"
  !insertmacro GalleryRegExt ".gif"
  !insertmacro GalleryRegExt ".webp"
  !insertmacro GalleryRegExt ".bmp"
  !insertmacro GalleryRegExt ".dib"
  !insertmacro GalleryRegExt ".ico"
  !insertmacro GalleryRegExt ".avif"
  !insertmacro GalleryRegExt ".svg"
  !insertmacro GalleryRegExt ".heic"
  !insertmacro GalleryRegExt ".heif"
  !insertmacro GalleryRegExt ".tif"
  !insertmacro GalleryRegExt ".tiff"
  WriteRegStr HKLM "Software\RegisteredApplications" "Gallery" "Software\Gallery\Capabilities"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
!macroend

!macro customUnInstall
  !insertmacro GalleryUnregExt ".jpg"
  !insertmacro GalleryUnregExt ".jpeg"
  !insertmacro GalleryUnregExt ".jpe"
  !insertmacro GalleryUnregExt ".jfif"
  !insertmacro GalleryUnregExt ".png"
  !insertmacro GalleryUnregExt ".apng"
  !insertmacro GalleryUnregExt ".gif"
  !insertmacro GalleryUnregExt ".webp"
  !insertmacro GalleryUnregExt ".bmp"
  !insertmacro GalleryUnregExt ".dib"
  !insertmacro GalleryUnregExt ".ico"
  !insertmacro GalleryUnregExt ".avif"
  !insertmacro GalleryUnregExt ".svg"
  !insertmacro GalleryUnregExt ".heic"
  !insertmacro GalleryUnregExt ".heif"
  !insertmacro GalleryUnregExt ".tif"
  !insertmacro GalleryUnregExt ".tiff"
  !insertmacro GalleryUnregExt ".cr2"
  !insertmacro GalleryUnregExt ".cr3"
  !insertmacro GalleryUnregExt ".nef"
  !insertmacro GalleryUnregExt ".arw"
  !insertmacro GalleryUnregExt ".dng"
  !insertmacro GalleryUnregExt ".orf"
  !insertmacro GalleryUnregExt ".rw2"
  !insertmacro GalleryUnregExt ".raf"
  !insertmacro GalleryUnregExt ".pef"
  !insertmacro GalleryUnregExt ".srw"
  DeleteRegValue HKLM "Software\RegisteredApplications" "Gallery"
  DeleteRegKey HKLM "Software\Gallery"
  DeleteRegKey HKLM "Software\Classes\Gallery.Image"
  DeleteRegKey HKLM "Software\Classes\Applications\Gallery.exe"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
!macroend
