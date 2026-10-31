using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace MeuOrcamento;

public partial class App : Application
{
    /// <summary>Holds the data file's "slot" while the app is open.</summary>
    static Mutex? _instanceLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureCulture();

        // --dados <file> uses another data file (e.g. a test file); without it, the saved location, or the default
        var path = Repository.DefaultPath;
        var i = Array.IndexOf(e.Args, "--dados");
        if (i >= 0 && i + 1 < e.Args.Length)
        {
            path = e.Args[i + 1];
        }
        else if (Repository.ReadLocation(Repository.LocationFile) is { } saved)
        {
            // the saved file went missing (OneDrive not synced, folder renamed): better not to open than to start empty
            if (!File.Exists(saved))
            {
                MessageBox.Show($"O arquivo de dados deveria estar em:\n{saved}\n\nmas não foi encontrado. Se ele fica no OneDrive, " +
                    "confira se o OneDrive está conectado e sincronizado e abra o Meu Orçamento de novo.",
                    "Meu Orçamento", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
            path = saved;
        }

        // two windows on the same file would overwrite each other: the second one only brings the first forward
        _instanceLock = new Mutex(true, LockName(path), out bool first);
        if (!first)
        {
            NativeWindow.BringOtherInstanceToFront();
            Shutdown();
            return;
        }

        Repository repo;
        try
        {
            repo = new Repository(path);
        }
        catch (DataFileUnavailableException ex)
        {
            MessageBox.Show($"{ex.Message}\n\nFeche o programa que está usando o arquivo e abra o Meu Orçamento de novo.",
                "Meu Orçamento", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var window = new MainWindow(repo);
        MainWindow = window;
        window.Show();
    }

    static string LockName(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()));
        return @"Local\MeuOrcamento-" + Convert.ToHexString(hash)[..16];
    }

    static bool _cultureConfigured;

    public static void ConfigureCulture()
    {
        if (_cultureConfigured) return;
        _cultureConfigured = true;
        var ptBr = new CultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = ptBr;
        CultureInfo.DefaultThreadCurrentUICulture = ptBr;
        CultureInfo.CurrentCulture = ptBr;
        CultureInfo.CurrentUICulture = ptBr;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("pt-BR")));
    }
}
