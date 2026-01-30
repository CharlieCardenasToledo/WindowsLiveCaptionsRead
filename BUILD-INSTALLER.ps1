# ============================================================================
# BUILD-INSTALLER.ps1
# Construye el instalador MSI para English Learning Assistant
# ============================================================================

param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

# Colores
function Write-Step { param([string]$msg) Write-Host "`n[PASO] $msg" -ForegroundColor Cyan }
function Write-Success { param([string]$msg) Write-Host "[✓] $msg" -ForegroundColor Green }
function Write-Error { param([string]$msg) Write-Host "[✗] $msg" -ForegroundColor Red }

Clear-Host
Write-Host @"
╔════════════════════════════════════════════════════════════════╗
║                                                                ║
║   English Learning Assistant - Constructor de Instalador      ║
║                                                                ║
╚════════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

$projectRoot = $PSScriptRoot
$publishDir = Join-Path $projectRoot "publish"
$installerDir = Join-Path $projectRoot "installer"
$outputDir = Join-Path $projectRoot "output"

# ============================================================================
# PASO 1: Limpiar directorios anteriores
# ============================================================================
Write-Step "Limpiando directorios de compilación..."

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $installerDir) { Remove-Item $installerDir -Recurse -Force }
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Success "Directorios limpiados"

# ============================================================================
# PASO 2: Compilar proyecto
# ============================================================================
if (-not $SkipBuild) {
    Write-Step "Compilando proyecto en modo $Configuration..."
    
    try {
        dotnet build --configuration $Configuration
        Write-Success "Proyecto compilado correctamente"
    }
    catch {
        Write-Error "Error al compilar: $_"
        exit 1
    }
}

# ============================================================================
# PASO 3: Publicar como ejecutable autónomo
# ============================================================================
if (-not $SkipPublish) {
    Write-Step "Publicando aplicación como ejecutable autónomo..."
    
    try {
        dotnet publish `
            --configuration $Configuration `
            --runtime win-x64 `
            --self-contained true `
            --output $publishDir `
            /p:PublishSingleFile=true `
            /p:IncludeNativeLibrariesForSelfExtract=true `
            /p:EnableCompressionInSingleFile=true `
            /p:PublishReadyToRun=true
        
        Write-Success "Aplicación publicada en: $publishDir"
    }
    catch {
        Write-Error "Error al publicar: $_"
        exit 1
    }
}

# ============================================================================
# PASO 4: Descargar Ollama installer
# ============================================================================
Write-Step "Descargando Ollama installer..."

$ollamaUrl = "https://ollama.com/download/OllamaSetup.exe"
$ollamaInstaller = Join-Path $installerDir "OllamaSetup.exe"

try {
    Invoke-WebRequest -Uri $ollamaUrl -OutFile $ollamaInstaller -UseBasicParsing
    Write-Success "Ollama installer descargado"
}
catch {
    Write-Error "Error al descargar Ollama: $_"
    Write-Host "Continuando sin Ollama (deberá instalarse manualmente)" -ForegroundColor Yellow
}

# ============================================================================
# PASO 5: Crear estructura del instalador
# ============================================================================
Write-Step "Creando estructura del instalador..."

# Copiar ejecutable y archivos necesarios
Copy-Item -Path (Join-Path $publishDir "EnglishLearningAssistant.exe") -Destination $installerDir
Copy-Item -Path (Join-Path $projectRoot "README.md") -Destination $installerDir
Copy-Item -Path (Join-Path $projectRoot "README.es.md") -Destination $installerDir
Copy-Item -Path (Join-Path $projectRoot "LANZAR_MODO_EXAMEN.bat") -Destination $installerDir -ErrorAction SilentlyContinue

Write-Success "Estructura creada"

# ============================================================================
# PASO 6: Crear script de instalación
# ============================================================================
Write-Step "Creando script de instalación..."

$installScript = @'
@echo off
title English Learning Assistant - Instalador
color 0B

echo ========================================
echo  English Learning Assistant
echo  Instalador v1.0
echo ========================================
echo.

REM Verificar permisos de administrador
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [!] Este instalador requiere permisos de administrador.
    echo [!] Reiniciando con permisos elevados...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo [1/4] Creando directorio de instalacion...
set "INSTALL_DIR=%ProgramFiles%\EnglishLearningAssistant"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

echo [2/4] Copiando archivos...
copy /Y "EnglishLearningAssistant.exe" "%INSTALL_DIR%\" >nul
copy /Y "README.md" "%INSTALL_DIR%\" >nul 2>&1
copy /Y "README.es.md" "%INSTALL_DIR%\" >nul 2>&1
copy /Y "LANZAR_MODO_EXAMEN.bat" "%INSTALL_DIR%\" >nul 2>&1

echo [3/4] Creando acceso directo en el escritorio...
powershell -Command "$WS = New-Object -ComObject WScript.Shell; $SC = $WS.CreateShortcut('%USERPROFILE%\Desktop\English Learning Assistant.lnk'); $SC.TargetPath = '%INSTALL_DIR%\EnglishLearningAssistant.exe'; $SC.WorkingDirectory = '%INSTALL_DIR%'; $SC.Description = 'AI-powered English Learning Assistant'; $SC.Save()"

echo [4/4] Creando entrada en el menu inicio...
powershell -Command "$WS = New-Object -ComObject WScript.Shell; $SC = $WS.CreateShortcut('%APPDATA%\Microsoft\Windows\Start Menu\Programs\English Learning Assistant.lnk'); $SC.TargetPath = '%INSTALL_DIR%\EnglishLearningAssistant.exe'; $SC.WorkingDirectory = '%INSTALL_DIR%'; $SC.Description = 'AI-powered English Learning Assistant'; $SC.Save()"

echo.
echo ========================================
echo  Instalacion completada!
echo ========================================
echo.
echo La aplicacion ha sido instalada en:
echo %INSTALL_DIR%
echo.
echo Accesos directos creados en:
echo - Escritorio
echo - Menu Inicio
echo.

REM Preguntar si instalar Ollama
if exist "OllamaSetup.exe" (
    echo.
    echo ========================================
    echo  Ollama (Motor de IA)
    echo ========================================
    echo.
    echo Ollama es necesario para que funcione el asistente de IA.
    echo.
    choice /C SN /M "Deseas instalar Ollama ahora"
    if errorlevel 2 goto skip_ollama
    if errorlevel 1 goto install_ollama
    
    :install_ollama
    echo.
    echo Instalando Ollama...
    start /wait OllamaSetup.exe
    echo.
    echo Descargando modelo de IA (llama3.2)...
    echo Esto puede tardar varios minutos...
    ollama pull llama3.2
    goto after_ollama
    
    :skip_ollama
    echo.
    echo [!] Ollama NO fue instalado.
    echo [!] Deberas instalarlo manualmente desde: https://ollama.ai/download
    echo.
)

:after_ollama
echo.
echo ========================================
echo  Primeros Pasos
echo ========================================
echo.
echo 1. Activa los Subtitulos en Vivo de Windows (Win + Ctrl + L)
echo 2. Ejecuta "English Learning Assistant" desde el escritorio
echo 3. Presiona Ctrl + Espacio para abrir el asistente
echo.
pause
'@

$installScript | Out-File -FilePath (Join-Path $installerDir "INSTALAR.bat") -Encoding ASCII

Write-Success "Script de instalación creado"

# ============================================================================
# PASO 7: Crear instalador portable (ZIP)
# ============================================================================
Write-Step "Creando instalador portable (ZIP)..."

$zipPath = Join-Path $outputDir "EnglishLearningAssistant-v1.0-Portable.zip"

try {
    Compress-Archive -Path "$installerDir\*" -DestinationPath $zipPath -Force
    Write-Success "Instalador portable creado: $zipPath"
}
catch {
    Write-Error "Error al crear ZIP: $_"
}

# ============================================================================
# PASO 8: Crear instalador autoextraíble (opcional)
# ============================================================================
Write-Step "Creando instalador autoextraíble..."

$sfxScript = @"
;!@Install@!UTF-8!
Title="English Learning Assistant - Instalador"
BeginPrompt="¿Deseas instalar English Learning Assistant?\n\nEsto instalará:\n- Aplicación principal\n- Ollama (Motor de IA)\n- Accesos directos"
RunProgram="INSTALAR.bat"
;!@InstallEnd@!
"@

$sfxScriptPath = Join-Path $installerDir "config.txt"
$sfxScript | Out-File -FilePath $sfxScriptPath -Encoding UTF8

# Verificar si 7-Zip está instalado
$7zipPath = "C:\Program Files\7-Zip\7z.exe"
if (Test-Path $7zipPath) {
    try {
        $sfxPath = Join-Path $outputDir "EnglishLearningAssistant-v1.0-Setup.exe"
        $sfxModule = "C:\Program Files\7-Zip\7zSD.sfx"
        
        if (Test-Path $sfxModule) {
            # Crear archivo temporal
            $tempArchive = Join-Path $env:TEMP "installer_temp.7z"
            & $7zipPath a -t7z $tempArchive "$installerDir\*" -mx9
            
            # Combinar SFX module + config + archive
            cmd /c copy /b "$sfxModule" + "$sfxScriptPath" + "$tempArchive" "$sfxPath"
            
            Remove-Item $tempArchive -Force
            Write-Success "Instalador autoextraíble creado: $sfxPath"
        }
        else {
            Write-Host "[!] Módulo SFX no encontrado, saltando instalador .exe" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "[!] Error al crear instalador autoextraíble: $_" -ForegroundColor Yellow
    }
}
else {
    Write-Host "[!] 7-Zip no encontrado, saltando instalador autoextraíble" -ForegroundColor Yellow
    Write-Host "    Instala 7-Zip para generar el instalador .exe" -ForegroundColor Gray
}

# ============================================================================
# RESUMEN
# ============================================================================
Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                                                                ║" -ForegroundColor Green
Write-Host "║              ¡CONSTRUCCIÓN COMPLETADA CON ÉXITO!               ║" -ForegroundColor Green
Write-Host "║                                                                ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

Write-Host "📦 ARCHIVOS GENERADOS:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Ejecutable:" -ForegroundColor Yellow
Write-Host "    $publishDir\EnglishLearningAssistant.exe" -ForegroundColor Gray
Write-Host ""
Write-Host "  Instaladores:" -ForegroundColor Yellow
Get-ChildItem $outputDir | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "    $($_.Name) ($size MB)" -ForegroundColor Gray
}
Write-Host ""

Write-Host "📋 PRÓXIMOS PASOS:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Prueba el instalador en una máquina limpia" -ForegroundColor White
Write-Host "  2. Sube los instaladores a GitHub Releases" -ForegroundColor White
Write-Host "  3. Actualiza el README con enlaces de descarga" -ForegroundColor White
Write-Host ""

Write-Host "🚀 Para distribuir:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  - Instalador portable (ZIP): Usuarios técnicos" -ForegroundColor White
Write-Host "  - Instalador autoextraíble (.exe): Usuarios no técnicos" -ForegroundColor White
Write-Host ""
