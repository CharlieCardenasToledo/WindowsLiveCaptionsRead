using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation; // Requires UIAutomationClient and UIAutomationTypes assemblies

namespace WindowsLiveCaptionsReader.Services
{
    public class BrowserCaptureService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        public async Task<string> GetSelectedTextAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Get the current active window
                    IntPtr handle = GetForegroundWindow();
                    if (handle == IntPtr.Zero) return "No active window detected.";

                    // Check if it's a browser (optimization)
                    GetWindowThreadProcessId(handle, out int processId);
                    var process = Process.GetProcessById(processId);
                    string procName = process.ProcessName.ToLower();

                    // Only support major browsers for now
                    if (!procName.Contains("chrome") && !procName.Contains("msedge") && !procName.Contains("firefox"))
                    {
                        // Fallback: Try anyway, but warn log?
                        // return string.Empty; 
                    }

                    // 2. Get Automation Element
                    AutomationElement element = AutomationElement.FromHandle(handle);
                    if (element == null) return "Could not access window UI.";

                    // 3. Find the focused element (where selection likely is)
                    AutomationElement? focusedElement = null;
                    try
                    {
                        focusedElement = AutomationElement.FocusedElement;
                    }
                    catch 
                    {
                        // Fallback validation
                    }

                    if (focusedElement != null)
                    {
                        // Try TextPattern (Document-like elements)
                        if (focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out object patternObj))
                        {
                            var textPattern = (TextPattern)patternObj;
                            var selection = textPattern.GetSelection();
                            if (selection.Length > 0)
                            {
                                return selection[0].GetText(-1).Trim();
                            }
                        }
                        
                        // Try ValuePattern (Input boxes, URL bars)
                        if (focusedElement.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj))
                        {
                             var valuePattern = (ValuePattern)valuePatternObj;
                             return valuePattern.Current.Value;
                        }
                    }

                    return ""; // No selection found
                }
                catch (Exception ex)
                {
                    return $"Error capturing text: {ex.Message}";
                }
            });
        }
        
        // Helper to check if a process is a browser
        public bool IsBrowserActive()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero) return false;
                GetWindowThreadProcessId(handle, out int processId);
                var process = Process.GetProcessById(processId);
                string n = process.ProcessName.ToLower();
                return n.Contains("chrome") || n.Contains("msedge") || n.Contains("firefox") || n.Contains("opera") || n.Contains("brave");
            }
            catch { return false; }
        }
    }
}
