using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace WindowsLiveCaptionsReader.Services
{
    public class WhisperService : IDisposable
    {
        private WhisperFactory? _whisperFactory;
        private WhisperProcessor? _processor;
        private string _modelPath;
        // Small model: Better accuracy for classroom transcription (~465MB)
        // Options: Tiny(75MB), Base(141MB), Small(465MB), Medium(1.5GB), Large(3GB)
        private const GgmlType ModelType = GgmlType.Small;

        public bool IsModelLoaded => _whisperFactory != null;

        public WhisperService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string modelFolder = Path.Combine(appData, "WindowsLiveCaptionsReader", "Models");
            Directory.CreateDirectory(modelFolder);
            _modelPath = Path.Combine(modelFolder, "ggml-small.bin");
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(_modelPath))
            {
                await DownloadModelAsync();
            }

            if (_whisperFactory == null)
            {
                _whisperFactory = WhisperFactory.FromPath(_modelPath);
                
                // Configure simpler builder for older C# compatibility if needed, 
                // but usually builder pattern is standard in Whisper.net
                var builder = _whisperFactory.CreateBuilder()
                    .WithLanguage("en"); // We are listening to English

                _processor = builder.Build();
            }
        }

        private async Task DownloadModelAsync()
        {
            using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(ModelType);
            using var fileWriter = File.OpenWrite(_modelPath);
            await modelStream.CopyToAsync(fileWriter);
        }

        public async Task<string> TranscribeAsync(string wavFilePath)
        {
            if (_processor == null) await InitializeAsync();

            if (!File.Exists(wavFilePath)) return "";

            using var fileStream = File.OpenRead(wavFilePath);
            var text = "";

            await foreach (var result in _processor.ProcessAsync(fileStream))
            {
                text += result.Text + " ";
            }

            return text.Trim();
        }

        public void Dispose()
        {
            _processor?.Dispose();
            _whisperFactory?.Dispose();
        }
    }
}
