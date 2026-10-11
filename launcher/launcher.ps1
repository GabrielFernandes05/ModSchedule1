#Requires -Version 5.1
# ModSchedule1 launcher: instala/atualiza MelonLoader + ModSchedule1 no Schedule I e abre o jogo.
# Baixado do GitHub pelo ModSchedule1.bat a cada execução, então mudanças aqui chegam a todos sozinhas.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # a barra de progresso deixa o Invoke-WebRequest 10x mais lento no PS 5.1
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$Repo         = 'GabrielFernandes05/ModSchedule1'
$Branch       = 'master'
$MelonVersion = '0.7.3'
$SteamAppId   = '3164500'
$GameExe      = 'Schedule I.exe'
$ModDll       = 'ModSchedule1.dll'

$Root      = Join-Path $env:LOCALAPPDATA 'ModSchedule1'
$Cache     = Join-Path $Root 'release'
$StatePath = Join-Path $Root 'state.json'
$LogPath   = Join-Path $Root 'launcher.log'

# Tudo que um loader/mod de terceiros costuma deixar na pasta do jogo. Vai para backup na instalação limpa, nunca é apagado.
$ForeignItems = 'MelonLoader', 'Mods', 'Plugins', 'UserLibs', 'UserData', 'BepInEx',
                'version.dll', 'winhttp.dll', 'dobby.dll', 'doorstop_config.ini', '.doorstop_version', 'NOTICE.txt'

New-Item -ItemType Directory -Force $Root | Out-Null
$script:LogBox = $null

function Log([string]$msg) {
    $line = '{0:HH:mm:ss}  {1}' -f (Get-Date), $msg
    Add-Content -Path $LogPath -Value $line -Encoding UTF8
    if ($script:LogBox) {
        $script:LogBox.AppendText($line + [Environment]::NewLine)
        [System.Windows.Forms.Application]::DoEvents()
    }
}

# ---------- estado ----------

function Load-State {
    $s = @{ gameDir = $null; flavor = $null; sha = $null }
    if (Test-Path $StatePath) {
        $j = Get-Content $StatePath -Raw | ConvertFrom-Json
        foreach ($k in @($s.Keys)) { if ($j.$k) { $s[$k] = $j.$k } }
    }
    $s
}
function Save-State { $State | ConvertTo-Json | Set-Content $StatePath -Encoding UTF8 }

# ---------- jogo ----------

function Find-GameDir {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return $null }
    $libs = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libs += Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' |
                 ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($lib in $libs) {
        $dir = Join-Path $lib 'steamapps\common\Schedule I'
        if (Test-Path (Join-Path $dir $GameExe)) { return $dir }
    }
    $null
}

function Ask-GameDir {
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Selecione a pasta do Schedule I (a que contém '$GameExe')"
    while ($dlg.ShowDialog() -eq 'OK') {
        if (Test-Path (Join-Path $dlg.SelectedPath $GameExe)) { return $dlg.SelectedPath }
        [System.Windows.Forms.MessageBox]::Show("Não encontrei '$GameExe' nessa pasta.", 'ModSchedule1') | Out-Null
    }
    $null
}

# Normal (IL2CPP) traz GameAssembly.dll; Mono (beta "alternate") traz Managed\Assembly-CSharp.dll. Se sobrar arquivo
# de uma troca de branch e existirem os dois, vale o mais novo (o que a Steam acabou de baixar).
function Detect-Flavor([string]$dir) {
    if (-not $dir) { return $null }
    $il2cpp = Get-Item (Join-Path $dir 'GameAssembly.dll') -ErrorAction SilentlyContinue
    $mono = Get-Item (Join-Path $dir 'Schedule I_Data\Managed\Assembly-CSharp.dll') -ErrorAction SilentlyContinue
    if ($il2cpp -and $mono) { if ($mono.LastWriteTime -gt $il2cpp.LastWriteTime) { return 'mono' } else { return 'il2cpp' } }
    if ($il2cpp) { return 'il2cpp' }
    if ($mono) { return 'mono' }
    $null
}

$MonoHelp = "O Schedule I está na versão Mono (beta 'alternate'). O ModSchedule1 só funciona na versão Normal.`n`n" +
            "Na Steam: clique com o botão direito em Schedule I > Propriedades > Betas > selecione 'Nenhum'.`n" +
            "Espere o download terminar e clique em 'Verificar de novo' no launcher."

# Bloqueia o jogo se a versão não for a Normal. Chamado na sincronização e de novo ao clicar em Iniciar.
function Test-GameVersion {
    $detected = Detect-Flavor $State.gameDir
    $State.flavor = 'il2cpp'   # único build publicado; Mono fica para depois
    if ($detected -eq 'il2cpp') { return $true }
    if ($detected -eq 'mono') {
        Log 'BLOQUEADO: jogo na versão Mono. Troque para a versão Normal na Steam.'
        [System.Windows.Forms.MessageBox]::Show($MonoHelp, 'ModSchedule1 - versão errada do jogo', 'OK', 'Warning') | Out-Null
    } else {
        Log 'BLOQUEADO: não consegui identificar a versão do jogo (GameAssembly.dll ausente).'
        [System.Windows.Forms.MessageBox]::Show("Não encontrei os arquivos da versão Normal do jogo.`n`nNa Steam: Propriedades > Arquivos instalados > Verificar integridade.", 'ModSchedule1', 'OK', 'Warning') | Out-Null
    }
    $false
}

# ---------- backup ----------

$script:BackupDir = $null
function Backup-Item([string]$path) {
    if (-not $script:BackupDir) {
        $script:BackupDir = Join-Path $State.gameDir ('_ModSchedule1_backup\{0:yyyyMMdd-HHmmss}' -f (Get-Date))
    }
    $rel = $path.Substring($State.gameDir.Length).TrimStart('\')
    $dest = Join-Path $script:BackupDir $rel
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Move-Item -LiteralPath $path -Destination $dest -Force
    Log "  backup: $rel"
}

# ---------- release do GitHub ----------

function Get-RepoFile([string]$sha, [string]$relPath) {
    $dest = Join-Path $Cache ($relPath -replace '^release/', '' -replace '/', '\')
    if (Test-Path $dest) { return $dest }
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/$Repo/$sha/$relPath" -OutFile "$dest.part"
    Move-Item "$dest.part" $dest -Force
    Log "  baixado: $relPath"
    $dest
}

function Update-Release {
    try {
        $sha = (Invoke-RestMethod "https://api.github.com/repos/$Repo/commits/$Branch" -Headers @{ 'User-Agent' = 'ModSchedule1' }).sha
    } catch {
        Log "AVISO: sem acesso ao GitHub ($($_.Exception.Message)). Usando a versão já baixada."
        return
    }
    if ($sha -ne $State.sha) {
        Log "Nova versão no GitHub: $($sha.Substring(0,7))"
        if (Test-Path $Cache) { Remove-Item $Cache -Recurse -Force }   # cache nosso, sempre rebaixável
        $State.sha = $sha
    } else {
        Log "ModSchedule1 já está na última versão ($($sha.Substring(0,7)))"
    }
    Get-RepoFile $sha 'release/features.json' | Out-Null
    try { Get-RepoFile $sha "release/$($State.flavor)/$ModDll" | Out-Null }
    catch { Log "AVISO: ainda não há $ModDll publicado para a versão '$($State.flavor)'." }
    Save-State
}

# ---------- MelonLoader ----------

function MelonMarker { Join-Path $State.gameDir 'MelonLoader\modschedule1-melon.txt' }

function Test-Melon {
    $m = MelonMarker
    (Test-Path $m) -and ((Get-Content $m -Raw).Trim() -eq $MelonVersion) -and (Test-Path (Join-Path $State.gameDir 'version.dll'))
}

function Install-Melon {
    $zip = Join-Path $Root "MelonLoader-$MelonVersion.x64.zip"
    if (-not (Test-Path $zip)) {
        Log "Baixando MelonLoader $MelonVersion..."
        Invoke-WebRequest -UseBasicParsing "https://github.com/LavaGang/MelonLoader/releases/download/v$MelonVersion/MelonLoader.x64.zip" -OutFile "$zip.part"
        Move-Item "$zip.part" $zip -Force
    }
    $old = Join-Path $State.gameDir 'MelonLoader'
    if (Test-Path $old) { Backup-Item $old }   # não mistura arquivos de versões diferentes do loader
    Expand-Archive $zip -DestinationPath $State.gameDir -Force
    Set-Content (MelonMarker) $MelonVersion
    Write-LoaderConfig
    Log "MelonLoader $MelonVersion instalado. A 1a abertura do jogo demora alguns minutos (o loader gera os assemblies)."
}

# Esconde o console e a splash do MelonLoader: para quem joga, o loader fica invisível.
function Write-LoaderConfig {
    $dir = Join-Path $State.gameDir 'UserData'
    New-Item -ItemType Directory -Force $dir | Out-Null
    @'
[loader]
debug_mode = false
disable_start_screen = true

[console]
hide_console = true
hide_warnings = false

[logs]
max_logs = 10
'@ | Set-Content (Join-Path $dir 'Loader.cfg') -Encoding UTF8
}

# ---------- mods ----------

function Sync-Mods {
    foreach ($folder in 'Mods', 'Plugins') {
        $dir = Join-Path $State.gameDir $folder
        New-Item -ItemType Directory -Force $dir | Out-Null
        Get-ChildItem $dir | Where-Object { $_.Name -ne $ModDll } | ForEach-Object {
            Log "Mod de fora do ModSchedule1 encontrado: $($_.Name)"
            Backup-Item $_.FullName
        }
    }
    $src = Join-Path $Cache "$($State.flavor)\$ModDll"
    $dst = Join-Path $State.gameDir "Mods\$ModDll"
    if (-not (Test-Path $src)) {
        Log "AVISO: $ModDll indisponível; o jogo vai abrir só com o MelonLoader (necessário para gerar os assemblies de dev)."
        return
    }
    if (-not (Test-Path $dst) -or (Get-FileHash $src).Hash -ne (Get-FileHash $dst).Hash) {
        Copy-Item $src $dst -Force
        Log "$ModDll instalado na pasta Mods"
    }
}

function Clean-Install {
    Log 'Instalação limpa: movendo loaders e mods existentes para backup...'
    foreach ($name in $ForeignItems) {
        $p = Join-Path $State.gameDir $name
        if (Test-Path $p) { Backup-Item $p }
    }
    Install-Melon
    Sync-Mods
    if ($script:BackupDir) { Log "Backup em: $script:BackupDir" }
}

# Lê o Enabled de cada feature no cfg que o mod grava (UserData\ModSchedule1.cfg, TOML do MelonPreferences).
function Read-FeatureStates {
    $states = @{}
    $cfg = Join-Path $State.gameDir 'UserData\ModSchedule1.cfg'
    if (-not (Test-Path $cfg)) { return $states }
    $section = $null
    foreach ($line in Get-Content $cfg) {
        if ($line -match '^\s*\[(.+)\]\s*$') { $section = $Matches[1] -replace '^ModSchedule1_', '' }
        elseif ($section -and $line -match '^\s*Enabled\s*=\s*(true|false)') { $states[$section] = $Matches[1] -eq 'true' }
    }
    $states
}

# ---------- janela ----------

$State = Load-State

$form = New-Object System.Windows.Forms.Form
$form.Text = 'ModSchedule1'
$form.Size = New-Object System.Drawing.Size(820, 640)
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

$title = New-Object System.Windows.Forms.Label
$title.Font = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
$title.Location = '12,10'; $title.Size = '780,34'
$title.Text = 'ModSchedule1'
$form.Controls.Add($title)

$gameLabel = New-Object System.Windows.Forms.Label
$gameLabel.Location = '14,50'; $gameLabel.Size = '640,20'
$form.Controls.Add($gameLabel)

$changeDir = New-Object System.Windows.Forms.Button
$changeDir.Text = 'Alterar pasta...'; $changeDir.Location = '670,46'; $changeDir.Size = '120,26'
$form.Controls.Add($changeDir)

$flavorLabel = New-Object System.Windows.Forms.Label
$flavorLabel.Text = 'Versão do jogo:'; $flavorLabel.Location = '14,82'; $flavorLabel.Size = '100,20'
$form.Controls.Add($flavorLabel)

$flavorValue = New-Object System.Windows.Forms.Label
$flavorValue.Location = '116,82'; $flavorValue.Size = '420,20'
$flavorValue.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($flavorValue)

$recheck = New-Object System.Windows.Forms.Button
$recheck.Text = 'Verificar de novo'; $recheck.Location = '670,78'; $recheck.Size = '120,26'
$form.Controls.Add($recheck)

$list = New-Object System.Windows.Forms.ListView
$list.View = 'Details'; $list.FullRowSelect = $true; $list.Location = '12,112'; $list.Size = '780,250'
[void]$list.Columns.Add('Mod', 200)
[void]$list.Columns.Add('Status', 90)
[void]$list.Columns.Add('Descrição', 470)
$form.Controls.Add($list)

$script:LogBox = New-Object System.Windows.Forms.TextBox
$LogBox.Multiline = $true; $LogBox.ReadOnly = $true; $LogBox.ScrollBars = 'Vertical'
$LogBox.Location = '12,370'; $LogBox.Size = '780,170'
$LogBox.Font = New-Object System.Drawing.Font('Consolas', 8.5)
$form.Controls.Add($LogBox)

$start = New-Object System.Windows.Forms.Button
$start.Text = 'Iniciar jogo'; $start.Location = '12,550'; $start.Size = '200,40'; $start.Enabled = $false
$start.Font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($start)

$clean = New-Object System.Windows.Forms.Button
$clean.Text = 'Instalação limpa'; $clean.Location = '224,550'; $clean.Size = '140,40'; $clean.Enabled = $false
$form.Controls.Add($clean)

$logs = New-Object System.Windows.Forms.Button
$logs.Text = 'Log do jogo'; $logs.Location = '376,550'; $logs.Size = '120,40'
$form.Controls.Add($logs)

$folder = New-Object System.Windows.Forms.Button
$folder.Text = 'Pasta do jogo'; $folder.Location = '508,550'; $folder.Size = '120,40'
$form.Controls.Add($folder)

function Refresh-View {
    $gameLabel.Text = "Jogo: $($State.gameDir)"
    switch (Detect-Flavor $State.gameDir) {
        'il2cpp' { $flavorValue.Text = 'Normal (IL2CPP) - OK'; $flavorValue.ForeColor = [System.Drawing.Color]::DarkGreen }
        'mono'   { $flavorValue.Text = 'Mono (beta alternate) - NÃO SUPORTADA, troque para Normal'; $flavorValue.ForeColor = [System.Drawing.Color]::Firebrick }
        default  { $flavorValue.Text = 'não identificada'; $flavorValue.ForeColor = [System.Drawing.Color]::Firebrick }
    }
    $list.Items.Clear()
    $featuresPath = Join-Path $Cache 'features.json'
    if (-not (Test-Path $featuresPath)) { return }
    $manifest = Get-Content $featuresPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sha = if ($State.sha) { $State.sha.Substring(0, 7) } else { '?' }
    $title.Text = "ModSchedule1  v$($manifest.version)  ($sha)  -  MelonLoader $MelonVersion"
    $enabled = Read-FeatureStates
    foreach ($f in $manifest.features) {
        $on = if ($enabled.ContainsKey($f.id)) { $enabled[$f.id] } else { [bool]$f.default }
        $item = New-Object System.Windows.Forms.ListViewItem($f.name)
        [void]$item.SubItems.Add($(if ($on) { 'Ativo' } else { 'Desativado' }))
        [void]$item.SubItems.Add($f.description)
        if (-not $on) { $item.ForeColor = [System.Drawing.Color]::Gray }
        [void]$list.Items.Add($item)
    }
}

function Run-Sync([bool]$cleanInstall) {
    $start.Enabled = $false; $clean.Enabled = $false
    try {
        if (-not $State.gameDir -or -not (Test-Path (Join-Path $State.gameDir $GameExe))) {
            $State.gameDir = Find-GameDir
            if ($State.gameDir) { Log "Jogo encontrado pela Steam: $($State.gameDir)" }
            else { $State.gameDir = Ask-GameDir }
            if (-not $State.gameDir) { Log 'Pasta do jogo não informada.'; return }
        }
        Save-State
        Refresh-View
        if (-not (Test-GameVersion)) { return }

        Update-Release
        if ($cleanInstall -or -not (Test-Path (MelonMarker))) { Clean-Install }
        else {
            if (-not (Test-Melon)) { Install-Melon }
            Sync-Mods
        }
        Log 'Tudo pronto.'
        $start.Enabled = $true
    } catch {
        Log "ERRO: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show("Erro: $($_.Exception.Message)`n`nDetalhes em $LogPath", 'ModSchedule1') | Out-Null
    } finally {
        $clean.Enabled = [bool]$State.gameDir
        Refresh-View
    }
}

$start.Add_Click({
    if (-not (Test-GameVersion)) { $start.Enabled = $false; Refresh-View; return }   # trocou de branch com o launcher aberto
    Log 'Abrindo o jogo pela Steam...'
    Start-Process "steam://rungameid/$SteamAppId"
    $form.Close()
})
$clean.Add_Click({
    $ok = [System.Windows.Forms.MessageBox]::Show(
        "Todos os loaders, mods e configurações na pasta do jogo serão movidos para _ModSchedule1_backup e o ModSchedule1 será reinstalado do zero. Continuar?",
        'Instalação limpa', 'YesNo')
    if ($ok -eq 'Yes') { $script:BackupDir = $null; Run-Sync $true }
})
$changeDir.Add_Click({
    $dir = Ask-GameDir
    if ($dir) { $State.gameDir = $dir; Save-State; Run-Sync $false }
})
$recheck.Add_Click({ Run-Sync $false })
$logs.Add_Click({
    $p = Join-Path $State.gameDir 'MelonLoader\Latest.log'
    if (Test-Path $p) { Start-Process notepad.exe $p } else { Log 'Ainda não há log do jogo (abra o jogo uma vez).' }
})
$folder.Add_Click({ if ($State.gameDir) { Start-Process explorer.exe $State.gameDir } })

$form.Add_Shown({ Log "Launcher iniciado. Log: $LogPath"; Run-Sync $false })
[void]$form.ShowDialog()
