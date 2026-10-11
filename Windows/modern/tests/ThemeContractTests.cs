using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EasyEject {
    // Static source contract for the WinUI interface: it follows the Windows light, dark and
    // high-contrast themes through theme resources and declares per-monitor DPI awareness.
    // The exact XAML/manifest/code-behind are copied beside this runner at build time.
    // This is not a visual check; Ryan's foreground pass covers actual rendering.
    public static partial class Tests {
        static string ThemeSource(string name) {
            string path = Path.Combine(AppContext.BaseDirectory, "modern-app-source", name);
            Check(File.Exists(path), "Missing copied interface source " + name); return File.ReadAllText(path);
        }
        static string[] ThemeSources(string pattern) {
            string folder = Path.Combine(AppContext.BaseDirectory, "modern-app-source");
            Check(Directory.Exists(folder), "Interface source copy missing"); return Directory.GetFiles(folder, pattern).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
        static void ThemeContractTests() {
            Test("modern XAML uses theme resources for every brush and never forces a theme", delegate {
                var files = ThemeSources("*.xaml"); Check(files.Length >= 5, "Interface XAML not copied");
                var literal = new Regex("\\b(Foreground|Background|BorderBrush|Fill|Stroke)=\"(?!\\{)[^\"]+\"");
                foreach (var file in files) {
                    string text = File.ReadAllText(file);
                    var hit = literal.Match(text); Check(!hit.Success, Path.GetFileName(file) + " hard-codes a brush: " + hit.Value);
                    Check(text.IndexOf("RequestedTheme", StringComparison.Ordinal) < 0, Path.GetFileName(file) + " forces a theme instead of following Windows");
                }
            });
            Test("modern code-behind creates no fixed colors that ignore light dark or high contrast", delegate {
                var files = ThemeSources("*.cs"); Check(files.Length >= 5, "Interface code not copied");
                foreach (var file in files) {
                    string text = File.ReadAllText(file);
                    foreach (var banned in new[] { "Color.FromArgb", "Colors.", "RequestedTheme", "new SolidColorBrush(" })
                        Check(text.IndexOf(banned, StringComparison.Ordinal) < 0, Path.GetFileName(file) + " uses " + banned);
                }
            });
            Test("modern app manifest declares per-monitor DPI awareness", delegate {
                string manifest = ThemeSource("app.manifest");
                Check(Regex.IsMatch(manifest, "<dpiAwareness[^>]*>\\s*PerMonitorV2\\s*</dpiAwareness>"), "Interface is not PerMonitorV2 DPI aware");
            });
        }
    }
}
