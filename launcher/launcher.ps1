#Requires -Version 5.1
# Instalador do launcher do ModSchedule1. O launcher agora é um .exe (sem janela de cmd, fica aberto com o jogo).
# Este script só é usado por quem ainda abre o ModSchedule1.bat: baixa o .exe, cria o atalho na Área de Trabalho e abre.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Windows.Forms

$Root = Join-Path $env:LOCALAPPDATA 'ModSchedule1'
$Exe = Join-Path $Root 'ModSchedule1.exe'
$Url = 'https://raw.githubusercontent.com/GabrielFernandes05/ModSchedule1/master/release/launcher/ModSchedule1.exe'

try {
    New-Item -ItemType Directory -Force $Root | Out-Null
    if (-not (Test-Path $Exe)) {   # depois de instalado, o próprio .exe se atualiza
        Invoke-WebRequest -UseBasicParsing $Url -OutFile "$Exe.part"
        Move-Item -Force "$Exe.part" $Exe
    }
    # GetFolderPath acha a Área de Trabalho real (inclusive quando está no OneDrive)
    $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ModSchedule1.lnk'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
    $shortcut.TargetPath = $Exe
    $shortcut.WorkingDirectory = $Root
    $shortcut.IconLocation = "$Exe,0"
    $shortcut.Description = 'ModSchedule1 - launcher do Schedule I com mods'
    $shortcut.Save()
    Start-Process $Exe
} catch {
    [System.Windows.Forms.MessageBox]::Show("Falha ao instalar o launcher: $($_.Exception.Message)", 'ModSchedule1') | Out-Null
}
