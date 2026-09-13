@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo.
echo  ============================================
echo   Video Converter  (H.265 to H.264)
echo   Fix: video plays audio but no picture
echo  ============================================
echo.

set "FFMPEG=%~dp0tools\ffmpeg\ffmpeg.exe"
if not exist "%FFMPEG%" (
  echo [ERROR] ffmpeg not found.
  echo Run 下载ffmpeg.bat first, or put ffmpeg.exe in tools\ffmpeg\
  pause
  exit /b 1
)

set "TARGET="
if not "%~1"=="" (
  if exist "%~1\*" set "TARGET=%~1"
)
if "%TARGET%"=="" set "TARGET=%~dp0input"
if not exist "%TARGET%" set "TARGET=%~dp0."

echo Scanning: %TARGET%
echo.

set "COUNT=0"
set "SKIP=0"
set "FAIL=0"

for /r "%TARGET%" %%f in (*.mp4 *.mov *.m4v *.avi *.mkv *.flv *.wmv *.ts *.3gp *.webm) do (
  echo %%~nxf | findstr /i ".h264" >nul
  if "!errorlevel!"=="0" (
    set /a SKIP+=1
    echo   [skip] %%~nxf
  ) else (
    echo   [convert] %%~nxf
    "%FFMPEG%" -y -hide_banner -loglevel error -i "%%f" -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart "%%~dpnf.h264.mp4"
    if "!errorlevel!"=="0" (
      set /a COUNT+=1
      echo   [OK] %%~nf.h264.mp4
    ) else (
      set /a FAIL+=1
      echo   [FAIL] %%~nxf
    )
  )
)

echo.
echo  ============================================
echo   Done.
if "!COUNT!" GTR "0" echo    converted: !COUNT!
if "!SKIP!" GTR "0" echo    skipped: !SKIP!
if "!FAIL!" GTR "0" echo    failed: !FAIL!
echo.
echo   New files: .h264.mp4  (originals kept)
echo  ============================================
pause
