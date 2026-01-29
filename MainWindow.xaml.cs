using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WindowsLiveCaptionsReader.Services;

namespace WindowsLiveCaptionsReader
{
    public partial class MainWindow : Window
    {
        private CaptionReader _reader;
        private OllamaService _translator;
        private AudioCaptureService _micService;
        
        // State flags
        private bool _isPaused = false;
        private bool _isMicActive = false;
        
        // Debouncing logic
        private DispatcherTimer _debounceTimer;
        private CancellationTokenSource? _translationCts;
        private string _pendingText = "";
        
        public ObservableCollection<TranslationItem> History { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            
            History = new ObservableCollection<TranslationItem>();
            HistoryList.ItemsSource = History;

            _reader = new CaptionReader();
            _translator = new OllamaService("llama3.2");
            _micService = new AudioCaptureService();

            _reader.TextChanged += Reader_TextChanged;
            _reader.StatusChanged += Reader_StatusChanged;
            
            _micService.TextCaptured += (s, text) => Reader_TextChanged(s, text);
            _micService.StatusChanged += (s, status) => 
            {
                Dispatcher.Invoke(() => {
                     // Prefix mic status to distinguish
                     if (_isMicActive) StatusText.Text = $"Mic: {status}";
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
            _reader.Start();
            await EnsureServicesAreRunning();
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
                 StatusIndicator.Fill = System.Windows.Media.Brushes.Red;
                 TranslatedTextBlock.Text = "Could not start Ollama. Please run 'ollama serve' manually.";
             }
             else
             {
                 StatusText.Text = "Ready";
                 StatusIndicator.Fill = System.Windows.Media.Brushes.SpringGreen;
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
                if (e.Contains("Error"))
                    StatusIndicator.Fill = System.Windows.Media.Brushes.Red;
                else if (e.Contains("Listening") && !_isPaused)
                    StatusIndicator.Fill = System.Windows.Media.Brushes.SpringGreen;
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
            StatusIndicator.Fill = System.Windows.Media.Brushes.Yellow;

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
                        StatusIndicator.Fill = System.Windows.Media.Brushes.Red;
                    }
                    else if (!string.IsNullOrWhiteSpace(finalTranslation))
                    {
                        TranslatedTextBlock.Text = finalTranslation;
                        StatusText.Text = "Done";
                        StatusIndicator.Fill = System.Windows.Media.Brushes.SpringGreen;
                        
                        // Add to history
                        AddToHistory(textToTranslate, finalTranslation, currentIcon, currentColor);
                        
                        // --- FASE 2: AUTO-TRIGGER ASSISTANT ---
                        // Only auto-trigger if it came from "Teacher" (System) and looks like a question
                        if (currentIcon == "\uE7F4" && IsPossibleQuestion(textToTranslate))
                        {
                            if (_assistantWindow != null && _assistantWindow.IsLoaded)
                            {
                                Dispatcher.Invoke(() => 
                                {
                                    string newContext = GetRecentContext();
                                    _assistantWindow.UpdateContext(newContext); // Auto-Refresh
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

        private bool IsPossibleQuestion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            
            // Hard check: Question mark
            if (text.EndsWith("?")) return true;
            
            // Soft check: Starts with question words (English)
            var questions = new[] { "what", "where", "when", "why", "who", "how", "do", "does", "did", "are", "is", "can", "could", "would", "will" };
            var firstWord = text.Split(' ')[0].ToLower();
            
            if (questions.Contains(firstWord) && text.Length > 10) return true; // Minimal length check
            
            return false;
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

        private void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            UpdatePauseButtonState();
            
           if (_isPaused)
            {
                StatusText.Text = "Paused";
                StatusIndicator.Fill = System.Windows.Media.Brushes.Orange;
            }
            else
            {
                StatusText.Text = "Listening...";
                StatusIndicator.Fill = System.Windows.Media.Brushes.SpringGreen;
            }
        }
        
        private void MicToggle_Click(object sender, RoutedEventArgs e)
        {
            _isMicActive = !_isMicActive;
            
            if (_isMicActive)
            {
                _micService.Start();
                BtnMic.Foreground = System.Windows.Media.Brushes.SpringGreen;
                BtnMic.ToolTip = "Disable Microphone";
            }
            else
            {
                _micService.Stop();
                BtnMic.Foreground = System.Windows.Media.Brushes.White;
                BtnMic.ToolTip = "Enable Microphone";
                StatusText.Text = "Listening (Captions)...";
            }
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            History.Clear();
            OriginalTextBlock.Text = "Listening...";
            TranslatedTextBlock.Text = "";
        }

        private void UpdatePauseButtonState()
        {
             // Icon updates are handled in XAML via binding or style triggers usually, 
             // but for direct code behind update if we named the button content:
             if (BtnPauseIcon != null)
             {
                BtnPauseIcon.Text = _isPaused ? "\uE768" : "\uE769"; // Play : Pause
                BtnPause.ToolTip = _isPaused ? "Resume Listening" : "Pause Listening";
             }
        }

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

        private async void BrowserScan_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "⏳ Switch to Browser in 3s...";
            await Task.Delay(1000);
            StatusText.Text = "⏳ Switch to Browser in 2s...";
            await Task.Delay(1000);
            StatusText.Text = "⏳ Switch to Browser in 1s...";
            await Task.Delay(1000);

            StatusText.Text = "🌐 Scanning...";
            string text = await _browserScanner.GetSelectedTextAsync();

            if (string.IsNullOrWhiteSpace(text))
            {
                StatusText.Text = "⚠️ No text found/selected.";
                return;
            }

            StatusText.Text = "✅ Text Captured!";
            
            // Open Assistant with this text
            // If text is very long, maybe truncate for context preview?
            // Reuse the existing logic to open Assistant
            
            if (_assistantWindow == null || !_assistantWindow.IsLoaded)
            {
                _assistantWindow = new AssistantWindow(_translator, $"[BROWSER CONTEXT]:\n{text}", this);
                _assistantWindow.Show();
            }
            else
            {
                _assistantWindow.UpdateContext($"[BROWSER CONTEXT]:\n{text}", true);
                _assistantWindow.Activate();
                if (_assistantWindow.WindowState == WindowState.Minimized) _assistantWindow.WindowState = WindowState.Normal;
            }
            
            // Auto trigger analysis
            // We need to expose Analyze if we want to force it, but passing context usually triggers it in constructor.
            // If window was already open, UpdateContext doesn't auto-trigger Analyze in previous code.
            // Ideally we should tell the window to analyze.
        }

        private async void GenerateSummary_Click(object sender, RoutedEventArgs e)
        {
            if (History.Count == 0)
            {
                StatusText.Text = "No history to summarize.";
                return;
            }

            StatusText.Text = "Generating Summary...";
            StatusIndicator.Fill = System.Windows.Media.Brushes.Yellow;

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
                StatusIndicator.Fill = System.Windows.Media.Brushes.Cyan;
                
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
    }

    public class TranslationItem
    {
        public string OriginalText { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string SourceIcon { get; set; } = "\uE7F4"; // Default to CC (Captions)
        public string SourceColor { get; set; } = "White";
    }
}
