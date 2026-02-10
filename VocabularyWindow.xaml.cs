using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsLiveCaptionsReader.Models;
using WindowsLiveCaptionsReader.Services;

namespace WindowsLiveCaptionsReader
{
    public partial class VocabularyWindow : Window
    {
        private readonly VocabularyService _service;
        private List<VocabularyItem> _allWords;

        public VocabularyWindow(VocabularyService service)
        {
            InitializeComponent();
            _service = service;
            Loaded += VocabularyWindow_Loaded;
        }

        private async void VocabularyWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadVocabulary();
        }

        private async Task LoadVocabulary()
        {
            StatusText.Text = "Loading...";
            try
            {
                _allWords = await _service.GetAllVocabularyAsync();
                FilterList(SearchBox.Text);
                StatusText.Text = "Ready";
                CountText.Text = $"Total: {_allWords.Count} words";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error loading vocabulary";
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void FilterList(string query)
        {
            if (_allWords == null) return;

            if (string.IsNullOrWhiteSpace(query) || query == "Search...")
            {
                VocabGrid.ItemsSource = _allWords;
            }
            else
            {
                var filtered = _allWords.Where(w => 
                    w.Word.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                    w.SpanishTranslation.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    w.Definition.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                VocabGrid.ItemsSource = filtered;
            }
        }

        private void SearchBox_KeyUp(object sender, KeyEventArgs e)
        {
            FilterList(SearchBox.Text);
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search...")
            {
                SearchBox.Text = "";
                SearchBox.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Search...";
                SearchBox.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void AddWord_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Add Word feature coming soon!", "My Vocabulary");
        }

        private async void ExtractFromText_Click(object sender, RoutedEventArgs e)
        {
            // Simple input dialog for now (using Clipboard as source?)
            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text) || text.Length < 20)
            {
                MessageBox.Show("Please copy text (20+ chars) to clipboard before clicking Analyze.", "Analyze Text");
                return;
            }

            StatusText.Text = "Analyzing Clipboard Text...";
            try
            {
                var suggestions = await _service.ExtractPotentialVocabularyAsync(text);
                if (suggestions.Count > 0)
                {
                    string msg = "Found specific words:\n\n" + string.Join("\n", suggestions) + "\n\nAdd them?";
                    if (MessageBox.Show(msg, "Extraction Results", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                         foreach (var s in suggestions)
                         {
                             var parts = s.Split('|');
                             if (parts.Length >= 3)
                             {
                                 await _service.AddOrUpdateWordAsync(parts[0], parts[1], parts[2], text.Substring(0, Math.Min(text.Length, 50)) + "...");
                             }
                         }
                         await LoadVocabulary();
                    }
                }
                else
                {
                    MessageBox.Show("No new vocabulary found suitable for B1/B2 level.", "Analysis Complete");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Analysis failed: {ex.Message}");
            }
            finally
            {
                StatusText.Text = "Ready";
            }
        }

        private async void DeleteWord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                if (MessageBox.Show("Delete this word?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    await _service.DeleteWordAsync(id);
                    await LoadVocabulary();
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
