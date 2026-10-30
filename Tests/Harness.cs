using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MeuOrcamento.Tests;

/// <summary>Records the results, plus the helpers every test shares.</summary>
static class Harness
{
    static readonly List<string> _report = new();
    static int _passed, _failed;

    public static string Output { get; private set; } = "";
    static string DataFolder => Path.Combine(Output, "data");
    static string ScreensFolder => Path.Combine(Output, "screens");

    public static void Start(string output)
    {
        Output = output;
        // starts from scratch on every run
        if (Directory.Exists(output))
        {
            foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(output, recursive: true);
        }
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(ScreensFolder);
    }

    public static void Section(string name)
    {
        _report.Add("");
        _report.Add($"── {name}");
        Console.WriteLine($"\n── {name}");
    }

    public static void Check(string name, bool ok, string? actual = null)
    {
        if (ok) _passed++; else _failed++;
        var line = $"{(ok ? "OK  " : "FAIL")} {name}{(ok || string.IsNullOrEmpty(actual) ? "" : " — got: " + actual)}";
        _report.Add(line);
        if (!ok) Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(line);
        Console.ResetColor();
    }

    /// <summary>Writes the report and returns the number of failures (it becomes the exit code).</summary>
    public static int Finish()
    {
        var summary = $"{_passed} passed · {_failed} failed";
        File.WriteAllLines(Path.Combine(Output, "report.txt"), new[] { summary }.Concat(_report));
        Console.WriteLine();
        Console.ForegroundColor = _failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(summary);
        Console.ResetColor();
        Console.WriteLine($"Report, fonts and screens in: {Output}");
        return _failed;
    }

    // ───────────── reaching internals (x:Name fields and private methods) ─────────────

    const BindingFlags AnyMember = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    public static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, AnyMember)!.GetValue(o)!;
    public static void Call(object o, string method, params object?[] args) => o.GetType().GetMethod(method, AnyMember)!.Invoke(o, args);

    // ───────────── test data and windows ─────────────

    /// <summary>A new, empty data file inside output\data (never the real file).</summary>
    public static Repository NewRepository(string name) => new(Path.Combine(DataFolder, name));

    public static string DataPath(string name) => Path.Combine(DataFolder, name);

    /// <summary>Opens the main window off screen.</summary>
    public static MainWindow OpenWindow(Repository repo, double width = 1320, double height = 880)
    {
        var window = new MainWindow(repo) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -7000, Top = 0, Width = width, Height = height };
        window.Show();
        Wait(350);
        return window;
    }

    /// <summary>Lets WPF work (layout, animations) for a few milliseconds.</summary>
    public static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Saves the window to screens\{name}.png and records the fonts used in it.</summary>
    public static void Capture(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        FontAudit.Record(content, name);
        var dpi = VisualTreeHelper.GetDpi(window);
        var image = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        image.Render(background);
        image.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(ScreensFolder, name + ".png"));
        png.Save(file);
    }
}

/// <summary>Typography audit: every font combination that shows up in the captured screens.</summary>
static class FontAudit
{
    static readonly Dictionary<string, (int Count, SortedSet<string> Examples)> _combinations = new();
    static readonly Dictionary<string, HashSet<string>> _byText = new();

    public static IReadOnlyCollection<string> Combinations => _combinations.Keys;

    /// <summary>The combinations an exact text appeared in. Fields start with "[field] ".</summary>
    public static IReadOnlySet<string> Of(string text) => _byText.TryGetValue(text, out var keys) ? keys : new HashSet<string>();

    public static void Record(DependencyObject root, string screen)
    {
        if (root is UIElement element && !element.IsVisible) return;
        if (root is TextBlock block && !string.IsNullOrWhiteSpace(block.Text))
            Note(block.FontFamily, block.FontSize, block.FontWeight, block.FontStyle, screen, block.Text);
        else if (root is TextBox box)
            Note(box.FontFamily, box.FontSize, box.FontWeight, box.FontStyle, screen, "[field] " + box.Text);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            Record(VisualTreeHelper.GetChild(root, i), screen);
    }

    /// <summary>"Family|size|weight|style", e.g. "JetBrains Mono|11|Normal|Normal".</summary>
    public static string Key(string family, double size, string weight, string style = "Normal") => $"{family}|{size:0.#}|{weight}|{style}";

    static void Note(FontFamily family, double size, FontWeight weight, FontStyle style, string screen, string text)
    {
        var name = family.Source.Contains("JetBrains") ? "JetBrains Mono" : family.Source.Split(',')[0].Trim();
        var key = Key(name, size, weight.ToString(), style.ToString());
        if (!_byText.TryGetValue(text.Trim(), out var keys)) _byText[text.Trim()] = keys = new();
        keys.Add(key);
        var example = text.Trim().Replace("\n", " ");
        example = $"{screen}: {(example.Length > 34 ? example[..34] + "…" : example)}";
        var current = _combinations.TryGetValue(key, out var entry) ? entry : (0, new SortedSet<string>());
        if (current.Item2.Count < 3) current.Item2.Add(example);
        _combinations[key] = (current.Item1 + 1, current.Item2);
    }

    public static void WriteReport(string file)
    {
        var lines = new List<string> { $"{_combinations.Count} font combinations in use", "" };
        foreach (var (key, (count, examples)) in _combinations.OrderBy(k => k.Key))
        {
            lines.Add($"{key}  ×{count}");
            lines.AddRange(examples.Select(e => "      " + e));
        }
        File.WriteAllLines(file, lines);
    }
}
