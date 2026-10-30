using System.IO;
using System.Windows;

namespace MeuOrcamento.Tests;

/// <summary>
/// Runs every check and reports what passed and what failed.
///   dotnet run --project Tests                  → everything (behavior, screens, fonts)
///   dotnet run --project Tests -- real file     → only captures the screens of a COPY of a data file
/// Results in Tests\output\ (report.txt, fonts.txt, screens\) — or Tests\output-real\ in "real" mode.
/// The exit code is the number of failures.
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        App.ConfigureCulture();
        // an "empty" Application with the app's styles; it only shuts down when the tests are done
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var dictionary in new[] { "Dark", "Styles" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/MeuOrcamento;component/Themes/{dictionary}.xaml"),
            });

        if (args is ["real", var file])
        {
            // its own folder, so the test report isn't wiped
            Harness.Start(Path.Combine(TestsFolder(), "output-real"));
            ScreenTests.CaptureRealData(file);
            return 0;
        }

        Harness.Start(Path.Combine(TestsFolder(), "output"));

        NumberBoxTests.Run();
        RepositoryTests.Run();
        TransactionEditorTests.Run();
        AllocationEditorTests.Run();
        WindowTests.Run();
        ScreenTests.Run();
        return Harness.Finish();
    }

    /// <summary>The Tests\ folder (where the .csproj is), walking up from bin\...</summary>
    static string TestsFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "MeuOrcamento.Tests.csproj")))
            folder = folder.Parent;
        return folder?.FullName ?? AppContext.BaseDirectory;
    }
}
