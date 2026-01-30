# ============================================================================
# Windows Live Captions Reader - Instalador Automático
# ============================================================================
# Este script instala automáticamente todas las dependencias necesarias
# para ejecutar el asistente de aprendizaje de inglés.
# ============================================================================

param(
    [switch]$SkipOllama,
    [switch]$SkipDotNet,
    [switch]$Unattended
)

# Configuración
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Colores para output
function Write-ColorOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Write-Step {
    param([string]$Message)
    Write-ColorOutput "`n[PASO] $Message" "Cyan"
}

function Write-Success {
    param([string]$Message)
    Write-ColorOutput "[✓] $Message" "Green"
}

function Write-Error {
    param([string]$Message)
    Write-ColorOutput "[✗] $Message" "Red"
}

function Write-Warning {
    param([string]$Message)
    Write-ColorOutput "[!] $Message" "Yellow"
}

# Banner
Clear-Host
Write-ColorOutput @"
╔════════════════════════════════════════════════════════════════╗
║                                                                ║
║   Windows Live Captions Reader - Instalador Automático        ║
║   Asistente de Aprendizaje de Inglés con IA                   ║
║                                                                ║
╚════════════════════════════════════════════════════════════════╝
"@ "Cyan"

Write-Host ""

# Verificar permisos de administrador
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Warning "Este script requiere permisos de administrador para instalar dependencias."
    Write-Host "Reiniciando con permisos elevados..." -ForegroundColor Yellow
    Start-Sleep -Seconds 2
    
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($SkipOllama) { $arguments += " -SkipOllama" }
    if ($SkipDotNet) { $arguments += " -SkipDotNet" }
    if ($Unattended) { $arguments += " -Unattended" }
    
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    exit
}

Write-Success "Ejecutando con permisos de administrador"

# ============================================================================
# PASO 1: Verificar e instalar winget
# ============================================================================
Write-Step "Verificando Windows Package Manager (winget)..."

try {
    $wingetVersion = winget --version
    Write-Success "winget está instalado: $wingetVersion"
}
catch {
    Write-Warning "winget no está instalado. Instalando..."
    
    try {
        # Instalar App Installer (incluye winget)
        Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe
        Write-Success "winget instalado correctamente"
    }
    catch {
        Write-Error "No se pudo instalar winget automáticamente."
        Write-Host "Por favor, instala 'App Installer' desde Microsoft Store e intenta de nuevo." -ForegroundColor Yellow
        Read-Host "Presiona Enter para salir"
        exit 1
    }
}

# ============================================================================
# PASO 2: Instalar .NET 8.0 SDK
# ============================================================================
if (-not $SkipDotNet) {
    Write-Step "Verificando .NET 8.0 SDK..."
    
    try {
        $dotnetVersion = dotnet --version
        if ($dotnetVersion -match "^8\.") {
            Write-Success ".NET 8.0 SDK ya está instalado: $dotnetVersion"
        }
        else {
            throw "Versión incorrecta"
        }
    }
    catch {
        Write-Warning ".NET 8.0 SDK no encontrado. Instalando..."
        
        try {
            winget install --id Microsoft.DotNet.SDK.8 --silent --accept-source-agreements --accept-package-agreements
            Write-Success ".NET 8.0 SDK instalado correctamente"
            
            # Actualizar PATH
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
        }
        catch {
            Write-Error "Error al instalar .NET 8.0 SDK: $_"
            Write-Host "Por favor, descarga e instala manualmente desde: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
            Read-Host "Presiona Enter para continuar de todos modos"
        }
    }
}
else {
    Write-Warning "Omitiendo instalación de .NET (parámetro -SkipDotNet)"
}

# ============================================================================
# PASO 3: Instalar Ollama
# ============================================================================
if (-not $SkipOllama) {
    Write-Step "Verificando Ollama..."
    
    try {
        $ollamaPath = Get-Command ollama -ErrorAction Stop
        Write-Success "Ollama ya está instalado: $($ollamaPath.Source)"
    }
    catch {
        Write-Warning "Ollama no encontrado. Instalando..."
        
        try {
            winget install --id Ollama.Ollama --silent --accept-source-agreements --accept-package-agreements
            Write-Success "Ollama instalado correctamente"
            
            # Actualizar PATH
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
        }
        catch {
            Write-Error "Error al instalar Ollama: $_"
            Write-Host "Por favor, descarga e instala manualmente desde: https://ollama.ai/download" -ForegroundColor Yellow
            Read-Host "Presiona Enter para continuar de todos modos"
        }
    }
}
else {
    Write-Warning "Omitiendo instalación de Ollama (parámetro -SkipOllama)"
}

# ============================================================================
# PASO 4: Descargar modelo de IA (llama3.2)
# ============================================================================
Write-Step "Verificando modelo de IA (llama3.2)..."

try {
    # Verificar si Ollama está ejecutándose
    $ollamaRunning = $false
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:11434" -Method Get -TimeoutSec 2 -ErrorAction SilentlyContinue
        $ollamaRunning = $true
    }
    catch {
        Write-Warning "Ollama no está ejecutándose. Iniciando servidor..."
        Start-Process -FilePath "ollama" -ArgumentList "serve" -WindowStyle Hidden
        Start-Sleep -Seconds 3
    }
    
    # Verificar si el modelo ya está descargado
    $models = ollama list 2>$null
    if ($models -match "llama3.2") {
        Write-Success "Modelo llama3.2 ya está descargado"
    }
    else {
        Write-Warning "Descargando modelo llama3.2 (esto puede tardar varios minutos)..."
        Write-Host "Tamaño aproximado: ~2GB" -ForegroundColor Yellow
        
        ollama pull llama3.2
        Write-Success "Modelo llama3.2 descargado correctamente"
    }
}
catch {
    Write-Error "Error al verificar/descargar modelo: $_"
    Write-Host "Puedes descargarlo manualmente más tarde ejecutando: ollama pull llama3.2" -ForegroundColor Yellow
}

# ============================================================================
# PASO 5: Restaurar dependencias del proyecto
# ============================================================================
Write-Step "Restaurando dependencias del proyecto..."

$projectPath = $PSScriptRoot

try {
    Push-Location $projectPath
    dotnet restore
    Write-Success "Dependencias restauradas correctamente"
}
catch {
    Write-Error "Error al restaurar dependencias: $_"
    Write-Host "Intenta ejecutar manualmente: dotnet restore" -ForegroundColor Yellow
}
finally {
    Pop-Location
}

# ============================================================================
# PASO 6: Compilar el proyecto
# ============================================================================
Write-Step "Compilando el proyecto..."

try {
    Push-Location $projectPath
    dotnet build --configuration Release
    Write-Success "Proyecto compilado correctamente"
}
catch {
    Write-Error "Error al compilar el proyecto: $_"
    Write-Host "Intenta ejecutar manualmente: dotnet build" -ForegroundColor Yellow
}
finally {
    Pop-Location
}

# ============================================================================
# PASO 7: Crear acceso directo en el escritorio
# ============================================================================
Write-Step "Creando acceso directo en el escritorio..."

try {
    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktopPath "English Learning Assistant.lnk"
    
    $WScriptShell = New-Object -ComObject WScript.Shell
    $shortcut = $WScriptShell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = "dotnet"
    $shortcut.Arguments = "run --project `"$projectPath`""
    $shortcut.WorkingDirectory = $projectPath
    $shortcut.Description = "Asistente de Aprendizaje de Inglés con IA"
    $shortcut.IconLocation = "shell32.dll,21"
    $shortcut.Save()
    
    Write-Success "Acceso directo creado en el escritorio"
}
catch {
    Write-Warning "No se pudo crear el acceso directo: $_"
}

# ============================================================================
# PASO 8: Crear script de inicio rápido
# ============================================================================
Write-Step "Creando script de inicio rápido..."

$startScript = @"
@echo off
title English Learning Assistant
echo ========================================
echo  English Learning Assistant
echo  Iniciando...
echo ========================================
echo.

REM Verificar si Ollama está ejecutándose
curl -s http://localhost:11434 >nul 2>&1
if errorlevel 1 (
    echo [!] Iniciando servidor Ollama...
    start /B ollama serve
    timeout /t 3 /nobreak >nul
)

echo [✓] Servidor Ollama activo
echo [✓] Iniciando aplicación...
echo.

cd /d "%~dp0"
dotnet run

pause
"@

$startScriptPath = Join-Path $projectPath "INICIAR.bat"
$startScript | Out-File -FilePath $startScriptPath -Encoding ASCII -Force

Write-Success "Script de inicio creado: INICIAR.bat"

# ============================================================================
# RESUMEN FINAL
# ============================================================================
Write-Host ""
Write-ColorOutput "╔════════════════════════════════════════════════════════════════╗" "Green"
Write-ColorOutput "║                                                                ║" "Green"
Write-ColorOutput "║              ¡INSTALACIÓN COMPLETADA CON ÉXITO!                ║" "Green"
Write-ColorOutput "║                                                                ║" "Green"
Write-ColorOutput "╚════════════════════════════════════════════════════════════════╝" "Green"
Write-Host ""

Write-ColorOutput "📋 RESUMEN DE INSTALACIÓN:" "Cyan"
Write-Host ""
Write-Host "  ✓ .NET 8.0 SDK instalado" -ForegroundColor Green
Write-Host "  ✓ Ollama instalado" -ForegroundColor Green
Write-Host "  ✓ Modelo llama3.2 descargado" -ForegroundColor Green
Write-Host "  ✓ Dependencias del proyecto restauradas" -ForegroundColor Green
Write-Host "  ✓ Proyecto compilado" -ForegroundColor Green
Write-Host ""

Write-ColorOutput "🚀 CÓMO INICIAR LA APLICACIÓN:" "Cyan"
Write-Host ""
Write-Host "  Opción 1: Haz doble clic en el acceso directo del escritorio" -ForegroundColor Yellow
Write-Host "            'English Learning Assistant'" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Opción 2: Ejecuta el archivo INICIAR.bat en la carpeta del proyecto" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Opción 3: Desde PowerShell/CMD:" -ForegroundColor Yellow
Write-Host "            cd `"$projectPath`"" -ForegroundColor Gray
Write-Host "            dotnet run" -ForegroundColor Gray
Write-Host ""

Write-ColorOutput "📚 PRIMEROS PASOS:" "Cyan"
Write-Host ""
Write-Host "  1. Activa los Subtítulos en Vivo de Windows (Win + Ctrl + L)" -ForegroundColor White
Write-Host "  2. Inicia la aplicación" -ForegroundColor White
Write-Host "  3. Presiona Ctrl + Espacio para abrir el asistente de IA" -ForegroundColor White
Write-Host "  4. ¡Comienza a practicar tu inglés!" -ForegroundColor White
Write-Host ""

Write-ColorOutput "📖 DOCUMENTACIÓN:" "Cyan"
Write-Host ""
Write-Host "  README.md    - Guía completa en inglés" -ForegroundColor White
Write-Host "  README.es.md - Guía completa en español" -ForegroundColor White
Write-Host ""

if (-not $Unattended) {
    Write-Host ""
    $launch = Read-Host "¿Deseas iniciar la aplicación ahora? (S/N)"
    if ($launch -eq "S" -or $launch -eq "s" -or $launch -eq "Y" -or $launch -eq "y") {
        Write-Host ""
        Write-ColorOutput "Iniciando aplicación..." "Green"
        Start-Sleep -Seconds 1
        
        # Iniciar Ollama si no está ejecutándose
        try {
            Invoke-WebRequest -Uri "http://localhost:11434" -Method Get -TimeoutSec 2 -ErrorAction Stop | Out-Null
        }
        catch {
            Start-Process -FilePath "ollama" -ArgumentList "serve" -WindowStyle Hidden
            Start-Sleep -Seconds 3
        }
        
        # Iniciar aplicación
        Push-Location $projectPath
        dotnet run
        Pop-Location
    }
}

Write-Host ""
Write-ColorOutput "¡Gracias por usar English Learning Assistant! 🎓" "Cyan"
Write-Host ""
