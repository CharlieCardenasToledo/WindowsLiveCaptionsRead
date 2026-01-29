using System;
using System.Windows;
using System.Windows.Input;

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
            SuggestionsContent.Text = "Thinking...";
            SuggestionsContent.Foreground = System.Windows.Media.Brushes.LightGray;

            try
            {
                var input = _context;
                // Specific prompt for exam assistance
                var response = await _ollama.GenerateSuggestionsAsync(input);
                
                SuggestionsContent.Text = response;
                SuggestionsContent.Foreground = System.Windows.Media.Brushes.White;
                StatusText.Text = "Ready";
            }
            catch (Exception ex)
            {
                SuggestionsContent.Text = "Error: " + ex.Message;
                SuggestionsContent.Foreground = System.Windows.Media.Brushes.Red;
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
        
        public void UpdateContext(string newContext)
        {
             _context = newContext;
             ContextText.Text = newContext;
             Analyze();
        }
    }
}
