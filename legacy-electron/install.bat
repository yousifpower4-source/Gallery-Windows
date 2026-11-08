@echo off
cd /d "%~dp0"
echo Installing dependencies...
call npm install || (echo npm install failed - is Node.js installed? & pause & exit /b 1)
echo Creating Desktop and Start Menu shortcuts...
powershell -NoProfile -Command "$ws=New-Object -ComObject WScript.Shell; foreach($d in @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'))){ $s=$ws.CreateShortcut((Join-Path $d 'Gallery.lnk')); $s.TargetPath=(Resolve-Path 'node_modules\electron\dist\electron.exe').Path; $s.Arguments='\"'+(Get-Location).Path+'\"'; $s.WorkingDirectory=(Get-Location).Path; $s.IconLocation=(Resolve-Path 'build\icon.ico').Path; $s.Save() }"
echo Done. Launch "Gallery" from your Desktop or Start Menu.
pause
