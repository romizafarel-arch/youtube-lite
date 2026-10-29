@echo off
title Pasang uBlock Origin di YouTube Lite
echo Membuka halaman uBlock Origin untuk profil YouTube Lite...
start "" "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --user-data-dir="%LOCALAPPDATA%\YouTubeLite" "https://microsoftedge.microsoft.com/addons/detail/ublock-origin/odfafepnkmbhccpbejgmiehpchacaeak"
echo Silakan klik tombol 'Dapatkan' / 'Get' di halaman yang terbuka.
timeout /t 5 >nul
exit
