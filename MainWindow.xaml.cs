using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsLiveCaptionsReader.Services;
using WindowsLiveCaptionsReader.Models;

namespace WindowsLiveCaptionsReader
{
    public partial class MainWindow : Window
    {
        private CaptionReader _reader;
        private OllamaService _translator;
        private AudioCaptureService _micService;
        
        // New Services for Manual Mode
        private AudioRecorderService _audioRecorder;
        private WhisperService _whisper;
        
        // Session Management
        private SessionService _sessionService;
        private Session _currentSession;

        private QuestionDetectionService _questionService;
        private VocabularyService _vocabularyService;

        
        // State flags
        private bool _isPaused = false;
        private bool _isMicActive = false;
        private bool _isRecordingMode = false; // False = Real-time (Live), True = Manual Recording
        
        // Debouncing logic
        private DispatcherTimer _debounceTimer;
        private CancellationTokenSource? _translationCts;
        private string _pendingText = "";
        
        // Recording state
        private string _tempAudioFile;
        
        public ObservableCollection<TranslationItem> History { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            
            History = new ObservableCollection<TranslationItem>();
            HistoryList.ItemsSource = History;

            _reader = new CaptionReader();
            _translator = new OllamaService("llama3.2");
            _micService = new AudioCaptureService();
            
            try
            {
                // Initialize new services
                _audioRecorder = new AudioRecorderService();
                _whisper = new WhisperService();
                _sessionService = new SessionService();
                _questionService = new QuestionDetectionService(_translator);
                _vocabularyService = new VocabularyService(_translator);
                _tempAudioFile = Path.Combine(Path.GetTempPath(), "ela_recording.wav");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing services: {ex.Message}\n\nStack: {ex.StackTrace}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Handle or rethrow if fatal
            }

            _reader.TextChanged += Reader_TextChanged;
            _reader.StatusChanged += Reader_StatusChanged;
            
            _micService.TextCaptured += (s, text) => Reader_TextChanged(s, text);
            _micService.StatusChanged += (s, status) => 
            {
                Dispatcher.Invoke(() => {
                     // Only show mic status in Live mode
                     if (!_isRecordingMode && _isMicActive) StatusText.Text = $"Mic: {status}";
                });
            };
            
            _micService.AudioLevelChanged += (s, level) =>
            {
                Dispatcher.Invoke(() => {
                    if (MicLevelBar != null) MicLevelBar.Value = level;
                });
            };
            
            // Debounce timer initialization
            _debounceTimer = new DispatcherTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(500); // Increased to 500ms for sentence break
            _debounceTimer.Tick += DebounceTimer_Tick;

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try 
            {
                _reader.Start();
                
                // Init database services
                if (_sessionService != null) await _sessionService.InitializeAsync();
                if (_vocabularyService != null) await _vocabularyService.InitializeAsync();
                
                await EnsureServicesAreRunning();
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Error during async initialization: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            await CreateNewSessionAsync();
            
            // Pre-load Whisper model in background to avoid delays later
            _ = Task.Run(async () => 
            {
                try 
                {
                    await _whisper.InitializeAsync();
                    Dispatcher.Invoke(() => StatusText.Text = "Whisper listo ✓");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Whisper init error: {ex.Message}");
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _reader.Stop();
            _micService.StopListening();
            _audioRecorder.Dispose();
            _whisper.Dispose();
            _sessionService.Dispose();
            _vocabularyService.Dispose();
            _translationCts?.Cancel();
            base.OnClosed(e);
        }

        private async Task EnsureServicesAreRunning()
        {
             // 1. Check Ollama Status
             StatusText.Text = "Checking services...";
             bool isRunning = await _translator.IsRunningAsync();
             
             if (!isRunning)
             {
                 StatusText.Text = "Starting Ollama...";
                 TranslatedTextBlock.Text = "Attempting to start local Ollama server...";
                 
                 bool started = _translator.StartServer();
                 if (started)
                 {
                     // Wait for it to spin up
                     for(int i=0; i<5; i++)
                     {
                         await Task.Delay(1000); // Wait 1s
                         if (await _translator.IsRunningAsync())
                         {
                             isRunning = true;
                             break;
                         }
                     }
                 }
             }

             if (!isRunning)
             {
                 StatusText.Text = "Ollama Error";
                 TranslatedTextBlock.Text = "Could not start Ollama. Please run 'ollama serve' manually.";
             }
             else
             {
                 StatusText.Text = "Ready";
                 TranslatedTextBlock.Text = ""; // Clear warning
             }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl + Space -> Toggle Assistant
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Copilot_Click(this, new RoutedEventArgs());
            }
            // Esc -> Close Settings or Overlay
            if (e.Key == Key.Escape)
            {
                if (SuggestionsOverlay.Visibility == Visibility.Visible)
                    SuggestionsOverlay.Visibility = Visibility.Collapsed;
                else if (SettingsPanel.Visibility == Visibility.Visible)
                     SettingsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _reader.Stop();
        }

        private void Reader_StatusChanged(object sender, string e)
        {
            Dispatcher.Invoke(() => 
            {
                StatusText.Text = e;
            });
        }

        // Source tracking
        private string _pendingSourceIcon = "\uE7F4"; // CC
        private string _pendingSourceColor = "#CCCCFF"; 

        private void Reader_TextChanged(object sender, string text)
        {
            if (_isPaused || string.IsNullOrWhiteSpace(text)) return;

            // Determine source based on sender
            string sourceIcon = "\uE7F4"; // CC
            string sourceColor = "#CCCCFF"; // Light Blue for System

            if (sender is AudioCaptureService)
            {
                sourceIcon = "\uE720"; // Mic
                sourceColor = "#90EE90"; // Light Green for Mic
            }

            Dispatcher.Invoke(() => 
            {
                // Visual feedback immediate
                OriginalTextBlock.Text = text;
                TranslatedTextBlock.Text = "..."; 
                if (sender is AudioCaptureService) StatusText.Text = "Listening (Mic)...";
                else StatusText.Text = "Listening (Sys)...";
                
                // Restart debounce timer
                _pendingText = text;
                _pendingSourceIcon = sourceIcon;
                _pendingSourceColor = sourceColor;

                _debounceTimer.Stop();
                _debounceTimer.Start();
            });
        }

        private async void DebounceTimer_Tick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            string textToTranslate = _pendingText;
            string currentIcon = _pendingSourceIcon;
            string currentColor = _pendingSourceColor;

            // Update UI status
            StatusText.Text = "Translating...";

            // Cancel previous translation if any
            _translationCts?.Cancel();
            _translationCts = new CancellationTokenSource();

            try
            {
                // Streaming call (No context, just translate)
                string finalTranslation = await _translator.TranslateStreamAsync(
                    textToTranslate, 
                    onPartialUpdate: (partial) => 
                    {
                        Dispatcher.Invoke(() => 
                        {
                            TranslatedTextBlock.Text = partial;
                            TranslatedTextBlock.Foreground = System.Windows.Media.Brushes.LightGray;
                        });
                    },
                    historyContext: null,
                    token: _translationCts.Token);
                
                if (!_translationCts.Token.IsCancellationRequested)
                {
                    if (finalTranslation.StartsWith("[Error"))
                    {
                        TranslatedTextBlock.Text = finalTranslation;
                        TranslatedTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                        StatusText.Text = "Translation Error";
                    }
                    else if (!string.IsNullOrWhiteSpace(finalTranslation))
                    {
                        TranslatedTextBlock.Text = finalTranslation;
                        StatusText.Text = "Done";
                        
                        // Add to history
                        AddToHistory(textToTranslate, finalTranslation, currentIcon, currentColor);
                        
                        // Save to DB
                        var entry = new TranscriptionEntry
                        {
                            SessionId = _currentSession?.Id ?? 0,
                            OriginalText = textToTranslate,
                            TranslatedText = finalTranslation,
                            Timestamp = DateTime.Now,
                            Source = currentIcon == "\uE7F4" ? EntrySource.LiveCaption : EntrySource.Microphone, 
                            ConfidenceScore = 1.0f 
                        };
                        
                        if (_currentSession != null)
                        {
                            _currentSession.Entries.Add(entry);
                            // Fire and forget save
                            _ = _sessionService.SaveEntryAsync(entry);
                        }

                        // --- FASE 2: QUESTION DETECTION & AUTO-TRIGGER ---
                        // Only auto-trigger if it came from "Teacher" (System)
                        if (currentIcon == "\uE7F4")
                        {
                            var detectedQuestion = await _questionService.AnalyzeTextAsync(textToTranslate, _currentSession?.Id ?? 0, entry.Id);
                            
                            if (detectedQuestion != null)
                            {
                                entry.ContainsQuestion = true;
                                if (_currentSession != null)
                                {
                                    _currentSession.Questions.Add(detectedQuestion);
                                    _ = _sessionService.SaveQuestionAsync(detectedQuestion);
                                }
                                
                                // Show visual cue (optional console log for now)
                                System.Diagnostics.Debug.WriteLine($"Question Detected: {detectedQuestion.Type} - {detectedQuestion.QuestionText}");

                                // Auto-trigger Assistant (Phase 3.3)
                                Dispatcher.Invoke(() => 
                                {
                                    string context = GetRecentContext();
                                    
                                    if (_assistantWindow == null || !_assistantWindow.IsLoaded)
                                    {
                                        _assistantWindow = new AssistantWindow(_translator, context, this);
                                        _assistantWindow.Show();
                                    }
                                    else
                                    {
                                        _assistantWindow.Activate();
                                    }

                                    // Trigger specific question mode
                                    _assistantWindow.UpdateContext(context, false);
                                    _assistantWindow.ShowQuestionResponse(detectedQuestion.QuestionText, context);
                                });
                            }
                        }
                    }
                    else
                    {
                        // Fallback if empty
                        TranslatedTextBlock.Text = "(Sin traducción)"; 
                        StatusText.Text = "Empty Response";
                        AddToHistory(textToTranslate, "(Sin traducción)", currentIcon, currentColor);
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                StatusText.Text = "System Error";
                TranslatedTextBlock.Text = $"Exception: {ex.Message}";
                TranslatedTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }



        private void AddToHistory(string original, string translated, string icon = "\uE7F4", string color = "White")
        {
            var newItem = new TranslationItem 
            { 
                OriginalText = original, 
                TranslatedText = translated,
                Timestamp = DateTime.Now,
                SourceIcon = icon,
                SourceColor = color
            };

            History.Add(newItem);
            
            // Auto scroll
            HistoryScrollViewer.ScrollToBottom();

            // Persist to file
            AppendToLog(newItem);
        }

        private void AppendToLog(TranslationItem item)
        {
            try
            {
                string logLine = $"{item.Timestamp:yyyy-MM-dd HH:mm:ss} | {item.OriginalText} | {item.TranslatedText}{Environment.NewLine}";
                System.IO.File.AppendAllText("conversation_history.log", logLine);
            }
            catch { /* Best effort logging */ }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MainBorder != null)
            {
               // Convert slider 0-1 to hex alpha? No, Border Background is solid, we set Opacity of the whole border/window background?
               // Actually changing the brush alpha is better for glass look.
               // Assuming #CC000000 base (204 alpha). 
               // Let's just control the Window Background or Border Background opacity.
               // Simplest: Control Border Background Opacity.
               
               byte alpha = (byte)(e.NewValue * 255);
               MainBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 0, 0, 0));
            }
        }

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                SettingsPanel.Visibility = Visibility.Visible;
                LoadMicrophones();
            }
        }

        private void LoadMicrophones()
        {
            var devices = _micService.GetMicrophones();
            ComboMicrophones.ItemsSource = devices;
            ComboMicrophones.DisplayMemberPath = "Name";
            // Select default if not set (simple logic for now)
            if (ComboMicrophones.SelectedIndex < 0 && devices.Count > 0)
                ComboMicrophones.SelectedIndex = 0;
        }

        private void ComboMicrophones_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
             if (ComboMicrophones.SelectedItem is AudioCaptureService.AudioDevice device)
             {
                 _micService.SetDevice(device.Index);
             }
        }

        private async void TestSystem_Click(object sender, RoutedEventArgs e)
        {
             TestResultText.Text = "Running diagnostics...";
             
             // 1. Ollama Check
             bool ollamaOk = await _translator.IsRunningAsync();
             
             // 2. Mic Check
             bool micOk = ComboMicrophones.SelectedItem != null;
             
             var sb = new System.Text.StringBuilder();
             sb.AppendLine(ollamaOk ? "✅ Ollama: Online" : "❌ Ollama: Offline");
             sb.AppendLine(micOk ? $"✅ Mic: Selected" : "⚠️ Mic: None selected");
             
             // 3. Audio Level Check (User must speak)
             sb.AppendLine("ℹ️ Speak now to see Mic Level bar move.");
             
             TestResultText.Text = sb.ToString();
             TestResultText.Foreground = ollamaOk ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Orange;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
        }

        private void Topmost_Checked(object sender, RoutedEventArgs e)
        {
            this.Topmost = true;
        }

        private void Topmost_Unchecked(object sender, RoutedEventArgs e)
        {
            this.Topmost = false;
        }

        private void CloseApp_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private AssistantWindow? _assistantWindow;

        private void Copilot_Click(object sender, RoutedEventArgs e)
        {
            // Gather context from history (last 5 items)
            var recentItems = History.TakeLast(5).Select(h => 
                $"{(h.SourceIcon == "\uE7F4" ? "Teacher" : "Me")}: {h.OriginalText}"
            ).ToList();
            
            string context = string.Join("\n", recentItems);

            if (_assistantWindow == null || !_assistantWindow.IsLoaded)
            {
                _assistantWindow = new AssistantWindow(_translator, context, this);
                _assistantWindow.Show();
            }
            else
            {
                _assistantWindow.UpdateContext(context);
                _assistantWindow.Activate();
            }
        }
        
        private void DismissCopilot_Click(object sender, RoutedEventArgs e)
        {
            // Legacy handler kept for binding safety if XAML still references it, 
            // though we are hiding the overlay via this new logic being separate.
            SuggestionsOverlay.Visibility = Visibility.Collapsed;
        }

        // --- SIMPLIFIED UX: PRIMARY AND SECONDARY ACTION BUTTONS ---
        
        private void PrimaryAction_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isRecordingMode)
            {
                // RECORDING MODE: Primary button handles Record/Stop
                if (!_audioRecorder.IsRecording)
                {
                    // Start Recording
                    StartRecording();
                }
                else
                {
                    // Stop and Analyze
                    StopAndAnalyze();
                }
            }
            else
            {
                // LIVE MODE: Primary button handles Pause/Resume
                _isPaused = !_isPaused;
                if (_isPaused)
                {
                    _reader.Stop();
                    UpdatePrimaryButton("▶️", "REANUDAR ESCUCHA", "#FF9800");
                    StatusText.Text = "Pausado";
                }
                else
                {
                    _reader.Start();
                    UpdatePrimaryButton("⏸️", "PAUSAR ESCUCHA", "#4CAF50");
                    StatusText.Text = "Escuchando...";
                }
            }
        }
        
        private void SecondaryAction_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle between Live and Recording modes
            _isRecordingMode = !_isRecordingMode;
            
            if (_isRecordingMode)
            {
                // Switch to RECORDING Mode
                SwitchToRecordingMode();
            }
            else
            {
                // Switch to LIVE Mode
                SwitchToLiveMode();
            }
        }
        
        // Helper methods for UI updates
        private void UpdatePrimaryButton(string icon, string text, string color)
        {
            PrimaryButtonIcon.Text = icon;
            PrimaryButtonText.Text = text;
            PrimaryButtonBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(color);
        }
        
        private void UpdateSecondaryButton(string icon, string text)
        {
            SecondaryButtonIcon.Text = icon;
            SecondaryButtonText.Text = text;
        }
        
        private void SwitchToRecordingMode()
        {
            // Stop live capturing
            _reader.Stop();
            if (_isMicActive) _micService.StopListening();
            
            // Update mode indicator
            ModeText.Text = "MODO GRABACIÓN";
            ModeIcon.Text = "🎙️";
            ModeIndicatorBadge.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#FF5722");
            
            // Update primary button
            UpdatePrimaryButton("🔴", "GRABAR AUDIO", "#FF5722");
            
            // Update secondary button
            UpdateSecondaryButton("⚡", "Volver a Modo Live");
            
            // Update status
            StatusText.Text = "Listo para grabar";
            OriginalTextBlock.Text = "Presiona 'GRABAR AUDIO' y habla sin límites";
            TranslatedTextBlock.Text = "";
            
            _isPaused = false;
        }
        
        private void SwitchToLiveMode()
        {
            // Resume live capturing
            _reader.Start();
            
            // Update mode indicator
            ModeText.Text = "MODO LIVE";
            ModeIcon.Text = "⚡";
            ModeIndicatorBadge.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#4CAF50");
            
            // Update primary button
            UpdatePrimaryButton("⏸️", "PAUSAR ESCUCHA", "#4CAF50");
            
            // Update secondary button
            UpdateSecondaryButton("🎙️", "Cambiar a Modo Grabación");
            
            // Update status
            StatusText.Text = "Escuchando...";
            RecordingTimer.Visibility = Visibility.Collapsed;
            
            _isPaused = false;
        }
        
        private async void StartRecording()
        {
            try 
            {
                _audioRecorder.StartRecording(_tempAudioFile);
                
                // Update UI
                UpdatePrimaryButton("⏹️", "DETENER Y ANALIZAR", "#E64A19");
                RecordingTimer.Visibility = Visibility.Visible;
                StatusText.Text = "Grabando...";
                StatusText.Foreground = Brushes.Red;
                
                OriginalTextBlock.Text = "🔴 Grabando... Habla ahora";
                TranslatedTextBlock.Text = "";
                
                // Start timer (simple implementation)
                var startTime = DateTime.Now;
                var timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += (s, e) =>
                {
                    if (_audioRecorder.IsRecording)
                    {
                        var elapsed = DateTime.Now - startTime;
                        RecordingTimer.Text = elapsed.ToString(@"mm\:ss");
                    }
                    else
                    {
                        timer.Stop();
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error al iniciar micrófono";
                MessageBox.Show($"Error: {ex.Message}");
            }
        }
        
        private async void StopAndAnalyze()
        {
            if (_audioRecorder.IsRecording)
            {
                // 1. Stop Recording
                _audioRecorder.StopRecording();
                RecordingTimer.Visibility = Visibility.Collapsed;
                StatusText.Text = "Procesando audio...";
                StatusText.Foreground = Brushes.Cyan;
                
                UpdatePrimaryButton("🔴", "GRABAR AUDIO", "#FF5722");
                
                try
                {
                    // 2. Transcribe with Whisper (Local)
                    OriginalTextBlock.Text = "📝 Transcribiendo con Whisper...";
                    TranslatedTextBlock.Text = "";
                    
                    if (!_whisper.IsModelLoaded)
                    {
                         StatusText.Text = "Descargando modelo Whisper...";
                         await _whisper.InitializeAsync();
                    }
                    
                    string transcription = await _whisper.TranscribeAsync(_tempAudioFile);
                    
                    if (string.IsNullOrWhiteSpace(transcription))
                    {
                        StatusText.Text = "No se detectó voz";
                        OriginalTextBlock.Text = "No se detectó voz en la grabación";
                        return;
                    }

                    // 3. Translate & Show
                    OriginalTextBlock.Text = transcription;
                    StatusText.Text = "Traduciendo...";
                    
                    // Translate with Ollama
                    string translation = await _translator.TranslateAsync(transcription);
                    TranslatedTextBlock.Text = translation;
                    
                    // Add to history
                    AddToHistory(transcription, translation, "\uE720", "#FF4CAF50"); // Mic icon
                    
                    StatusText.Text = "Análisis listo";
                    StatusText.Foreground = Brushes.SpringGreen;
                    
                    // 4. Open Assistant for Context
                    Copilot_Click(null, null);
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Error al procesar";
                    MessageBox.Show($"Error: {ex.Message}");
                    OriginalTextBlock.Text = "Error al procesar el audio";
                }
            }
        }
        
        // Keep old methods for compatibility but mark as deprecated
        private void ModeSwitch_Click(object sender, RoutedEventArgs e)
        {
            // Redirect to new secondary action logic
            _isRecordingMode = !_isRecordingMode;
            if (_isRecordingMode) SwitchToRecordingMode();
            else SwitchToLiveMode();
        }

        private async void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            // Redirect to unified primary action logic
            if (_isRecordingMode)
            {
                if (!_audioRecorder.IsRecording) StartRecording();
                else StopAndAnalyze();
            }
            else
            {
                _isPaused = !_isPaused;
                if (_isPaused)
                {
                    _reader.Stop();
                    UpdatePrimaryButton("▶️", "REANUDAR ESCUCHA", "#FF9800");
                    StatusText.Text = "Pausado";
                }
                else
                {
                    _reader.Start();
                    UpdatePrimaryButton("⏸️", "PAUSAR ESCUCHA", "#4CAF50");
                    StatusText.Text = "Escuchando...";
                }
            }
        }
        
        private async void StopAndSend_Click(object sender, RoutedEventArgs e)
        {
            // Redirect to unified stop and analyze
            StopAndAnalyze();
        }
        
        private void MicToggle_Click(object sender, RoutedEventArgs e)
        {
            _isMicActive = !_isMicActive;
            
            if (_isMicActive)
            {
                _micService.Start();
                StatusText.Text = "Micrófono activo";
            }
            else
            {
                _micService.Stop();
                StatusText.Text = "Micrófono desactivado";
            }
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            History.Clear();
            OriginalTextBlock.Text = "Listening...";
            TranslatedTextBlock.Text = "";
        }

        // UpdatePauseButtonState removed - functionality moved to PrimaryAction_Click

        public string GetRecentContext()
        {
            var recentItems = History.TakeLast(5).Select(h => 
                $"{(h.SourceIcon == "\uE7F4" ? "Teacher" : "Me")}: {h.OriginalText}"
            ).ToList();
            
            // Also include pending text if any
            if (!string.IsNullOrWhiteSpace(_pendingText))
            {
               string source = (_pendingSourceIcon == "\uE7F4") ? "Teacher" : "Me";
               recentItems.Add($"{source}: {_pendingText}");
            }
            
            return string.Join("\n", recentItems);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
        private Services.BrowserCaptureService _browserScanner = new Services.BrowserCaptureService();
        private Services.ChromeSessionService _chromeService = new Services.ChromeSessionService();

        private async void BrowserScan_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "🌐 Connecting...";
            
            // 1. Try High-Fidelity CDP Connection (Selenium)
            string text = "";
            bool isCdpConnected = _chromeService.ConnectToExistingSession();

            if (isCdpConnected)
            {
                StatusText.Text = "⚡ CDP Connected! Scanning DOM...";
                text = _chromeService.CaptureActiveTabContent();
                _chromeService.Disconnect(); // Free resources, don't close browser
            }
            else
            {
                // 2. Fallback to UI Automation (The "Old" Way)
                StatusText.Text = "⚠️ Standard Scan (Run .bat for HD)...";
                await Task.Delay(2000); // Wait for switch only if using UI Automation (which relies on focus)
                text = await _browserScanner.GetSelectedTextAsync();
            }

            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("Error") || text.StartsWith("Debug"))
            {
                StatusText.Text = isCdpConnected ? "❌ CDP Empty Result" : "⚠️ Legacy Scan Failed.";
                
                // Show hint if legacy failed
                if (!isCdpConnected) 
                {
                     // Optional: MessageBox.Show("For better results, close Chrome and run 'LANZAR_MODO_EXAMEN.bat' from the project folder.", "Tip", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                StatusText.Text = "✅ Captured!";
                
                if (_assistantWindow == null || !_assistantWindow.IsLoaded)
                {
                    _assistantWindow = new AssistantWindow(_translator, $"[SOURCE: {(isCdpConnected ? "CHROME_DOM" : "SCREEN_READER")}]\n{text}", this);
                    _assistantWindow.Show();
                }
                else
                {
                    _assistantWindow.UpdateContext($"[SOURCE: {(isCdpConnected ? "CHROME_DOM" : "SCREEN_READER")}]\n{text}", true);
                    _assistantWindow.Activate();
                    if (_assistantWindow.WindowState == WindowState.Minimized) _assistantWindow.WindowState = WindowState.Normal;
                }
            }
        }

        private async void GenerateSummary_Click(object sender, RoutedEventArgs e)
        {
            if (History.Count == 0)
            {
                StatusText.Text = "No history to summarize.";
                return;
            }

            StatusText.Text = "Generating Summary...";

            // Prepare transcript
            var sb = new System.Text.StringBuilder();
            foreach (var item in History)
            {
                 sb.AppendLine($"[{item.Timestamp:HH:mm}] {(item.SourceIcon == "\uE7F4" ? "Teacher" : "Student")}: {item.OriginalText}");
            }

            try
            {
                string summary = await _translator.GenerateSummaryAsync(sb.ToString());
                
                // Save to file
                string filename = $"Class_Summary_{DateTime.Now:yyyyMMdd_HHmm}.md";
                System.IO.File.WriteAllText(filename, summary);
                
                StatusText.Text = "Summary Saved!";
                
                // Open file automatically
                try {
                     System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = filename, UseShellExecute = true });
                } catch {}
            }
            catch (Exception ex)
            {
                 StatusText.Text = "Summary Failed";
                 MessageBox.Show(ex.Message);
            }
        }
        private async Task CreateNewSessionAsync()
        {
            string defaultTitle = $"Session {DateTime.Now:MMM dd, HH:mm}";
            _currentSession = await _sessionService.CreateSessionAsync(defaultTitle);
            
            Dispatcher.Invoke(() => 
            {
                CurrentSessionTitle.Text = _currentSession.Title;
                // Clear history if new session
                History.Clear();
                OriginalTextBlock.Text = "Listening...";
                TranslatedTextBlock.Text = "";
            });
        }

        private async void Sessions_Click(object sender, RoutedEventArgs e)
        {
            if (SessionPanel.Visibility == Visibility.Visible)
            {
                SessionPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Close other panels
                SettingsPanel.Visibility = Visibility.Collapsed;
                SuggestionsOverlay.Visibility = Visibility.Collapsed;
                
                SessionPanel.Visibility = Visibility.Visible;
                await RefreshSessionsList();
            }
        }
        
        private async Task RefreshSessionsList(string query = "")
        {
            var sessions = await _sessionService.SearchSessionsAsync(query);
            SessionsList.ItemsSource = sessions;
        }

        private async void NewSession_Click(object sender, RoutedEventArgs e)
        {
            // Save current if needed (autosave handles it, but maybe force save?)
            if (_currentSession != null)
            {
                await _sessionService.SaveSessionAsync(_currentSession);
            }
            
            await CreateNewSessionAsync();
            SessionPanel.Visibility = Visibility.Collapsed;
        }

        private void CloseSessions_Click(object sender, RoutedEventArgs e)
        {
            SessionPanel.Visibility = Visibility.Collapsed;
        }

        private async void SessionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SessionsList.SelectedItem is Session selectedSession)
            {
                // Load session
                var loaded = await _sessionService.LoadSessionAsync(selectedSession.Id);
                if (loaded != null)
                {
                    _currentSession = loaded;
                    _sessionService.StartAutoSave(_currentSession); // Switch auto-save to this session
                    
                    CurrentSessionTitle.Text = loaded.Title;
                    
                    // Populate History
                    History.Clear();
                    foreach(var entry in loaded.Entries.OrderBy(x => x.Timestamp))
                    {
                        AddToHistory(entry.OriginalText, entry.TranslatedText, 
                            entry.Source == EntrySource.Microphone ? "\uE720" : "\uE7F4", 
                            entry.Source == EntrySource.Microphone ? "#90EE90" : "#CCCCFF");
                    }
                    
                    SessionPanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void SearchSessionsBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchSessionsBox.Text == "Buscar...")
            {
                SearchSessionsBox.Text = "";
                SearchSessionsBox.Foreground = Brushes.White;
            }
        }

        private void SearchSessionsBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchSessionsBox.Text))
            {
                SearchSessionsBox.Text = "Buscar...";
                SearchSessionsBox.Foreground = Brushes.Gray;
            }
        }

        private async void SearchSessionsBox_KeyUp(object sender, KeyEventArgs e)
        {
             string query = SearchSessionsBox.Text == "Buscar..." ? "" : SearchSessionsBox.Text;
             await RefreshSessionsList(query);
        }

        private async void ExportSession_Click(object sender, RoutedEventArgs e)
        {
            Session sessionToExport = SessionsList.SelectedItem as Session;
            
            if (sessionToExport == null)
            {
                // Fallback to current session if valid
                if (_currentSession != null)
                {
                    if (MessageBox.Show("No session selected in list. Export current active session?", "Export", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        sessionToExport = _currentSession;
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("Please select a session to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Session to Markdown",
                Filter = "Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
                FileName = $"Session_{sessionToExport.StartTime:yyyyMMdd_HHmm}.md"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string content = await _sessionService.ExportToMarkdownAsync(sessionToExport.Id);
                    await File.WriteAllTextAsync(dialog.FileName, content);
                    MessageBox.Show("Export successful!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Vocabulary_Click(object sender, RoutedEventArgs e)
        {
            var vocabWin = new VocabularyWindow(_vocabularyService);
            vocabWin.Owner = this;
            vocabWin.ShowDialog();
        }
    }

    public class TranslationItem
    {
        public string OriginalText { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string SourceIcon { get; set; } = "\uE7F4"; // Default to CC (Captions)
        public string SourceColor { get; set; } = "White";
         // End of TranslationItem
    }
}
