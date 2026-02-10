using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsLiveCaptionsReader.Services
{
    public class OllamaService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "http://localhost:11434";
        private const string ChatUrl = BaseUrl + "/api/chat";
        private string _modelName = "llama3.2"; 
        
        public OllamaService(string modelName = "llama3.2")
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10); // More generous timeout for non-streaming
            _modelName = modelName;
        }

        public async Task<bool> IsRunningAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://localhost:11434/");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public bool StartServer()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> TranslateStreamAsync(string text, Action<string> onPartialUpdate, List<string>? historyContext = null, string targetLang = "Spanish", CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            
            // Diagnostic Log
            try 
            {
                await File.AppendAllTextAsync("ollama_debug.log", $"[{DateTime.Now:HH:mm:ss}] Requesting: {text}\n");
            } catch {}

            var prompt = $"Translate this text to Spanish. Output ONLY the translation. Text: {text}";

            var messagesList = new List<object>
            {
                new { role = "system", content = "Translate the input text to Spanish. Return only the translated text. No explanation." },
                new { role = "user", content = prompt }
            };

            var requestData = new
            {
                model = _modelName,
                messages = messagesList,
                stream = true, // RE-ENABLED STREAMING
                temperature = 0.3
            };

            var jsonContent = JsonSerializer.Serialize(requestData);
            var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                // Use ResponseHeadersRead to start reading stream immediately
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    SafeLog($"[{DateTime.Now:HH:mm:ss}] Error Status: {response.StatusCode} | {errorBody}\n");
                    return $"[Error: {response.StatusCode}]";
                }

                using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var reader = new System.IO.StreamReader(stream);

                var sb = new StringBuilder();
                SafeLog($"[{DateTime.Now:HH:mm:ss}] Stream started...\n");

                while (!reader.EndOfStream)
                {
                    if (token.IsCancellationRequested) break;

                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        
                        // Check for Done
                        if (doc.RootElement.TryGetProperty("done", out var doneElement) && doneElement.GetBoolean())
                        {
                            break;
                        }

                        // Check for Content
                        if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                            messageElement.TryGetProperty("content", out var contentElement))
                        {
                            var chunk = contentElement.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                sb.Append(chunk);
                                onPartialUpdate(sb.ToString()); // Real-time update
                            }
                        }
                    }
                    catch (JsonException) { /* Split chunk handling ignored for simplicity */ }
                }

                var final = CleanOutput(sb.ToString());
                SafeLog($"[{DateTime.Now:HH:mm:ss}] Completed. Length: {final.Length}\n");
                return final;
            }
            catch (TaskCanceledException)
            {
                SafeLog($"[{DateTime.Now:HH:mm:ss}] Canceled\n");
                return "";
            }
            catch (Exception ex)
            {
                SafeLog($"[{DateTime.Now:HH:mm:ss}] Exception: {ex.Message}\n");
                return $"[Error: {ex.Message}]";
            }
        }

        // Simple non-streaming wrapper for single-shot translation
        public async Task<string> TranslateAsync(string text, string targetLang = "Spanish")
        {
            var result = "";
            // We reuse the streaming method but just wait for it to finish and collect the buffer
            await TranslateStreamAsync(text, (partial) => { result = partial; }, null, targetLang);
            return result;
        }

        public async Task<string> GenerateSummaryAsync(string fullHistory)
        {
            var prompt = $@"
Analyze the following English class transcript/conversation and provide a structured summary in MARKDOWN format.
Focus on educational value for the student.

TRANSCRIPT:
{fullHistory}

OUTPUT FORMAT:
# Class Summary - {DateTime.Now:yyyy-MM-dd}

## 📌 Main Topics
- (List main topics discussed)

## 🧠 Key Vocabulary & Phrases
- (List new or important words used)

## ✅ Action Items / Homework
- (List any tasks mentioned, if none, say 'None detected')

## 💡 Improvement Tips
- (If the student spoke, suggest 1-2 grammatical corrections)
";
            
            // We use standard generate (not chat) for this block processing
            var requestData = new
            {
                model = _modelName,
                prompt = prompt,
                stream = false
            };
            
            var json = JsonSerializer.Serialize(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            try 
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/generate")
                {
                    Content = content
                };

                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                
                // Manually parse to avoid JsonDocument ambiguity if imports are messy
                using var doc = JsonDocument.Parse(responseString);
                
                if (doc.RootElement.TryGetProperty("response", out var res))
                {
                    string? val = res.GetString();
                    return val ?? "";
                }
                
                return "Error: Empty response property.";
            }
            catch (Exception ex)
            {
                SafeLog($"Summary Error: {ex.Message}\n");
                return $"Error generating summary: {ex.Message}";
            }
        }

        private void SafeLog(string message)
        {
            try { File.AppendAllText("ollama_debug.log", message); } catch { }
        }

        public async Task<string> GenerateResponseToQuestionAsync(string question, string conversationContext, CancellationToken token = default)
        {
            var prompt = $@"The user is taking an English class or practice session. The teacher asked a specific question.
Helping the user to reply is your goal.
Provide 3 distinct options for the user to reply, with varying complexity.

QUESTION ASKED: ""{question}""

CONTEXT:
{conversationContext}

REQUIREMENTS:
1.  **Simple Option:** Short, direct answer (A2 Level).
2.  **Standard Option:** Natural, complete answer (B1 Level).
3.  **Detailed Option:** Elaborate answer with more details (B2 Level).

FORMAT:
Option 1 (Simple): [Text]
(Translation): [Spanish Translation]

Option 2 (Standard): [Text]
(Translation): [Spanish Translation]

Option 3 (Detailed): [Text]
(Translation): [Spanish Translation]

Only provide the options and translations. No intro/outro.";

            var requestData = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful English tutor assistant. Your task is to provide reply options for the student." },
                    new { role = "user", content = prompt }
                },
                stream = false,
                temperature = 0.7
            };
            
            var jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(ChatUrl, content, token);
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync(token);
                    using var doc = JsonDocument.Parse(responseString);
                    if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                        messageElement.TryGetProperty("content", out var contentElement))
                    {
                        return CleanOutput(contentElement.GetString());
                    }
                }
            }
            catch { /* Ignore */ }
            return "Error generating response options.";
        }

        public async Task<string> GenerateSuggestionsAsync(string conversationContext, CancellationToken token = default)
        {
            var prompt = $@"The user is in a speaking exam or English conversation practice. Based on the conversation history below, suggest 3 natural, COMPLETE responses the user can say.

IMPORTANT REQUIREMENTS:
- User is learning English at B1 level (CEFR)
- Each response should be 2-4 complete sentences
- Use vocabulary and grammar appropriate for B1 level
- Responses should sound natural and conversational
- Include helpful educational information

Conversation History:
{conversationContext}

Provide the output in this EXACT format for each suggestion:

1. [Complete English response with 2-4 sentences]
   📝 Explicación: [Brief explanation in Spanish about grammar or usage]
   📚 Vocabulario: [2-3 key words/phrases with Spanish meanings]
   🇪🇸 Traducción: [Full Spanish translation]

2. [Second complete response...]
   📝 Explicación: [...]
   📚 Vocabulario: [...]
   🇪🇸 Traducción: [...]

3. [Third complete response...]
   📝 Explicación: [...]
   📚 Vocabulario: [...]
   🇪🇸 Traducción: [...]

Make sure each response is educational and helps the student learn, not just answer.";

            var requestData = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = "You are an expert English tutor for B1 level students (CEFR). Your goal is to help students learn while providing natural, appropriate responses. Each suggestion should be educational, complete (2-4 sentences), and include grammar explanations, vocabulary help, and Spanish translations. Always follow the exact format requested." },
                    new { role = "user", content = prompt }
                },
                stream = false,
                temperature = 0.7
            };
            
            var jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(ChatUrl, content, token);
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync(token);
                    using var doc = JsonDocument.Parse(responseString);
                    if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                        messageElement.TryGetProperty("content", out var contentElement))
                    {
                        return CleanOutput(contentElement.GetString());
                    }
                }
            }
            catch { /* Ignore errors */ }
            return "";
        }

        public async Task<string> GenerateVocabularyExtractionAsync(string text, string targetLang = "Spanish", CancellationToken token = default)
        {
            var prompt = $@"Analyze the following English text and extract 3-5 key vocabulary words or phrases suitable for a B1/B2 English learner.
Rules:
1. Ignore proper nouns (names, places) unless relevant.
2. Ignore very common words (A1 level).
3. Focus on useful phrases or intermediate words.
4. Output specific format: Word|Definition (English)|Translation ({targetLang})

TEXT: ""{text}""

OUTPUT FORMAT (one per line):
Word|Definition|Translation";

            var requestData = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = "You are a vocabulary extraction specialist. Extract key B1/B2 words/phrases with definitions and translations." },
                    new { role = "user", content = prompt }
                },
                stream = false,
                temperature = 0.5
            };
            
            var jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(ChatUrl, content, token);
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync(token);
                    using var doc = JsonDocument.Parse(responseString);
                    if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                        messageElement.TryGetProperty("content", out var contentElement))
                    {
                        return CleanOutput(contentElement.GetString());
                    }
                }
            }
            catch { /* Ignore */ }
            return "";
        }

        private string CleanOutput(string? output)
        {
             if (string.IsNullOrEmpty(output)) return "";
             // Remove thinking process <think>...</think> if present (DeepSeek models)
             // and trim quotes if the model adds them.
             
             // Simple regex replacement for <think> tags if needed, but manual check is safer for basic deps
             int thinkStart = output.IndexOf("<think>");
             int thinkEnd = output.IndexOf("</think>");
             if (thinkStart >= 0 && thinkEnd > thinkStart)
             {
                 output = output.Remove(thinkStart, thinkEnd - thinkStart + 8);
             }

             return output.Trim().Trim('"');
        }
    }
}
