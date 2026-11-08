Unicode true
!define APP "Gallery"
!define VER "2.0.0"
Name "${APP}"
OutFile "..\dist\Gallery-Setup-2.0.0.exe"
InstallDir "$PROGRAMFILES64\Gallery"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "Gallery ${VER}"
Icon "..\icon.ico"
UninstallIcon "..\icon.ico"
ShowInstDetails nevershow
ShowUninstDetails nevershow
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

!macro RegExt EXT
  WriteRegStr HKLM "Software\Classes\${EXT}\OpenWithProgids" "Gallery.Image" ""
  WriteRegStr HKLM "Software\Classes\Applications\Gallery.exe\SupportedTypes" "${EXT}" ""
  WriteRegStr HKLM "Software\Gallery\Capabilities\FileAssociations" "${EXT}" "Gallery.Image"
!macroend
!macro UnregExt EXT
  DeleteRegValue HKLM "Software\Classes\${EXT}\OpenWithProgids" "Gallery.Image"
!macroend
!define EXTS ".jpg .jpeg .jpe .jfif .png .apng .gif .webp .bmp .dib .ico .avif .svg .heic .heif .tif .tiff"

Section "Install"
  SetShellVarContext all
  SetRegView 32
  DeleteRegKey HKLM "SoftwareGallery"
  DeleteRegKey HKLM "SoftwareMicrosoftWindowsCurrentVersionUninstallGalleryWpf"
  DeleteRegValue HKLM "SoftwareRegisteredApplications" "Gallery"
  SetRegView 64
  ; replace the old Electron build if present
  IfFileExists "$PROGRAMFILES64\Gallery\Uninstall Gallery.exe" 0 +3
    ExecWait '"$PROGRAMFILES64\Gallery\Uninstall Gallery.exe" /S /allusers'
    Sleep 2500
  nsExec::Exec 'taskkill /F /IM Gallery.exe'
  SetOutPath "$INSTDIR"
  File "..\publish-sc\Gallery.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\Gallery.lnk" "$INSTDIR\Gallery.exe"
  CreateShortcut "$DESKTOP\Gallery.lnk" "$INSTDIR\Gallery.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GalleryWpf" "DisplayName" "Gallery"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GalleryWpf" "DisplayVersion" "${VER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GalleryWpf" "DisplayIcon" "$INSTDIR\Gallery.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GalleryWpf" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GalleryWpf" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GalleryWpf" "Publisher" "Gallery"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GalleryWpf" "NoModify" 1
  WriteRegStr HKLM "Software\Classes\Gallery.Image" "" "Gallery Image"
  WriteRegStr HKLM "Software\Classes\Gallery.Image\DefaultIcon" "" "$INSTDIR\Gallery.exe,0"
  WriteRegStr HKLM "Software\Classes\Gallery.Image\shell\open\command" "" '"$INSTDIR\Gallery.exe" "%1"'
  WriteRegStr HKLM "Software\Classes\Applications\Gallery.exe" "FriendlyAppName" "Gallery"
  WriteRegStr HKLM "Software\Classes\Applications\Gallery.exe\DefaultIcon" "" "$INSTDIR\Gallery.exe,0"
  WriteRegStr HKLM "Software\Classes\Applications\Gallery.exe\shell\open\command" "" '"$INSTDIR\Gallery.exe" "%1"'
  WriteRegStr HKLM "Software\Gallery\Capabilities" "ApplicationName" "Gallery"
  WriteRegStr HKLM "Software\Gallery\Capabilities" "ApplicationDescription" "Fast, modern photo gallery and viewer"
  !insertmacro RegExt ".jpg"
  !insertmacro RegExt ".jpeg"
  !insertmacro RegExt ".jpe"
  !insertmacro RegExt ".jfif"
  !insertmacro RegExt ".png"
  !insertmacro RegExt ".apng"
  !insertmacro RegExt ".gif"
  !insertmacro RegExt ".webp"
  !insertmacro RegExt ".bmp"
  !insertmacro RegExt ".dib"
  !insertmacro RegExt ".ico"
  !insertmacro RegExt ".avif"
  !insertmacro RegExt ".svg"
  !insertmacro RegExt ".heic"
  !insertmacro RegExt ".heif"
  !insertmacro RegExt ".tif"
  !insertmacro RegExt ".tiff"
  WriteRegStr HKLM "Software\RegisteredApplications" "Gallery" "Software\Gallery\Capabilities"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  SetRegView 64
  nsExec::Exec 'taskkill /F /IM Gallery.exe'
  Delete "$INSTDIR\Gallery.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\Gallery.lnk"
  Delete "$DESKTOP\Gallery.lnk"
  !insertmacro UnregExt ".jpg"
  !insertmacro UnregExt ".jpeg"
  !insertmacro UnregExt ".jpe"
  !insertmacro UnregExt ".jfif"
  !insertmacro UnregExt ".png"
  !insertmacro UnregExt ".apng"
  !insertmacro UnregExt ".gif"
  !insertmacro UnregExt ".webp"
  !insertmacro UnregExt ".bmp"
  !insertmacro UnregExt ".dib"
  !insertmacro UnregExt ".ico"
  !insertmacro UnregExt ".avif"
  !insertmacro UnregExt ".svg"
  !insertmacro UnregExt ".heic"
  !insertmacro UnregExt ".heif"
  !insertmacro UnregExt ".tif"
  !insertmacro UnregExt ".tiff"
  DeleteRegValue HKLM "Software\RegisteredApplications" "Gallery"
  DeleteRegKey HKLM "Software\Gallery"
  DeleteRegKey HKLM "Software\Classes\Gallery.Image"
  DeleteRegKey HKLM "Software\Classes\Applications\Gallery.exe"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GalleryWpf"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd
