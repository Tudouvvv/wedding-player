#!/bin/bash
# 婚礼影音播放器 · macOS 启动器（双击运行）
# 若双击提示「无法打开，因为它来自身份不明的开发者」：右键 → 打开；或先在终端执行：
#   chmod +x "启动播放器.command"
cd "$(dirname "$0")" || exit 1

echo "==================================================="
echo "  婚礼影音播放器"
echo "==================================================="
echo ""

if command -v python3 >/dev/null 2>&1; then
  echo "检测到 python3，用本地服务模式启动（可记住素材文件夹）。"
  echo "提示：歌单 / 扫码登录功能需要 pycryptodome（pip3 install pycryptodome，需联网）。"
  echo ""
  python3 server.py
else
  echo "未检测到 python3，改为直接用浏览器打开播放器页面。"
  echo "注意：此方式每次打开都要重新选择一次素材文件夹（照片 / 本地音乐 / 歌词都能正常播放）。"
  echo ""
  if [ -d "/Applications/Google Chrome.app" ]; then
    open -a "Google Chrome" wedding-player.html
  else
    open wedding-player.html
  fi
fi
