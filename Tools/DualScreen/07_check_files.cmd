@echo off
setlocal
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
 echo Run 00_setup_windows.cmd first.
 pause
 exit /b 1
)
".venv\Scripts\python.exe" "launch_demo.py" check
if errorlevel 1 pause
