@echo off
REM Build script for StagingBlocker
REM Usage: build.bat [Configuration] [Platform]
REM        build.bat Release x64   (default)

setlocal enabledelayedexpansion

set CONFIG=%1
set PLATFORM=%2

if "%CONFIG%"=="" set CONFIG=Release
if "%PLATFORM%"=="" set PLATFORM=x64

echo Building StagingBlocker  [%CONFIG% | %PLATFORM%] ...

dotnet build "..\StagingBlocker.sln" -c %CONFIG% -f net472 || (
    echo.
    echo [ERROR] Build failed!
    pause
    exit /b 1
)

echo.
echo Build complete.
echo Output DLL: GameData\StagingBlocker\Plugins\StagingBlocker.dll
echo.
pause
