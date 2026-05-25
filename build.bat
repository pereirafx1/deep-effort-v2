@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: DeepEffort v2 — one-click build script
:: Double-click this file (or run from a command prompt) to compile the DLL.
:: The finished DLL is placed in .\output\DeepEffortV2.dll
:: ─────────────────────────────────────────────────────────────────────────────

setlocal

echo.
echo  Building DeepEffort v2...
echo.

:: Restore NuGet packages and build in Release configuration.
dotnet build DeepEffortV2.csproj -c Release -o ./output

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo  *** BUILD FAILED — see errors above ***
    echo.
    echo  Common fixes:
    echo    1. Install .NET 8 SDK  ^(see README.txt for download link^)
    echo    2. Check internet / NuGet access for OFT.Indicators package
    echo    3. Or uncomment the local-DLL fallback block in DeepEffortV2.csproj
    echo       and set ATASDir to your ATAS installation folder.
    echo.
    pause
    exit /b 1
)

echo.
echo  Build succeeded!
echo  DLL is at:  %~dp0output\DeepEffortV2.dll
echo.
echo  Copy DeepEffortV2.dll to your ATAS custom-indicators folder
echo  ^(see README.txt for the exact path^), then restart ATAS or
echo  use the in-app "Reload indicators" option.
echo.

pause
endlocal
