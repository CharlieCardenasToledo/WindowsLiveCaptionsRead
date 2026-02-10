using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace WindowsLiveCaptionsReader
{
    public partial class AssistantWindow : Window
    {
        private Services.OllamaService _ollama;
        private string _context;
        private MainWindow _parent; // Reference to main window

        public AssistantWindow(Services.OllamaService ollama, string context, MainWindow parent)
        {
            InitializeComponent();
            _ollama = ollama;
            _context = context;
            _parent = parent;
            
            ContextText.Text = string.IsNullOrWhiteSpace(context) ? "(No recent conversation detected)" : context;
            
            // Auto-start analysis if context exists
            if (!string.IsNullOrWhiteSpace(context))
            {
                Analyze();
            }
        }

        private async void Analyze()
        {
            StatusText.Text = "Analyzing...";
            LoadingText.Text = "Thinking...";
            LoadingText.Visibility = Visibility.Visible;
            SuggestionsList.Visibility = Visibility.Collapsed;

            try
            {
                var response = await _ollama.GenerateSuggestionsAsync(_context);
                
                // For generic suggestions, we might just show the whole text if parsing is complex,
                // or try to parse if structure matches. For now, wrap in one item or improve parsing later.
                var items = new List<SuggestionItem> 
                { 
                    new SuggestionItem { Title = "General Suggestions", Text = response, Translation = "See below" } 
                };
                
                SuggestionsList.ItemsSource = items;
                LoadingText.Visibility = Visibility.Collapsed;
                SuggestionsList.Visibility = Visibility.Visible;
                StatusText.Text = "Ready";
            }
            catch (Exception ex)
            {
                LoadingText.Text = "Error: " + ex.Message;
                LoadingText.Foreground = System.Windows.Media.Brushes.Red;
                StatusText.Text = "Failed";
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            // Fetch fresh context
            string newContext = _parent.GetRecentContext();
            
            if (newContext != _context)
            {
                UpdateContext(newContext); // Trigger Analyze
            }
            else
            {
                Analyze(); // Just re-analyze same context
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
        
        public void UpdateContext(string newContext, bool autoAnalyze = false)
        {
             _context = newContext;
             ContextText.Text = _context;
             if (autoAnalyze) Analyze();
        }

        public async void ShowQuestionResponse(string question, string context)
        {
            StatusText.Text = "Generating Options...";
            LoadingText.Text = $"Generating options for: \"{question}\"...";
            LoadingText.Visibility = Visibility.Visible;
            SuggestionsList.Visibility = Visibility.Collapsed;

            try
            {
                var response = await _ollama.GenerateResponseToQuestionAsync(question, context);
                var items = ParseQuestionResponse(response);
                
                SuggestionsList.ItemsSource = items;
                LoadingText.Visibility = Visibility.Collapsed;
                SuggestionsList.Visibility = Visibility.Visible;
                StatusText.Text = "Options Ready";
            }
            catch (Exception ex)
            {
                LoadingText.Text = "Error: " + ex.Message;
                LoadingText.Foreground = System.Windows.Media.Brushes.Red;
                StatusText.Text = "Failed";
            }
        }

        private void CopySuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string text)
            {
                Clipboard.SetText(text);
                StatusText.Text = "Copied to clipboard!";
            }
        }

        private List<SuggestionItem> ParseQuestionResponse(string text)
        {
            var items = new List<SuggestionItem>();
            // Regex to find "Option X (Type): [Text] \n (Translation): [Trans]"
            // Pattern: Option \d+ \((.*?)\):\s*(.*?)\s*\(Translation\):\s*(.*?)(?=\nOption|\z)
            var regex = new Regex(@"Option \d+ \((.*?)\):\s*(.*?)\s*\(Translation\):\s*(.*?)(?=$|\nOption)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            var matches = regex.Matches(text);
            foreach (Match match in matches)
            {
                if (match.Groups.Count == 4)
                {
                    items.Add(new SuggestionItem
                    {
                        Title = match.Groups[1].Value.Trim(), // e.g. "Simple"
                        Text = match.Groups[2].Value.Trim(),
                        Translation = match.Groups[3].Value.Trim()
                    });
                }
            }

            if (items.Count == 0 && !string.IsNullOrWhiteSpace(text))
            {
                // Fallback
                items.Add(new SuggestionItem { Title = "Response", Text = text, Translation = "" });
            }

            return items;
        }
    }

    public class SuggestionItem
    {
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public string Translation { get; set; } = "";
    }
}
