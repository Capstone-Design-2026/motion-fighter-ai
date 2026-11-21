@echo off
setlocal
cd /d "%~dp0"
py -3.11 --version >nul 2>&1
if errorlevel 1 (
 echo Install Python 3.11 x64 with the Python launcher, then run this again.
 pause
 exit /b 1
)
if not exist ".venv\Scripts\python.exe" py -3.11 -m venv .venv
if errorlevel 1 goto failed
".venv\Scripts\python.exe" -m pip install --no-index --find-links=windows_wheels -r requirements-camera.txt
if errorlevel 1 goto failed
".venv\Scripts\python.exe" launch_demo.py check
if errorlevel 1 goto failed
echo Setup complete. Host: 01 + 02. Guest: 03 + 04.
pause
exit /b 0
:failed
echo Setup failed. Keep this message for diagnosis.
pause
exit /b 1
