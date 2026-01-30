# 📦 Guía de Construcción del Instalador

## Para Desarrolladores

Esta guía explica cómo construir los instaladores para distribuir la aplicación.

---

## Requisitos Previos

### Obligatorios
- **Windows 10/11**
- **.NET 8.0 SDK**
- **PowerShell 5.1+**

### Opcionales (para instalador .exe)
- **7-Zip** - Para crear instalador autoextraíble
  ```bash
  winget install 7zip.7zip
  ```

---

## Construcción Rápida

### Opción 1: Script Automático (Recomendado)

```powershell
.\BUILD-INSTALLER.ps1
```

Esto generará:
- ✅ Ejecutable autónomo (`EnglishLearningAssistant.exe`)
- ✅ Instalador portable (ZIP)
- ✅ Instalador autoextraíble (EXE) - si 7-Zip está instalado

### Opción 2: Solo Publicar Ejecutable

```powershell
dotnet publish --configuration Release --runtime win-x64 --self-contained true --output ./publish
```

---

## Archivos Generados

Después de ejecutar el script de construcción:

```
output/
├── EnglishLearningAssistant-v1.0-Portable.zip    (~50 MB)
└── EnglishLearningAssistant-v1.0-Setup.exe       (~50 MB) [opcional]

publish/
└── EnglishLearningAssistant.exe                  (~50 MB)

installer/
├── EnglishLearningAssistant.exe
├── OllamaSetup.exe
├── INSTALAR.bat
├── README.md
└── README.es.md
```

---

## Tipos de Instaladores

### 1. **Instalador Portable (ZIP)** ✅ Recomendado

**Ventajas:**
- ✅ No requiere permisos de administrador
- ✅ Fácil de distribuir
- ✅ Compatible con todos los sistemas

**Uso:**
1. Descomprimir el ZIP
2. Ejecutar `INSTALAR.bat`
3. Seguir las instrucciones

### 2. **Instalador Autoextraíble (EXE)**

**Ventajas:**
- ✅ Un solo archivo
- ✅ Extracción automática
- ✅ Más profesional para usuarios finales

**Requisitos:**
- Requiere 7-Zip instalado para construirlo

---

## Configuración del Proyecto

El archivo `.csproj` está configurado para:

```xml
<PropertyGroup>
  <!-- Ejecutable de Windows -->
  <OutputType>WinExe</OutputType>
  
  <!-- Publicación como archivo único -->
  <PublishSingleFile>true</PublishSingleFile>
  
  <!-- Incluir todas las dependencias -->
  <SelfContained>true</SelfContained>
  
  <!-- Optimizaciones -->
  <PublishReadyToRun>true</PublishReadyToRun>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
</PropertyGroup>
```

---

## Proceso de Instalación (Usuario Final)

### Instalador Portable (ZIP)

1. **Descargar** `EnglishLearningAssistant-v1.0-Portable.zip`
2. **Extraer** en cualquier carpeta
3. **Ejecutar** `INSTALAR.bat` (clic derecho → Ejecutar como administrador)
4. **Seguir** las instrucciones en pantalla

El instalador:
- ✅ Copia archivos a `C:\Program Files\EnglishLearningAssistant`
- ✅ Crea acceso directo en el escritorio
- ✅ Crea entrada en el menú inicio
- ✅ Ofrece instalar Ollama automáticamente
- ✅ Descarga el modelo de IA (llama3.2)

### Instalador Autoextraíble (EXE)

1. **Descargar** `EnglishLearningAssistant-v1.0-Setup.exe`
2. **Ejecutar** el instalador
3. **Aceptar** la extracción
4. **Seguir** las instrucciones automáticas

---

## Personalización

### Cambiar Versión

Edita `WindowsLiveCaptionsReader.csproj`:

```xml
<Version>1.0.0</Version>  <!-- Cambiar aquí -->
```

### Cambiar Nombre del Ejecutable

Edita `WindowsLiveCaptionsReader.csproj`:

```xml
<AssemblyName>EnglishLearningAssistant</AssemblyName>  <!-- Cambiar aquí -->
```

### Agregar Ícono

1. Crea un archivo `app.ico`
2. Agrega al `.csproj`:
   ```xml
   <ApplicationIcon>app.ico</ApplicationIcon>
   ```

---

## Distribución

### GitHub Releases

1. **Construir** los instaladores:
   ```powershell
   .\BUILD-INSTALLER.ps1
   ```

2. **Crear** un nuevo release en GitHub:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

3. **Subir** los archivos de `output/`:
   - `EnglishLearningAssistant-v1.0-Portable.zip`
   - `EnglishLearningAssistant-v1.0-Setup.exe`

4. **Actualizar** README con enlaces de descarga

---

## Solución de Problemas

### Error: "No se puede cargar el archivo porque la ejecución de scripts está deshabilitada"

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Error al publicar: "Runtime identifier 'win-x64' is not supported"

Asegúrate de tener .NET 8.0 SDK instalado:
```bash
dotnet --version  # Debe ser 8.0.x
```

### El ejecutable es muy grande (>100 MB)

Esto es normal para aplicaciones self-contained. Incluye:
- .NET Runtime (~30 MB)
- WPF Framework (~15 MB)
- Dependencias (NAudio, Selenium, etc.) (~5 MB)

Para reducir tamaño, puedes usar:
```xml
<PublishTrimmed>true</PublishTrimmed>
```

⚠️ **Advertencia**: El trimming puede causar problemas con reflexión.

---

## Checklist de Release

Antes de publicar un release:

- [ ] Actualizar versión en `.csproj`
- [ ] Probar el instalador en una máquina limpia
- [ ] Verificar que Ollama se instale correctamente
- [ ] Probar el ejecutable sin .NET instalado
- [ ] Actualizar CHANGELOG.md
- [ ] Actualizar README con enlaces de descarga
- [ ] Crear tag de Git
- [ ] Subir a GitHub Releases

---

## Notas Técnicas

### Tamaño del Instalador

- **Ejecutable**: ~50 MB (incluye .NET Runtime)
- **Ollama**: ~500 MB (descarga separada)
- **Modelo llama3.2**: ~2 GB (descarga durante instalación)

### Compatibilidad

- **Windows**: 10 (1809+), 11
- **Arquitectura**: x64 (64-bit)
- **RAM**: Mínimo 8 GB (recomendado 16 GB para Ollama)

---

**¿Preguntas?** Abre un issue en GitHub.
