@echo off
chcp 65001 >nul
cd /d "%~dp0"
if exist "tools\ffmpeg\ffmpeg.exe" (
  echo ffmpeg 已存在，无需下载。
  pause
  exit /b 0
)
echo 正在下载 ffmpeg（国内镜像，约 27MB）...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; New-Item -ItemType Directory -Path 'tools\ffmpeg' -Force | Out-Null; Invoke-WebRequest -Uri 'https://registry.npmmirror.com/-/binary/ffmpeg-static/b5.0.1/win32-x64.gz' -OutFile 'tools\ffmpeg\ffmpeg.gz' -UseBasicParsing; $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path 'tools\ffmpeg\ffmpeg.gz')); $ms = New-Object System.IO.MemoryStream(,$bytes); $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionMode]::Decompress); $out = New-Object System.IO.MemoryStream; $gz.CopyTo($out); $gz.Close(); [System.IO.File]::WriteAllBytes((Resolve-Path 'tools\ffmpeg\ffmpeg.exe' -ErrorAction SilentlyContinue), $out.ToArray())"
if exist "tools\ffmpeg\ffmpeg.gz" del "tools\ffmpeg\ffmpeg.gz"
if exist "tools\ffmpeg\ffmpeg.exe" (
  echo 下载完成。
) else (
  echo [错误] 下载失败，请手动下载 ffmpeg 放到 tools\ffmpeg\ffmpeg.exe
)
pause
