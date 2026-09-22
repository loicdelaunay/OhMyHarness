@echo off
cd /d "%~dp0"
dotnet run --project src\OhMyHarness.App -f net10.0-windows10.0.19041.0 -c Release
pause
