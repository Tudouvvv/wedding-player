@echo off
cd /d "%~dp0"
if exist wedding-server.exe (
  echo.
  echo  婚礼影音播放器 本地服务启动中...
  echo  服务窗口需保持打开，关闭本窗口即停止服务。
  echo.
  wedding-server.exe
  echo.
  echo  服务已停止。按任意键关闭本窗口。
  pause >nul
  exit /b
)
where py >nul 2>nul
if %errorlevel%==0 (
  py -c "import sys" >nul 2>nul
  if %errorlevel%==0 (
    start "" py server.py
    exit /b
  )
)
where python >nul 2>nul
if %errorlevel%==0 (
  python -c "import sys" >nul 2>nul
  if %errorlevel%==0 (
    start "" python server.py
    exit /b
  )
)
where python3 >nul 2>nul
if %errorlevel%==0 (
  python3 -c "import sys" >nul 2>nul
  if %errorlevel%==0 (
    start "" python3 server.py
    exit /b
  )
)
echo.
echo  [错误] 未找到可用的启动方式。
echo  请安装 Python 3：https://www.python.org/downloads/ （勾选 Add Python to PATH），
echo  或直接双击 wedding-player.html 使用（歌单导入功能将受限）。
pause

