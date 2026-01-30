$ErrorActionPreference = "SilentlyContinue"

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "   LANZADOR DE EXAMEN (PERFIL LIMPIO)" -ForegroundColor Cyan
Write-Host "=============================================="
Write-Host ""

# 1. Definir Rutas
$chromePath = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$edgePath = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
$examProfileDir = "$env:LOCALAPPDATA\Google\Chrome\User Data Exam"

# 2. Verificar si Chrome existe
if (-not (Test-Path $chromePath)) {
    Write-Host "Chrome no encontrado en ruta estandar. Buscando Edge..." -ForegroundColor Yellow
    if (Test-Path $edgePath) {
        $chromePath = $edgePath
        $examProfileDir = "$env:LOCALAPPDATA\Microsoft\Edge\User Data Exam"
        Write-Host "Usando Microsoft Edge." -ForegroundColor Green
    } else {
        Write-Host "ERROR: No se encontro Chrome ni Edge." -ForegroundColor Red
        Read-Host "Presiona Enter para salir"
        exit
    }
}

# 3. Matar instancias viejas (Opcional, pero recomendado para evitar conflictos de puerto)
# Con perfil separado NO es estrictamente necesario matar todo, pero ayuda.
Write-Host "Cerrando instancias previas del Modo Examen..."
Stop-Process -Name chrome -ErrorAction SilentlyContinue
Stop-Process -Name msedge -ErrorAction SilentlyContinue

# 4. Lanzar Navegador
Write-Host "Iniciando Navegador en Puerto 9222..."
Write-Host "Perfil: $examProfileDir"
Write-Host ""
Write-Host ">> POR FAVOR INICIA SESION EN CANVAS EN LA VENTANA QUE SE ABRA <<" -ForegroundColor Yellow
Write-Host ""

# Start-Process permite pasar argumentos limpiamente
Start-Process -FilePath $chromePath -ArgumentList "--remote-debugging-port=9222", "--user-data-dir=""$examProfileDir""", "--no-first-run", "--no-default-browser-check"

# 5. Verificacion
Start-Sleep -Seconds 3
Write-Host "Verificando puerto..."
$conn = Test-NetConnection -ComputerName localhost -Port 9222 -WarningAction SilentlyContinue

if ($conn.TcpTestSucceeded) {
    Write-Host "EXITO: CONEXION ESTABLECIDA." -ForegroundColor Green
    Write-Host "Ahora puedes ejecutar la App WindowsLiveCaptionsReader."
} else {
    Write-Host "ADVERTENCIA: No se detecto el puerto 9222 abierto." -ForegroundColor Red
    Write-Host "Intenta ejecutar este script como Administrador."
}

Read-Host "Presiona Enter para cerrar esta ventana"
