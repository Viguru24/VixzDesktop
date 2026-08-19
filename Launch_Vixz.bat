@echo off
title Vixz Desktop Launcher
cd /d "%~dp0"

echo [Vixz Desktop] Starting application...
cd src\VixzDesktop
dotnet run
