# 🔧 Guía de Instalación Rápida

## Para Usuarios No Técnicos

### Opción 1: Instalador Automático (Recomendado) ⚡

1. **Descarga el proyecto**
   - Haz clic en el botón verde "Code" → "Download ZIP"
   - Extrae el archivo ZIP en una carpeta de tu preferencia

2. **Ejecuta el instalador**
   - Haz clic derecho en `INSTALAR.ps1`
   - Selecciona "Ejecutar con PowerShell"
   - Si aparece una advertencia de seguridad, haz clic en "Más información" → "Ejecutar de todas formas"

3. **Espera a que termine**
   - El instalador descargará e instalará todo automáticamente
   - Puede tardar 10-15 minutos dependiendo de tu conexión a internet

4. **¡Listo!**
   - Encontrarás un acceso directo en tu escritorio: "English Learning Assistant"
   - También puedes usar el archivo `INICIAR.bat` en la carpeta del proyecto

---

### Opción 2: Instalación Manual 🛠️

Si el instalador automático no funciona, sigue estos pasos:

#### Paso 1: Instalar .NET 8.0
1. Descarga desde: https://dotnet.microsoft.com/download/dotnet/8.0
2. Ejecuta el instalador
3. Reinicia tu computadora

#### Paso 2: Instalar Ollama
1. Descarga desde: https://ollama.ai/download
2. Ejecuta el instalador
3. Abre PowerShell o CMD y ejecuta:
   ```
   ollama pull llama3.2
   ```

#### Paso 3: Compilar el Proyecto
1. Abre PowerShell o CMD en la carpeta del proyecto
2. Ejecuta:
   ```
   dotnet restore
   dotnet build
   ```

#### Paso 4: Ejecutar
```
dotnet run
```

---

## Para Desarrolladores

### Instalación Rápida

```bash
# Clonar repositorio
git clone https://github.com/CharlieCardenasToledo/WindowsLiveCaptionsRead.git
cd WindowsLiveCaptionsReader

# Instalar dependencias
dotnet restore

# Compilar
dotnet build

# Ejecutar
dotnet run
```

### Requisitos
- Windows 10/11
- .NET 8.0 SDK
- Ollama con modelo llama3.2

---

## Solución de Problemas del Instalador

### "No se puede ejecutar scripts en este sistema"

**Error**: `INSTALAR.ps1 cannot be loaded because running scripts is disabled`

**Solución**:
1. Abre PowerShell como Administrador
2. Ejecuta:
   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   ```
3. Intenta ejecutar el instalador de nuevo

### El instalador se cierra inmediatamente

**Solución**:
1. Haz clic derecho en `INSTALAR.ps1`
2. Selecciona "Editar" o "Abrir con PowerShell ISE"
3. Presiona F5 para ejecutar

### Error de permisos

**Solución**:
- Asegúrate de ejecutar PowerShell como Administrador
- El instalador solicitará permisos automáticamente

---

## ¿Necesitas Ayuda?

Si tienes problemas con la instalación:
1. Revisa la sección [Troubleshooting](README.md#-troubleshooting) en el README
2. Abre un [issue en GitHub](https://github.com/CharlieCardenasToledo/WindowsLiveCaptionsRead/issues)
3. Incluye el mensaje de error completo

---

**¡Disfruta aprendiendo inglés con IA! 🎓**
