@echo off
where pwsh >nul 2>nul || goto erro
where docker >nul 2>nul || goto erro
pwsh -NoProfile -File "%~dp0Invoke-Homologation.ps1" -Action Stop
if errorlevel 1 goto erro
exit /b 0
:erro
echo Nao foi possivel parar o IndicA2. Verifique PowerShell 7, Docker e o perfil local.
pause
exit /b 1
