@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Drag and drop a video file onto this batch file to test JitenMPV.
    echo.
    pause
    exit /b 1
)

set "ROOT=%~dp0"
set "PIPE=\\.\pipe\jiten-test-%RANDOM%"
set "LOG=%APPDATA%\jiten-mpv\debug.log"

where mpv >nul 2>&1
if errorlevel 1 (
    echo ERROR: mpv was not found on PATH.
    echo.
    pause
    exit /b 1
)

echo Building JitenMPV...
dotnet build "%ROOT%JitenMPV.sln" -c Debug -v q --nologo
if errorlevel 1 (
    echo.
    echo ERROR: build failed.
    pause
    exit /b 1
)

set "EXE="
for /f "delims=" %%F in ('dir /b /s "%ROOT%src\JitenMPV.App\bin\Debug\JitenMPV.App.exe" 2^>nul') do set "EXE=%%F"

if not defined EXE (
    echo ERROR: JitenMPV.App.exe not found under src\JitenMPV.App\bin\Debug.
    pause
    exit /b 1
)

echo.
echo Video:  %~1
echo Pipe:   %PIPE%
echo Plugin: %EXE%
echo Log:    %LOG%
echo.

REM jiten_external stops the Lua script from spawning the installed copy from %%APPDATA%%,
REM so mouse and keybind events go to the freshly built plugin started below.
start "" mpv --input-ipc-server=%PIPE% --script="%ROOT%scripts\jiten-mpv.lua" --script-opts=jiten_external=1 "%~1"

start "" "%EXE%" plugin %PIPE%

echo Streaming plugin log. Close mpv or press Ctrl+C to stop.
echo.
timeout /t 3 /nobreak >nul
powershell -NoProfile -Command "Get-Content -LiteralPath '%LOG%' -Wait -Tail 60"

endlocal
