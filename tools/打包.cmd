@echo off
chcp 65001 >nul
set PYTHONIOENCODING=utf-8
set PYTHONUTF8=1
python "%~dp0打包.py" %*
if %errorlevel% neq 0 (
  echo.
  echo PACKAGING FAILED, exit code %errorlevel%
)
echo.
pause
