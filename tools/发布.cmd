@echo off
chcp 65001 >nul
set PYTHONIOENCODING=utf-8
set PYTHONUTF8=1
python "%~dp0打包.py" check
if errorlevel 1 goto cannot
echo.
set /p ans=[PUBLISH] type yes to go on, anything else cancels:
if /i not "%ans%"=="yes" goto cancel
python "%~dp0打包.py" --publish %1
if errorlevel 1 (
  echo.
  echo PUBLISH FAILED
) else (
  echo.
  echo Done. Zip is in publish folder.
)
goto end
:cannot
echo.
echo Not publishable. Write this version's changes into update.md first.
goto end
:cancel
echo.
echo Cancelled.
:end
echo.
pause
