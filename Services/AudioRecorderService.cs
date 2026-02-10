using NAudio.Wave;
using System;
using System.IO;

namespace WindowsLiveCaptionsReader.Services
{
    public class AudioRecorderService : IDisposable
    {
        private WaveInEvent _waveIn;
        private WaveFileWriter _writer;
        private string _currentFilePath;
        private bool _isRecording;

        public event Action<double> AudioLevelUpdated;

        public bool IsRecording => _isRecording;

        public void StartRecording(string filePath)
        {
            if (_isRecording) StopRecording();

            _currentFilePath = filePath;
            _waveIn = new WaveInEvent();
            _waveIn.WaveFormat = new WaveFormat(16000, 1); // Whisper prefers 16kHz mono
            
            _writer = new WaveFileWriter(filePath, _waveIn.WaveFormat);

            _waveIn.DataAvailable += (s, e) =>
            {
                if (_writer != null)
                {
                    _writer.Write(e.Buffer, 0, e.BytesRecorded);
                    
                    // Simple level calculation for visualization
                    if (e.BytesRecorded > 0)
                    {
                        var max = 0;
                        for (int i = 0; i < e.BytesRecorded; i += 2)
                        {
                            short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                            var sample32 = Math.Abs(sample) / 32768f;
                            if (sample32 > max) max = (int)sample32; // Just a rough peak
                        }
                         // Emitting 0-1 range roughly
                    }
                }
            };

            _waveIn.StartRecording();
            _isRecording = true;
        }

        public void StopRecording()
        {
            if (!_isRecording) return;

            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;

            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            _isRecording = false;
        }

        public void Dispose()
        {
            StopRecording();
        }
    }
}
