@echo off
rem ModSchedule1 - baixa o launcher mais recente do GitHub e abre a janela do mod manager.
setlocal
set "REPO=GabrielFernandes05/ModSchedule1"
set "ROOT=%LOCALAPPDATA%\ModSchedule1"
if not exist "%ROOT%" mkdir "%ROOT%"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference=SilentlyContinue; [Net.ServicePointManager]::SecurityProtocol=Tls12; try { Invoke-WebRequest -UseBasicParsing https://raw.githubusercontent.com/%REPO%/master/launcher/launcher.ps1 -OutFile %ROOT%\launcher.ps1.new; Move-Item -Force %ROOT%\launcher.ps1.new %ROOT%\launcher.ps1 } catch { Write-Host Sem internet, usando o launcher ja baixado. }"

if not exist "%ROOT%\launcher.ps1" (
  echo Nao foi possivel baixar o launcher. Verifique sua internet.
  pause
  exit /b 1
)
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%ROOT%\launcher.ps1"
