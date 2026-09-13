@echo off
setlocal
rem ============================================================
rem  StylesVN self-check (Windows)
rem    1) compile + content validation  (RunAll: exit 0 = ok, 2 = warnings)
rem    2) full-story self test           (SelfTest: exit 0 = ok, 1 = problems, 2 = exception)
rem  Usage : run  check.cmd  in the project root
rem  Note  : close the Unity editor first (batchmode cannot open a locked project)
rem  Player-side health check (32 items): Builds\Windows\Styles.exe -styles-selfcheck
rem  Chinese notes: see AGENTS.md / check.sh
rem ============================================================

set "UNITY=D:\unity\2022.3.62f3c1\Editor\Unity.exe"
set "PROJECT=%~dp0."
set "LOG1=%~dp0compile.log"
set "LOG2=%~dp0selftest.log"
set "UNITY_BASE=-batchmode -nographics -quit -noUpm"

if not exist "%UNITY%" (
  echo [ERROR] Unity.exe not found: %UNITY%
  exit /b 2
)

tasklist /FI "IMAGENAME eq Unity.exe" 2>nul | find /i "Unity.exe" >nul
if not errorlevel 1 (
  echo [ABORT] Unity editor is running. Close it first - batchmode cannot open a locked project.
  exit /b 3
)

echo [1/2] Compile + content validation ...
"%UNITY%" %UNITY_BASE% -projectPath "%PROJECT%" -executeMethod Styles.EditorTools.ProjectBootstrap.ValidateOnly -logFile "%LOG1%"
set "RC1=%ERRORLEVEL%"

findstr /C:"error CS" /C:"Scripts have compiler errors" /C:"Failed to compile" "%LOG1%" >nul
if not errorlevel 1 (
  echo ----------------------------------------
  echo [FAIL] Compile error^(s^). See compile.log:
  findstr /C:"error CS" "%LOG1%"
  exit /b 1
)
if not "%RC1%"=="0" (
  echo [FAIL] Content validation reported problems ^(exit %RC1%^).
  echo        See compile.log and ToolsOut\content_report.txt
  exit /b 1
)
echo       OK: compiled, content validation clean.

echo [2/2] Full-story self test ...
"%UNITY%" %UNITY_BASE% -projectPath "%PROJECT%" -executeMethod Styles.EditorTools.SelfTest.RunBatch -logFile "%LOG2%"
set "RC2=%ERRORLEVEL%"
if not "%RC2%"=="0" (
  echo [FAIL] Self test failed ^(exit %RC2%^). See selftest.log
  exit /b 1
)
echo       OK: story reached the ending.

echo.
echo All checks passed.
exit /b 0

