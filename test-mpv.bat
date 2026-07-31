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

REM JITEN_MPV_EXE redirects the Lua's spawn to the build above instead of the installed copy in
REM %%APPDATA%%. Letting the script spawn it, rather than starting the plugin here, is what keeps
REM plugin_autostart and plugin_start_key under test; starting it by hand connects over IPC
REM regardless of either setting and makes them look broken.
set "JITEN_MPV_EXE=%EXE%"
REM --load-scripts=no keeps the installed copy in %%APPDATA%%\mpv\scripts out of the run. Loaded
REM alongside the repo copy it is a second script instance, spawning a second plugin process on the
REM same pipe, and every broadcast event is then acted on twice.
start "" mpv --load-scripts=no --input-ipc-server=%PIPE% --script="%ROOT%scripts\jiten-mpv.lua" "%~1"

echo Streaming plugin log. Close mpv or press Ctrl+C to stop.
echo (autostart off? the log stays on the previous run until you press the start key.)
echo.
timeout /t 3 /nobreak >nul
powershell -NoProfile -Command "Get-Content -LiteralPath '%LOG%' -Wait -Tail 60"

endlocal
