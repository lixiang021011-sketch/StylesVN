#!/usr/bin/env bash
# ============================================================
#  StylesVN 一键自检（Git Bash / WSL 版，与 check.cmd 等价）
#    ① 编译 + 内容校验（RunAll：退出码 0=通过 2=有警告）
#    ② 自动通关自检（SelfTest.RunBatch：0=通过 1=有问题 2=异常）
#  用法：工程根目录执行  ./check.sh
#  注意：运行前关闭 Unity 编辑器
# ============================================================
set -u

UNITY="/d/unity/2022.3.62f3c1/Editor/Unity.exe"
PROJECT="$(cd "$(dirname "$0")" && pwd)"
LOG1="$PROJECT/compile.log"
LOG2="$PROJECT/selftest.log"

if [ ! -f "$UNITY" ]; then
    echo "[错误] 未找到 Unity.exe：$UNITY"
    exit 2
fi

if tasklist //FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi "Unity.exe"; then
    echo "[中止] 检测到 Unity 编辑器正在运行，请先关闭再执行本脚本"
    exit 3
fi

echo "[1/2] 编译 + 内容校验 ..."
"$UNITY" -batchmode -nographics -quit -noUpm -projectPath "$PROJECT" \
         -executeMethod Styles.EditorTools.ProjectBootstrap.ValidateOnly -logFile "$LOG1"
RC1=$?

# batchmode 编译失败时退出码也可能是 0，因此以日志内容为准
ERRORS=$(grep -E "error CS[0-9]+|Scripts have compiler errors|Failed to compile" "$LOG1" | head -50)
if [ -n "$ERRORS" ]; then
    echo "----------------------------------------"
    echo "编译失败（完整日志：compile.log）："
    echo "$ERRORS"
    exit 1
fi
if [ "$RC1" -ne 0 ]; then
    echo "[失败] 内容校验有问题（退出码 $RC1），详见 compile.log 与 ToolsOut/content_report.txt"
    exit 1
fi
echo "      OK：编译通过，内容校验干净。"

echo "[2/2] 自动通关自检 ..."
"$UNITY" -batchmode -nographics -quit -noUpm -projectPath "$PROJECT" \
         -executeMethod Styles.EditorTools.SelfTest.RunBatch -logFile "$LOG2"
RC2=$?
if [ "$RC2" -ne 0 ]; then
    echo "[失败] 自动通关自检未通过（退出码 $RC2），详见 selftest.log"
    exit 1
fi
echo "      OK：剧本走到结局。"

echo
echo 全部通过。
exit 0

