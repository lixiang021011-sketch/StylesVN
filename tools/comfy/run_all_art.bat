@echo off
chcp 65001 >nul
cd /d "%~dp0..\.."
set PYTHONIOENCODING=utf-8
echo ==== 立绘 (13) ==== >> ToolsOut\art_batch.log
python tools\comfy\pipeline.py --kind char --all --cfg 2.4 --steps 9 >> ToolsOut\art_batch.log 2>&1
echo ==== 场景 (12) ==== >> ToolsOut\art_batch.log
python tools\comfy\pipeline.py --kind bg --all >> ToolsOut\art_batch.log 2>&1
echo ==== 标题图 (1) ==== >> ToolsOut\art_batch.log
python tools\comfy\pipeline.py --kind title >> ToolsOut\art_batch.log 2>&1
echo ==== DONE ==== >> ToolsOut\art_batch.log
