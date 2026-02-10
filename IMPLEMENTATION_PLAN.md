# Plan de Implementacion - English Learning Assistant v2.0

## Estado Actual del Proyecto

### Funcionalidades Existentes
| Funcionalidad | Estado | Archivo(s) |
|---|---|---|
| Captura de Live Captions (Windows) | Implementado | `Services/CaptionReader.cs` |
| Reconocimiento de voz (microfono) | Implementado | `Services/AudioCaptureService.cs` |
| Grabacion manual de audio (WAV) | Implementado | `Services/AudioRecorderService.cs` |
| Transcripcion local con Whisper | Implementado | `Services/WhisperService.cs` |
| Traduccion IA via Ollama (streaming) | Implementado | `Services/OllamaService.cs` |
| Captura de texto del navegador | Implementado | `Services/BrowserCaptureService.cs` |
| Chrome DevTools Protocol | Implementado | `Services/ChromeSessionService.cs` |
| Ventana de asistente IA | Implementado | `AssistantWindow.xaml.cs` |
| Historial de conversacion (en memoria) | Implementado | `MainWindow.xaml.cs` |
| Persistencia basica a log | Parcial | `conversation_history.log` |
| Deteccion de preguntas | Basica | `MainWindow.xaml.cs` (solo busca "?") |
| Resumen de clase | Implementado | `OllamaService.cs` |

### Problemas Identificados

1. **Sin gestion de sesiones**: El historial se guarda en un archivo plano (`conversation_history.log`) sin estructura. No hay forma de separar sesiones, recargarlas ni exportarlas.
2. **Deteccion de preguntas primitiva**: Solo busca el caracter `?` y algunas palabras clave. No detecta preguntas implicitas ni analiza entonacion.
3. **Asistente IA reactivo, no proactivo**: El asistente requiere accion manual (Ctrl+Space) en la mayoria de casos. La auto-activacion por preguntas es inconsistente.
4. **Sin base de datos**: Todo se almacena en memoria o archivos planos.
5. **Sin seguimiento de vocabulario**: No hay registro persistente de palabras aprendidas.
6. **Sin metricas de progreso**: No se mide el avance del estudiante.
7. **MainWindow.xaml.cs sobrecargado**: 865 lineas con UI + logica de negocio mezcladas.
8. **Sin tests unitarios**: Cero cobertura de pruebas.

---

## Nuevas Funcionalidades Propuestas

### 1. Sistema de Gestion de Sesiones

**Objetivo**: Permitir guardar, cargar, exportar y organizar sesiones de aprendizaje.

**Archivos nuevos**:
- `Models/Session.cs` - Modelo de datos de sesion
- `Models/TranscriptionEntry.cs` - Entrada individual de transcripcion
- `Services/SessionService.cs` - Logica de persistencia
- `Data/AppDbContext.cs` - Contexto de base de datos SQLite

**Archivos a modificar**:
- `MainWindow.xaml` / `MainWindow.xaml.cs` - UI de sesiones
- `WindowsLiveCaptionsReader.csproj` - Agregar dependencia SQLite

#### Modelo de Datos

```csharp
// Models/Session.cs
public class Session
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public string Summary { get; set; } = "";
    public List<TranscriptionEntry> Entries { get; set; } = new();
    public List<DetectedQuestion> Questions { get; set; } = new();
    public SessionMetadata Metadata { get; set; } = new();
}

public enum SessionStatus { Active, Paused, Completed, Archived }

// Models/TranscriptionEntry.cs
public class TranscriptionEntry
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string OriginalText { get; set; } = "";
    public string TranslatedText { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public EntrySource Source { get; set; }  // LiveCaption, Microphone, Browser, Recording
    public float? ConfidenceScore { get; set; }
    public bool ContainsQuestion { get; set; }
    public string? AiResponse { get; set; }
}

public enum EntrySource { LiveCaption, Microphone, Browser, Recording }

// Models/DetectedQuestion.cs
public class DetectedQuestion
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int EntryId { get; set; }
    public string QuestionText { get; set; } = "";
    public string Context { get; set; } = "";
    public string? SuggestedAnswer { get; set; }
    public QuestionType Type { get; set; }
    public DateTime DetectedAt { get; set; }
    public bool WasAnswered { get; set; }
}

public enum QuestionType
{
    Direct,        // "What is your name?"
    TagQuestion,   // "You like coffee, don't you?"
    Indirect,      // "I was wondering if you could help"
    YesNo,         // "Do you understand?"
    WhQuestion,    // "Where did you go?"
    Choice,        // "Do you want tea or coffee?"
    Rhetorical     // "Isn't it obvious?"
}

// Models/SessionMetadata.cs
public class SessionMetadata
{
    public int TotalEntries { get; set; }
    public int QuestionsDetected { get; set; }
    public int QuestionsAnswered { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> TopVocabulary { get; set; } = new();
    public string PrimaryTopic { get; set; } = "";
}
```

#### Servicio de Sesiones

```csharp
// Services/SessionService.cs
public class SessionService
{
    // Crear nueva sesion
    Task<Session> CreateSessionAsync(string title);

    // Guardar sesion actual
    Task SaveSessionAsync(Session session);

    // Cargar sesion por ID
    Task<Session?> LoadSessionAsync(int sessionId);

    // Listar todas las sesiones
    Task<List<Session>> GetAllSessionsAsync();

    // Buscar sesiones por texto
    Task<List<Session>> SearchSessionsAsync(string query);

    // Exportar sesion a Markdown
    Task<string> ExportToMarkdownAsync(int sessionId);

    // Exportar sesion a JSON
    Task<string> ExportToJsonAsync(int sessionId);

    // Eliminar sesion
    Task DeleteSessionAsync(int sessionId);

    // Auto-guardar (cada 30 segundos)
    void StartAutoSave(Session session);
    void StopAutoSave();
}
```

#### Base de Datos SQLite

```csharp
// Data/AppDbContext.cs
public class AppDbContext : DbContext
{
    public DbSet<Session> Sessions { get; set; }
    public DbSet<TranscriptionEntry> Entries { get; set; }
    public DbSet<DetectedQuestion> Questions { get; set; }
    public DbSet<VocabularyItem> Vocabulary { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsLiveCaptionsReader", "sessions.db");
        options.UseSqlite($"Data Source={dbPath}");
    }
}
```

**Dependencia NuGet a agregar**:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11" />
```

#### UI de Sesiones

Agregar al `MainWindow.xaml`:
- Panel lateral con lista de sesiones guardadas
- Boton "Nueva Sesion" / "Guardar Sesion" / "Cargar Sesion"
- Indicador de sesion activa en el header
- Dialogo de exportacion (Markdown / JSON)

---

### 2. Deteccion Inteligente de Preguntas

**Objetivo**: Detectar preguntas en tiempo real con alta precision, clasificarlas y activar el asistente automaticamente.

**Archivos nuevos**:
- `Services/QuestionDetectionService.cs` - Motor de deteccion

**Archivos a modificar**:
- `MainWindow.xaml.cs` - Integrar deteccion automatica
- `Services/OllamaService.cs` - Nuevo prompt para analisis de preguntas

#### Motor de Deteccion

```csharp
// Services/QuestionDetectionService.cs
public class QuestionDetectionService
{
    // Patrones de preguntas en ingles
    private static readonly string[] QuestionStarters = {
        "what", "where", "when", "who", "whom", "whose",
        "which", "why", "how",
        "do", "does", "did",
        "is", "are", "was", "were",
        "have", "has", "had",
        "can", "could", "will", "would", "shall", "should",
        "may", "might", "must"
    };

    private static readonly string[] IndirectPatterns = {
        @"(?i)i('m| am) wondering",
        @"(?i)do you (know|think|believe|suppose)",
        @"(?i)could you (tell|explain|show|help)",
        @"(?i)would you (mind|like|prefer)",
        @"(?i)i('d| would) like to (know|ask|understand)",
        @"(?i)can you (tell|explain|clarify)",
        @"(?i)any (idea|thoughts|suggestions|questions)",
        @"(?i)what do you (think|reckon|say)",
        @"(?i)how (about|come)",
        @"(?i)isn't it|aren't you|doesn't it|don't you|won't you"
    };

    // Detectar si un texto contiene una pregunta
    public QuestionAnalysis Analyze(string text);

    // Analisis con contexto (usa ultimas N entradas)
    public QuestionAnalysis AnalyzeWithContext(string text, List<TranscriptionEntry> context);

    // Clasificar tipo de pregunta
    public QuestionType ClassifyQuestion(string question);

    // Verificacion con IA (para casos ambiguos, confianza < 0.7)
    public Task<QuestionAnalysis> AnalyzeWithAIAsync(string text, string context);
}

public class QuestionAnalysis
{
    public bool IsQuestion { get; set; }
    public float Confidence { get; set; }        // 0.0 - 1.0
    public QuestionType Type { get; set; }
    public string ExtractedQuestion { get; set; } = "";
    public string Context { get; set; } = "";
    public bool RequiresResponse { get; set; }   // false para retoricas
    public string? SuggestedResponseHint { get; set; }
}
```

#### Estrategia de Deteccion (Cascada)

```
Texto capturado
    |
    v
[Nivel 1: Patron directo] -- Confianza > 0.9
    Buscar "?" al final
    Buscar inversiones sujeto-verbo ("Is he", "Do you")
    |
    v (si no detecta)
[Nivel 2: Palabras clave] -- Confianza 0.7 - 0.9
    Verificar inicio con question starters
    Verificar tag questions ("..., right?", "..., isn't it?")
    |
    v (si no detecta)
[Nivel 3: Patrones indirectos] -- Confianza 0.5 - 0.7
    Buscar patrones de preguntas indirectas
    Analizar contexto conversacional
    |
    v (si confianza < 0.7 y contexto sugiere pregunta)
[Nivel 4: Verificacion IA] -- Confianza variable
    Enviar a Ollama para analisis semantico
    Solo para casos ambiguos (no abusar de IA)
```

#### Integracion con el Flujo Principal

Modificar `MainWindow.xaml.cs` - metodo `DebounceTimer_Tick()`:

```csharp
// Despues de traducir, analizar si es pregunta
var analysis = _questionDetector.AnalyzeWithContext(
    originalText,
    _currentSession.Entries.TakeLast(5).ToList()
);

if (analysis.IsQuestion && analysis.Confidence >= 0.6)
{
    entry.ContainsQuestion = true;

    // Guardar pregunta detectada
    var question = new DetectedQuestion
    {
        QuestionText = analysis.ExtractedQuestion,
        Context = analysis.Context,
        Type = analysis.Type,
        DetectedAt = DateTime.Now
    };
    _currentSession.Questions.Add(question);

    // Auto-activar asistente si la confianza es alta
    if (analysis.Confidence >= 0.75 && analysis.RequiresResponse)
    {
        await ActivateAssistantForQuestion(question);
    }
    else
    {
        // Mostrar indicador sutil de pregunta detectada
        ShowQuestionIndicator(analysis);
    }
}
```

---

### 3. Asistente IA Contextual Mejorado

**Objetivo**: Cuando se detecta una pregunta, el asistente se activa automaticamente y genera sugerencias de respuesta basadas en todo el contexto de la sesion.

**Archivos nuevos**:
- `AssistantPanelControl.xaml` / `.cs` - Panel integrado (reemplaza ventana separada)

**Archivos a modificar**:
- `Services/OllamaService.cs` - Nuevos prompts contextuales
- `MainWindow.xaml` / `MainWindow.xaml.cs` - Panel integrado
- `AssistantWindow.xaml` / `AssistantWindow.xaml.cs` - Refactorizar o reemplazar

#### Nuevo Prompt para Respuestas a Preguntas

Agregar a `OllamaService.cs`:

```csharp
public async Task<string> GenerateQuestionResponseAsync(
    DetectedQuestion question,
    List<TranscriptionEntry> context,
    CancellationToken ct = default)
{
    var systemPrompt = @"You are an English tutor helping a B1 Spanish-speaking student
respond to questions in a live English conversation.

RULES:
- The student needs IMMEDIATE help (they are in a live conversation)
- Provide 3 response options: Simple, Standard, and Detailed
- Each response must be natural and appropriate for the context
- Include pronunciation hints for difficult words
- Keep responses at B1 level (simple grammar, common vocabulary)
- ALWAYS include Spanish translation

FORMAT for each option:
🟢 Simple: [1 sentence response]
🟡 Standard: [2-3 sentence response]
🔴 Detailed: [3-4 sentence response with examples]
📝 Grammar tip: [Brief Spanish explanation of key structure used]
🔊 Pronunciation: [Any tricky words with phonetic guide]
🇪🇸 Spanish: [Full translation of all responses]";

    var contextStr = string.Join("\n", context.Select(e =>
        $"[{e.Timestamp:HH:mm}] {e.OriginalText}"));

    var userPrompt = $@"CONVERSATION CONTEXT:
{contextStr}

DETECTED QUESTION: {question.QuestionText}
QUESTION TYPE: {question.Type}

Generate 3 response options the student can use RIGHT NOW.";

    // ... llamada a Ollama con streaming
}
```

#### Panel de Asistente Integrado

Reemplazar `AssistantWindow` con un panel deslizable dentro de `MainWindow`:

```xml
<!-- En MainWindow.xaml - Panel lateral derecho -->
<Border x:Name="AssistantPanel"
        Width="380"
        Visibility="Collapsed"
        HorizontalAlignment="Right">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- Header -->
            <RowDefinition Height="Auto"/>  <!-- Pregunta detectada -->
            <RowDefinition Height="*"/>     <!-- Sugerencias -->
            <RowDefinition Height="Auto"/>  <!-- Acciones -->
        </Grid.RowDefinitions>

        <!-- Header con tipo de pregunta -->
        <StackPanel Grid.Row="0" Orientation="Horizontal">
            <TextBlock Text="&#xE99A;" FontFamily="Segoe MDL2 Assets"/>
            <TextBlock Text="Question Detected!" FontWeight="Bold"/>
            <Border Background="#FF6B35" CornerRadius="8" Padding="6,2">
                <TextBlock x:Name="QuestionTypeBadge" Text="Wh-Question"/>
            </Border>
        </StackPanel>

        <!-- Pregunta extraida -->
        <Border Grid.Row="1" Background="#1A3B82F6" CornerRadius="8">
            <TextBlock x:Name="DetectedQuestionText" TextWrapping="Wrap"/>
        </Border>

        <!-- Opciones de respuesta -->
        <ScrollViewer Grid.Row="2">
            <StackPanel x:Name="ResponseOptions">
                <!-- Se llena dinamicamente con las 3 opciones -->
            </StackPanel>
        </ScrollViewer>

        <!-- Botones de accion -->
        <StackPanel Grid.Row="3" Orientation="Horizontal">
            <Button Content="Copy Response" Click="CopyResponse_Click"/>
            <Button Content="Refresh" Click="RefreshSuggestions_Click"/>
            <Button Content="Dismiss" Click="DismissAssistant_Click"/>
        </StackPanel>
    </Grid>
</Border>
```

#### Notificacion Visual de Pregunta

Cuando se detecta una pregunta, mostrar una notificacion sutil:

```csharp
private void ShowQuestionIndicator(QuestionAnalysis analysis)
{
    Dispatcher.Invoke(() =>
    {
        // Animacion de borde pulsante en el texto original
        var animation = new ColorAnimation
        {
            From = Colors.Transparent,
            To = Color.FromRgb(59, 130, 246), // Azul
            Duration = TimeSpan.FromMilliseconds(500),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3)
        };
        OriginalTextBorder.BorderBrush = new SolidColorBrush();
        ((SolidColorBrush)OriginalTextBorder.BorderBrush)
            .BeginAnimation(SolidColorBrush.ColorProperty, animation);

        // Mostrar badge de pregunta
        QuestionBadge.Visibility = Visibility.Visible;
        QuestionBadge.Text = $"Question ({analysis.Confidence:P0})";

        // Auto-mostrar panel si confianza alta
        if (analysis.Confidence >= 0.75)
        {
            ShowAssistantPanel();
        }
    });
}
```

---

### 4. Sistema de Vocabulario Persistente

**Objetivo**: Extraer y almacenar vocabulario nuevo de cada sesion para repaso.

**Archivos nuevos**:
- `Models/VocabularyItem.cs` - Modelo de vocabulario
- `Services/VocabularyService.cs` - Gestion de vocabulario

#### Modelo

```csharp
// Models/VocabularyItem.cs
public class VocabularyItem
{
    public int Id { get; set; }
    public string Word { get; set; } = "";
    public string Definition { get; set; } = "";
    public string SpanishTranslation { get; set; } = "";
    public string ExampleSentence { get; set; } = "";
    public string Pronunciation { get; set; } = "";
    public int TimesEncountered { get; set; } = 1;
    public int TimesReviewed { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public VocabularyLevel Level { get; set; } = VocabularyLevel.New;
    public List<int> SessionIds { get; set; } = new();
}

public enum VocabularyLevel { New, Learning, Familiar, Mastered }
```

---

### 5. Mejora de Arquitectura (Refactoring)

**Objetivo**: Separar responsabilidades y preparar para escalabilidad.

#### Extraer ViewModels (MVVM basico)

**Archivos nuevos**:
- `ViewModels/MainViewModel.cs` - Logica de MainWindow
- `ViewModels/SessionViewModel.cs` - Logica de sesiones
- `ViewModels/AssistantViewModel.cs` - Logica del asistente

Esto no es una refactorizacion completa a MVVM (seria demasiado trabajo sin beneficio inmediato), sino extraer la logica pesada del code-behind:

```csharp
// ViewModels/MainViewModel.cs
public class MainViewModel : INotifyPropertyChanged
{
    private readonly SessionService _sessionService;
    private readonly QuestionDetectionService _questionDetector;
    private readonly OllamaService _ollamaService;

    public ObservableCollection<TranscriptionEntry> Entries { get; }
    public Session? CurrentSession { get; set; }
    public bool IsSessionActive => CurrentSession?.Status == SessionStatus.Active;
    public QuestionAnalysis? LastDetectedQuestion { get; set; }

    // Comandos
    public ICommand NewSessionCommand { get; }
    public ICommand SaveSessionCommand { get; }
    public ICommand ExportSessionCommand { get; }

    // Metodos principales
    public async Task ProcessNewText(string text, EntrySource source);
    public async Task AnalyzeForQuestions(TranscriptionEntry entry);
}
```

---

## Orden de Implementacion

### Fase 1: Fundamentos de Datos (Prioridad Alta)
| # | Tarea | Archivos | Esfuerzo |
|---|---|---|---|
| 1.1 | Crear modelos de datos | `Models/Session.cs`, `Models/TranscriptionEntry.cs`, `Models/DetectedQuestion.cs`, `Models/SessionMetadata.cs` | Bajo |
| 1.2 | Configurar SQLite con EF Core | `Data/AppDbContext.cs`, `.csproj` | Medio |
| 1.3 | Implementar `SessionService` | `Services/SessionService.cs` | Medio |
| 1.4 | Integrar sesiones en MainWindow | `MainWindow.xaml`, `MainWindow.xaml.cs` | Alto |
| 1.5 | Auto-guardado y persistencia | `Services/SessionService.cs` | Bajo |

### Fase 2: Deteccion de Preguntas (Prioridad Alta)
| # | Tarea | Archivos | Esfuerzo |
|---|---|---|---|
| 2.1 | Crear `QuestionDetectionService` | `Services/QuestionDetectionService.cs` | Medio |
| 2.2 | Implementar deteccion por patron (Niveles 1-3) | `Services/QuestionDetectionService.cs` | Medio |
| 2.3 | Integrar verificacion IA (Nivel 4) | `Services/QuestionDetectionService.cs`, `Services/OllamaService.cs` | Medio |
| 2.4 | Conectar al flujo de captura | `MainWindow.xaml.cs` | Medio |
| 2.5 | Indicadores visuales de pregunta | `MainWindow.xaml` | Bajo |

### Fase 3: Asistente IA Mejorado (Prioridad Alta)
| # | Tarea | Archivos | Esfuerzo |
|---|---|---|---|
| 3.1 | Nuevo prompt contextual para preguntas | `Services/OllamaService.cs` | Bajo |
| 3.2 | Panel integrado de asistente | `MainWindow.xaml` | Medio |
| 3.3 | Auto-activacion por preguntas | `MainWindow.xaml.cs` | Medio |
| 3.4 | Opciones de respuesta (Simple/Standard/Detailed) | `MainWindow.xaml.cs` | Bajo |
| 3.5 | Copiar respuesta al portapapeles | `MainWindow.xaml.cs` | Bajo |

### Fase 4: Gestion de Sesiones UI (Prioridad Media)
| # | Tarea | Archivos | Esfuerzo |
|---|---|---|---|
| 4.1 | Panel de sesiones guardadas | `MainWindow.xaml` | Medio |
| 4.2 | Dialogo nueva sesion | `MainWindow.xaml`, `MainWindow.xaml.cs` | Bajo |
| 4.3 | Cargar sesion anterior | `MainWindow.xaml.cs` | Medio |
| 4.4 | Exportar sesion (Markdown / JSON) | `Services/SessionService.cs` | Medio |
| 4.5 | Busqueda en sesiones | `Services/SessionService.cs`, `MainWindow.xaml` | Medio |

### Fase 5: Vocabulario y Progreso (Prioridad Media)
| # | Tarea | Archivos | Esfuerzo |
|---|---|---|---|
| 5.1 | Modelo de vocabulario | `Models/VocabularyItem.cs` | Bajo |
| 5.2 | Extraccion automatica de vocabulario | `Services/VocabularyService.cs` | Medio |
| 5.3 | Vista de vocabulario | Nueva ventana o panel | Medio |
| 5.4 | Estadisticas de progreso | `Services/VocabularyService.cs` | Medio |

### Fase 6: Refactoring (Prioridad Baja)
| # | Tarea | Archivos | Esfuerzo |
|---|---|---|---|
| 6.1 | Extraer `MainViewModel` | `ViewModels/MainViewModel.cs` | Alto |
| 6.2 | Extraer `AssistantViewModel` | `ViewModels/AssistantViewModel.cs` | Medio |
| 6.3 | Inyeccion de dependencias basica | `App.xaml.cs` | Medio |

---

## Estructura de Archivos Propuesta

```
WindowsLiveCaptionsReader/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs          (modificado)
├── AssistantWindow.xaml / AssistantWindow.xaml.cs (deprecar en Fase 3)
│
├── Models/                          [NUEVO]
│   ├── Session.cs
│   ├── TranscriptionEntry.cs
│   ├── DetectedQuestion.cs
│   ├── SessionMetadata.cs
│   └── VocabularyItem.cs
│
├── Data/                            [NUEVO]
│   └── AppDbContext.cs
│
├── ViewModels/                      [NUEVO - Fase 6]
│   ├── MainViewModel.cs
│   ├── SessionViewModel.cs
│   └── AssistantViewModel.cs
│
├── Services/
│   ├── CaptionReader.cs             (sin cambios)
│   ├── AudioCaptureService.cs       (sin cambios)
│   ├── AudioRecorderService.cs      (sin cambios)
│   ├── WhisperService.cs            (sin cambios)
│   ├── OllamaService.cs             (modificado - nuevos prompts)
│   ├── BrowserCaptureService.cs     (sin cambios)
│   ├── ChromeSessionService.cs      (sin cambios)
│   ├── SessionService.cs            [NUEVO]
│   ├── QuestionDetectionService.cs  [NUEVO]
│   └── VocabularyService.cs         [NUEVO]
│
├── Utils/
│   ├── LiveCaptionsHandler.cs       (sin cambios)
│   ├── RegexPatterns.cs             (agregar patrones de preguntas)
│   ├── TextUtil.cs                  (sin cambios)
│   └── Icons.cs                     (agregar nuevos iconos)
│
├── Apis/
│   └── WindowsAPI.cs               (sin cambios)
│
├── Migrations/                      [NUEVO - auto-generado por EF Core]
│   └── ...
│
└── WindowsLiveCaptionsReader.csproj (modificado)
```

---

## Dependencias Nuevas

```xml
<!-- Agregar a WindowsLiveCaptionsReader.csproj -->

<!-- Base de datos SQLite para persistencia de sesiones -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">
    <PrivateAssets>all</PrivateAssets>
</PackageReference>

<!-- JSON para exportacion (ya incluido en .NET 8, solo confirmar) -->
<!-- System.Text.Json viene con el framework -->
```

---

## Flujo Completo Propuesto (Post-Implementacion)

```
┌─────────────────────────────────────────────────────────────────┐
│                    INICIO DE APLICACION                         │
├─────────────────────────────────────────────────────────────────┤
│  1. Cargar/Crear Sesion                                        │
│     ├─ "Nueva Sesion" → Crear sesion en SQLite                 │
│     └─ "Continuar Sesion" → Cargar sesion anterior             │
│                                                                 │
│  2. Captura de Audio (paralelo)                                │
│     ├─ Live Captions (automatico)                              │
│     ├─ Microfono (reconocimiento de voz)                       │
│     └─ Grabacion manual → Whisper                              │
│                                                                 │
│  3. Procesamiento de Texto                                     │
│     ├─ Limpieza (regex, normalizacion)                         │
│     ├─ Traduccion streaming (Ollama)                           │
│     ├─ Deteccion de preguntas (cascada 4 niveles)              │
│     └─ Extraccion de vocabulario                               │
│                                                                 │
│  4. Si pregunta detectada (confianza >= 0.75):                 │
│     ├─ Clasificar tipo de pregunta                             │
│     ├─ Activar panel de asistente automaticamente              │
│     ├─ Generar 3 opciones de respuesta (Simple/Standard/Full)  │
│     ├─ Mostrar con traduccion y tips gramaticales              │
│     └─ Permitir copiar respuesta al portapapeles               │
│                                                                 │
│  5. Si pregunta detectada (confianza 0.5 - 0.75):             │
│     ├─ Mostrar indicador visual sutil                          │
│     └─ Click para activar asistente manualmente                │
│                                                                 │
│  6. Persistencia continua                                      │
│     ├─ Auto-guardado cada 30 segundos                          │
│     ├─ Cada entrada guardada en SQLite                         │
│     └─ Vocabulario actualizado automaticamente                 │
│                                                                 │
│  7. Fin de sesion                                              │
│     ├─ Generar resumen automatico                              │
│     ├─ Guardar estadisticas (preguntas, vocabulario, duracion) │
│     └─ Opcion de exportar (Markdown / JSON)                    │
└─────────────────────────────────────────────────────────────────┘
```

---

## Detalle Tecnico: Integracion de Deteccion de Preguntas

### Flujo en `MainWindow.xaml.cs`

```csharp
// Modificacion del metodo DebounceTimer_Tick existente
private async void DebounceTimer_Tick(object? sender, EventArgs e)
{
    _debounceTimer.Stop();
    var text = _pendingText;
    if (string.IsNullOrWhiteSpace(text)) return;

    // 1. Crear entrada de transcripcion
    var entry = new TranscriptionEntry
    {
        OriginalText = text,
        Timestamp = DateTime.Now,
        Source = _currentSource,
        SessionId = _currentSession.Id
    };

    // 2. Traducir (existente, sin cambios)
    _translationCts?.Cancel();
    _translationCts = new CancellationTokenSource();
    try
    {
        var translation = await _ollama.TranslateStreamAsync(
            text,
            partial => Dispatcher.Invoke(() => TranslatedTextBlock.Text = partial),
            _translationCts.Token
        );
        entry.TranslatedText = translation;
    }
    catch (OperationCanceledException) { return; }

    // 3. NUEVO: Detectar preguntas
    var recentContext = _currentSession.Entries.TakeLast(5).ToList();
    var analysis = _questionDetector.AnalyzeWithContext(text, recentContext);

    if (analysis.IsQuestion)
    {
        entry.ContainsQuestion = true;

        var question = new DetectedQuestion
        {
            SessionId = _currentSession.Id,
            EntryId = entry.Id,
            QuestionText = analysis.ExtractedQuestion,
            Context = string.Join("\n", recentContext.Select(e => e.OriginalText)),
            Type = analysis.Type,
            DetectedAt = DateTime.Now
        };

        // Verificacion IA para casos ambiguos
        if (analysis.Confidence < 0.7 && analysis.Confidence >= 0.5)
        {
            var aiAnalysis = await _questionDetector.AnalyzeWithAIAsync(
                text, question.Context);
            analysis = aiAnalysis;
        }

        if (analysis.Confidence >= 0.75 && analysis.RequiresResponse)
        {
            // Auto-activar asistente
            await ActivateAssistantForQuestion(question, recentContext);
        }
        else if (analysis.Confidence >= 0.5)
        {
            // Indicador visual sutil
            ShowQuestionIndicator(analysis);
        }

        _currentSession.Questions.Add(question);
    }

    // 4. Guardar entrada
    _currentSession.Entries.Add(entry);
    AddToHistory(entry);
    await _sessionService.SaveEntryAsync(entry);

    // 5. Extraer vocabulario (asincrono, no bloquea UI)
    _ = _vocabularyService.ExtractAndSaveAsync(text, _currentSession.Id);
}

private async Task ActivateAssistantForQuestion(
    DetectedQuestion question,
    List<TranscriptionEntry> context)
{
    Dispatcher.Invoke(() =>
    {
        // Mostrar panel de asistente
        AssistantPanel.Visibility = Visibility.Visible;
        DetectedQuestionText.Text = question.QuestionText;
        QuestionTypeBadge.Text = question.Type.ToString();
        AssistantStatus.Text = "Generating responses...";
    });

    // Generar respuestas contextuales
    var response = await _ollama.GenerateQuestionResponseAsync(
        question, context, _translationCts.Token);

    Dispatcher.Invoke(() =>
    {
        SuggestionsContent.Text = response;
        AssistantStatus.Text = "Ready";
        question.SuggestedAnswer = response;
        question.WasAnswered = true;
    });
}
```

---

## Metricas de Exito

| Metrica | Valor Objetivo |
|---|---|
| Deteccion de preguntas directas ("?") | 99% precision |
| Deteccion de preguntas Wh- sin "?" | 90% precision |
| Deteccion de preguntas indirectas | 75% precision |
| Tiempo de activacion del asistente | < 2 segundos |
| Tiempo de generacion de respuestas | < 5 segundos |
| Auto-guardado sin afectar rendimiento | < 50ms por guardado |
| Sesiones cargables sin perdida de datos | 100% integridad |

---

## Notas Tecnicas

### Rendimiento
- La deteccion de preguntas (Niveles 1-3) es puramente basada en regex/patrones, sin overhead de IA.
- Solo el Nivel 4 (verificacion IA) usa Ollama, y solo para casos ambiguos (confianza 0.5-0.7).
- El auto-guardado usa `SaveChangesAsync()` de EF Core, que es eficiente para cambios incrementales.
- El panel de asistente es un control integrado (no ventana separada), eliminando overhead de crear/destruir ventanas.

### Compatibilidad
- SQLite no requiere instalacion adicional (bundled con EF Core).
- Todas las dependencias nuevas son compatibles con .NET 8.0 y publicacion self-contained.
- No hay cambios en los requisitos del sistema (Windows 10/11, Ollama).

### Migracion de Datos
- El `conversation_history.log` existente puede importarse a SQLite con un script de migracion unica.
- La primera ejecucion con la nueva version creara la base de datos automaticamente.
