using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using WindowsLiveCaptionsReader.Utils;
using System.Text;

namespace WindowsLiveCaptionsReader.Services
{
    public class CaptionReader
    {
        public event EventHandler<string>? TextChanged;
        public event EventHandler<string>? StatusChanged;

        private CancellationTokenSource? _cts;
        private AutomationElement? _window;
        private string _previousLatestCaption = "";

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            
            Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private void RunLoop(CancellationToken token)
        {
            StatusChanged?.Invoke(this, "Starting...");
            
            try 
            {
                _window = LiveCaptionsHandler.LaunchLiveCaptions();
                LiveCaptionsHandler.FixLiveCaptions(_window);
                LiveCaptionsHandler.HideLiveCaptions(_window);
                StatusChanged?.Invoke(this, "Listening...");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                return;
            }

            while (!token.IsCancellationRequested)
            {
                string fullText = "";
                try
                {
                     if (_window == null) throw new Exception("Window null");
                     fullText = LiveCaptionsHandler.GetCaptions(_window);
                }
                catch
                {
                    // Attempt recovery
                    try {
                        _window = LiveCaptionsHandler.LaunchLiveCaptions();
                        LiveCaptionsHandler.HideLiveCaptions(_window);
                    } catch {
                       Thread.Sleep(1000);
                       continue;
                    }
                }

                if (string.IsNullOrEmpty(fullText))
                {
                    Thread.Sleep(50);
                    continue;
                }

                // Cleanup
                fullText = RegexPatterns.Acronym().Replace(fullText, "$1$2");
                fullText = RegexPatterns.AcronymWithWords().Replace(fullText, "$1 $2");
                fullText = RegexPatterns.PunctuationSpace().Replace(fullText, "$1 ");
                fullText = RegexPatterns.CJPunctuationSpace().Replace(fullText, "$1");
                fullText = TextUtil.ReplaceNewlines(fullText, TextUtil.MEDIUM_THRESHOLD);

                // Logic to extract relevant text
                bool endsWithEOS = Array.IndexOf(TextUtil.PUNC_EOS, fullText[^1]) != -1;
                int lastEOSIndex;
                if (endsWithEOS)
                    lastEOSIndex = fullText[0..^1].LastIndexOfAny(TextUtil.PUNC_EOS);
                else
                    lastEOSIndex = fullText.LastIndexOfAny(TextUtil.PUNC_EOS);

                string latestCaption = fullText.Substring(lastEOSIndex + 1).Trim();

                if (latestCaption != _previousLatestCaption)
                {
                     // Notify UI
                     _previousLatestCaption = latestCaption;
                     TextChanged?.Invoke(this, latestCaption);
                }

                Thread.Sleep(30);
            }
        }
    }
}
