@echo off
cd /d "%~dp0"
dotnet run --project src\OhMyHarness.App -f net10.0-desktop -c Release
pause
