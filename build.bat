@echo off
echo Building DeepEffort v2...
dotnet build DeepEffortV2.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build failed. Check errors above.
    pause
    exit /b 1
)
echo.
echo Done. DLL is at: bin\Release\net8.0\DeepEffortV2.dll
echo Copy it to your ATAS custom-indicators folder, then reload indicators in ATAS.
echo.
pause
