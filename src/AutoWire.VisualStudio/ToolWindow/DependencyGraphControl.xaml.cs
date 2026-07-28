using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace AutoWire.VisualStudio.ToolWindow
{
    /// <summary>
    /// WPF control for the AutoWire Dependency Graph tool window. Lets the user load or paste the Mermaid
    /// text produced by the AutoWire source generator and renders it via a WebView2 hosting mermaid.js from
    /// a CDN (<c>https://cdn.jsdelivr.net/npm/mermaid</c> — requires internet access at runtime).
    /// </summary>
    public partial class DependencyGraphControl : UserControl
    {
        /// <summary>Matches <c>public const string Mermaid = """ ... """;</c> as emitted by AutoWireDependencyGraph.g.cs.</summary>
        private static readonly Regex MermaidConstRegex = new Regex(
            "Mermaid\\s*=\\s*\"\"\"\\s*\\r?\\n(?<body>.*?)\\r?\\n\\s*\"\"\"\\s*;",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private bool _webViewInitialized;
        private string _lastRenderedMermaid;

        public DependencyGraphControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            VSColorTheme.ThemeChanged += OnVsThemeChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            VSColorTheme.ThemeChanged -= OnVsThemeChanged;
        }

        private void OnVsThemeChanged(ThemeChangedEventArgs e)
        {
            // Re-render with the new theme's colors so the diagram doesn't stay stuck with the old palette.
            if (_lastRenderedMermaid is not null)
                _ = RenderAsync(_lastRenderedMermaid);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureWebViewInitializedAsync();
            }
            catch
            {
                // EnsureWebViewInitializedAsync already reports failures via ShowFallback; never let a
                // background exception escape the WPF Loaded event and crash the host process.
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select AutoWireDependencyGraph.g.cs",
                Filter = "AutoWire generated dependency graph (*.g.cs)|AutoWireDependencyGraph.g.cs;*.g.cs|C# files (*.cs)|*.cs|All files (*.*)|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var text = File.ReadAllText(dialog.FileName);
                var match = MermaidConstRegex.Match(text);
                if (!match.Success)
                {
                    StatusText.Text = "Could not find a 'public const string Mermaid = \"\"\" ... \"\"\";' literal in the selected file.";
                    return;
                }

                PasteTextBox.Text = match.Groups["body"].Value;
                StatusText.Text = $"Loaded from {Path.GetFileName(dialog.FileName)}";
                _ = RenderAsync(PasteTextBox.Text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText.Text = $"Failed to read file: {ex.Message}";
            }
        }

        private void RenderButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RenderAsync(PasteTextBox.Text);
        }

        private async System.Threading.Tasks.Task<bool> EnsureWebViewInitializedAsync()
        {
            if (_webViewInitialized) return true;

            try
            {
                // WebView2's default environment places its user-data folder next to the hosting
                // executable (devenv.exe, typically under Program Files), which a non-elevated user
                // cannot write to and fails with E_ACCESSDENIED (0x80070005). Point it at a writable,
                // per-user folder instead.
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutoWire", "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder);

                await MermaidWebView.EnsureCoreWebView2Async(environment);
                _webViewInitialized = true;
                FallbackMessage.Visibility = Visibility.Collapsed;
                MermaidWebView.Visibility = Visibility.Visible;
                return true;
            }
            catch (Exception ex)
            {
                // Most commonly: the WebView2 Evergreen Runtime is not installed on this machine.
                ShowFallback(
                    "Could not initialize the embedded WebView2 browser. " +
                    "Install the WebView2 Runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and reopen this window." +
                    Environment.NewLine + Environment.NewLine + ex.Message);
                return false;
            }
        }

        private void ShowFallback(string message)
        {
            FallbackMessage.Text = message;
            FallbackMessage.Visibility = Visibility.Visible;
            MermaidWebView.Visibility = Visibility.Collapsed;
        }

        private async System.Threading.Tasks.Task RenderAsync(string mermaidText)
        {
            if (string.IsNullOrWhiteSpace(mermaidText))
            {
                StatusText.Text = "Nothing to render — browse a file or paste Mermaid text first.";
                return;
            }

            if (!await EnsureWebViewInitializedAsync()) return;

            try
            {
                var html = BuildHtml(mermaidText);
                MermaidWebView.NavigateToString(html);
                _lastRenderedMermaid = mermaidText;
                StatusText.Text = $"Rendered {DateTime.Now:T}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Render failed: {ex.Message}";
            }
        }

        /// <summary>Builds a standalone HTML document that loads mermaid.js from a CDN and renders the given
        /// graph text, styled to match the current Visual Studio color theme (dark or light).</summary>
        private static string BuildHtml(string mermaidText)
        {
            var escaped = System.Net.WebUtility.HtmlEncode(mermaidText);

            var background = ToHex(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey));
            var foreground = ToHex(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey));
            var isDark = IsDark(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey));
            var mermaidTheme = isDark ? "dark" : "default";

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8' />
<style>
  body {{ margin: 0; padding: 8px; font-family: Segoe UI, sans-serif; background: {background}; color: {foreground}; }}
  #status {{ color: #888; font-size: 12px; margin-bottom: 6px; }}
</style>
<script src='https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js'></script>
</head>
<body>
<div id='status'>Rendering…</div>
<pre class='mermaid'>
{escaped}
</pre>
<script>
  try {{
    mermaid.initialize({{ startOnLoad: true, securityLevel: 'loose', theme: '{mermaidTheme}' }});
    document.getElementById('status').textContent = '';
  }} catch (e) {{
    document.getElementById('status').textContent = 'mermaid.js failed to load or render: ' + e;
  }}
</script>
</body>
</html>";
        }

        private static string ToHex(System.Drawing.Color color) =>
            $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>Perceptual luminance check used to pick mermaid's 'dark' vs 'default' theme.</summary>
        private static bool IsDark(System.Drawing.Color color) =>
            (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) < 128;
    }
}
