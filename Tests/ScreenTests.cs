using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeuOrcamento.Controls;
using MeuOrcamento.Editors;
using static MeuOrcamento.Tests.Harness;

namespace MeuOrcamento.Tests;

/// <summary>Every captured screen (screens\*.png), edge cases and the font audit.</summary>
static class ScreenTests
{
    /// <summary>
    /// The type scale (Themes\Styles.xaml). Any text outside this list is a failure.
    /// If the scale changes on purpose, update it here too.
    /// </summary>
    static readonly HashSet<string> Scale = new()
    {
        FontAudit.Key("Bahnschrift", 17, "Light"),
        FontAudit.Key("Bahnschrift", 24, "Light"),
        FontAudit.Key("Bahnschrift", 26, "Light"),
        FontAudit.Key("Bahnschrift", 34, "Light"),
        FontAudit.Key("Bahnschrift", 112, "Light"),
        FontAudit.Key("JetBrains Mono", 11, "Normal"),
        FontAudit.Key("JetBrains Mono", 11, "Medium"),
        FontAudit.Key("Segoe UI Variable Text", 15, "Normal"),
        FontAudit.Key("Segoe Fluent Icons", 10, "Normal"),
        FontAudit.Key("Segoe Fluent Icons", 12, "Normal"),
    };

    public static void Run()
    {
        Section("SCREENS");
        AllScreens();
        Section("EDGE CASES");
        EdgeCases();
        Section("FONTS");
        FontAudit.WriteReport(Path.Combine(Output, "fonts.txt"));
        var outside = FontAudit.Combinations.Where(c => !Scale.Contains(c)).ToList();
        Check("every text uses the type scale", outside.Count == 0, string.Join("; ", outside));

        // category name and description: the same voice as the values, in tables, lists and fields
        var voice = FontAudit.Key("Bahnschrift", 17, "Light");
        var names = new[] { "Gastos fixos", "Aluguel", "[field] Gastos fixos" };
        var wrong = names.Where(t => !FontAudit.Of(t).SetEquals(new[] { voice })).Select(t => $"{t} → {string.Join(", ", FontAudit.Of(t))}").ToList();
        Check("names and descriptions use Bahnschrift Light 17", wrong.Count == 0, string.Join("; ", wrong));
    }

    static Transaction Tx(int y, int m, int d, string description, decimal amount, Guid category, bool recurring = false) =>
        new() { Date = new DateTime(y, m, d), Description = description, Amount = amount, CategoryId = category, Recurring = recurring };

    static void AllScreens()
    {
        var repo = NewRepository("screens.json");
        var september = repo.CreateMonth(new DateTime(2026, 9, 1));
        september.Salary = 6500;
        Guid Cat(string name) => repo.Data.Categories.First(c => c.Name == name).Id;
        september.Transactions.AddRange(new[]
        {
            Tx(2026, 9, 5, "Aluguel", 1800, Cat("Gastos fixos"), true),
            Tx(2026, 9, 10, "Internet", 120, Cat("Gastos fixos"), true),
            Tx(2026, 9, 10, "Faculdade", 320, Cat("Educação"), true),
            Tx(2026, 9, 6, "Tesouro Selic", 1300, Cat("Investimentos"), true),
            Tx(2026, 9, 14, "Mercado", 780, Cat("Dia a dia")),
            Tx(2026, 9, 20, "Show", 450, Cat("Lazer")),
        });
        var october = repo.CreateMonth(new DateTime(2026, 10, 1));
        Check("october inherits september's 4 recurring transactions", october.Transactions.Count == 4, $"{october.Transactions.Count}");
        october.ExtraIncome = 800;
        october.Allocations.First(a => a.CategoryId == Cat("Gastos fixos")).FixedAmount = 2500;
        october.Transactions.AddRange(new[]
        {
            Tx(2026, 10, 3, "Mercado", 640, Cat("Dia a dia")),
            Tx(2026, 10, 12, "Uber", 210, Cat("Dia a dia")),
            Tx(2026, 10, 18, "Cinema e jantar", 980, Cat("Lazer")),
            Tx(2026, 10, 20, "Reserva", 400, Cat("Reserva de emergência")),
            Tx(2026, 10, 22, "Curso de C#", 89.9m, Cat("Educação")),
        });
        repo.Save();

        var window = OpenWindow(repo);
        window.GoTo(new DateTime(2026, 10, 1));
        Wait(300);
        string[] pages = { "summary", "transactions", "history" };
        for (int i = 0; i < pages.Length; i++)
        {
            window.Navigate(i);
            Wait(250);
            Capture(window, pages[i]);
        }
        var octoberRow = ((IEnumerable)Field<ItemsControl>(window, "HistoryTable").ItemsSource).Cast<HistoryRow>().Single(r => r.Month == october);
        var expectedColors = new[] { "Gastos fixos", "Dia a dia", "Lazer", "Educação", "Investimentos", "Reserva de emergência" }
            .Select(name => CategoryColors.For(repo.Data.Categories.First(c => c.Name == name).Color));
        Check("history bar: one stretch per category, in its color, expenses before investments",
            octoberRow.Parts.Select(p => p.Fill).SequenceEqual(expectedColors), $"{octoberRow.Parts.Count} stretches");
        window.Navigate(0);
        window.GoTo(new DateTime(2026, 11, 1));
        Wait(250);
        Capture(window, "empty-month");
        window.GoTo(new DateTime(2026, 10, 1));
        Wait(150);

        Drawer(window, new TransactionEditor(repo, october, null, Cat("Lazer")), "drawer-transaction");
        var split = new TransactionEditor(repo, october, null, Cat("Lazer"));
        Field<NumberBox>(split, "Amount").Text = "1.200,00";
        Field<TextBox>(split, "Description").Text = "Bicicleta";
        Field<RadioButton>(split, "SplitOption").IsChecked = true;
        Field<NumberBox>(split, "InstallmentCount").Text = "10";
        Drawer(window, split, "drawer-installments");
        Drawer(window, new AllocationEditor(repo, october), "drawer-allocation");
        Drawer(window, new IncomeEditor(october, true), "drawer-income");
        Drawer(window, new ConfirmEditor("EXCLUIR · 22.10", "Curso de C#", "R$ 89,90 em Educação. Essa exclusão não pode ser desfeita.", "EXCLUIR"), "drawer-delete");

        // deleting a month from the screen: confirm it and check the effect on the data
        _ = window.DeleteMonth(september);
        Wait(450);
        Capture(window, "drawer-delete-month");
        window.CloseDrawer(true);
        Wait(300);
        var reread = new Repository(repo.FilePath);
        Check("deleting september from the screen: only october stays", reread.Data.Months.Select(m => m.Key).SequenceEqual(new[] { "2026-10" }));
        Check("and october still has its 9 transactions", reread.FindMonth(new DateTime(2026, 10, 1))?.Transactions.Count == 9);
        Check("and the backup copy was created",
            Directory.GetFiles(Path.GetDirectoryName(repo.FilePath)!, "screens.antes-de-excluir-2026-09.*.json").Length == 1);
        window.Navigate(2);
        Wait(200);
        Capture(window, "history-after-delete");
        window.Close();
    }

    static void Drawer(MainWindow window, IEditor editor, string name)
    {
        _ = window.OpenDrawer(editor);
        Wait(450);
        Capture(window, name);
        window.CloseDrawer(false);
        Wait(100);
    }

    static void EdgeCases()
    {
        var repo = NewRepository("edge-cases.json");
        var month = repo.CreateMonth(new DateTime(2026, 10, 1));
        month.Salary = 128_450.75m;
        month.ExtraIncome = 9_999.99m;
        repo.FindCategory(month.Allocations[0].CategoryId)!.Name = "Gastos fixos da casa e do carro (financiamento)";
        var random = new Random(7);
        string[] names = { "Supermercado do bairro com entrega", "Uber", "Assinatura de streaming", "Farmácia", "Restaurante", "Combustível", "Presente de aniversário da família" };
        for (int i = 0; i < 40; i++)
            month.Transactions.Add(Tx(2026, 10, 1 + i % 28, names[i % names.Length], Math.Round((decimal)(random.NextDouble() * 9000 + 10), 2),
                month.Allocations[i % 6].CategoryId, i % 5 == 0));
        var pets = new Category { Name = "Pets", Color = 6 };
        repo.Data.Categories.Add(pets);
        month.Allocations[5].Percent = 8;
        month.Allocations.Add(new Allocation { CategoryId = pets.Id, Percent = 2 });
        repo.Save();

        var window = OpenWindow(repo, 1100, 680);
        window.GoTo(new DateTime(2026, 10, 1));
        Wait(250);
        Capture(window, "min-summary");
        window.Navigate(1); Wait(200); Capture(window, "min-transactions");
        window.Navigate(2); Wait(200); Capture(window, "min-history");
        window.Navigate(0);
        _ = window.OpenDrawer(new AllocationEditor(repo, month));
        Wait(400);
        Capture(window, "min-allocation");
        window.CloseDrawer(false);

        month.Transactions.Clear();
        month.Salary = 9_876_543.21m;
        window.GoTo(new DateTime(2026, 10, 1));
        Wait(250);
        DependencyObject box = Field<TextBlock>(window, "HeroValue");
        while (box is not Viewbox) box = VisualTreeHelper.GetParent(box);
        var column = (FrameworkElement)((FrameworkElement)box).Parent;
        double width = ((FrameworkElement)box).RenderSize.Width;
        Check("a 7-digit balance fits its column (minimum window)", width <= column.ActualWidth + 0.5, $"{width:0} of {column.ActualWidth:0} px");
        Capture(window, "min-millions");
        window.Close();

        var empty = NewRepository("first-run.json");
        var first = OpenWindow(empty);
        Wait(400);
        Capture(first, "first-run");
        Check("the first run opens the income drawer", Field<FrameworkElement>(first, "Sheet").Visibility == Visibility.Visible);
        first.CloseDrawer(false);
        Wait(150);
        Capture(first, "zero-income");
        var rows = ((IEnumerable)Field<ItemsControl>(first, "CategoryTable").ItemsSource).Cast<CategoryRow>().ToList();
        Check("zero income: an investment with no goal shows a dash, not META OK",
            rows.Where(r => r.Kind == "INVESTIMENTO").All(r => r.Remaining == "—"),
            string.Join(", ", rows.Where(r => r.Kind == "INVESTIMENTO").Select(r => r.Remaining)));
        first.Close();
    }

    /// <summary>Captures the screens of a COPY of a data file (the app never opens the original).</summary>
    public static void CaptureRealData(string file)
    {
        var copy = DataPath("real.json");
        File.Copy(file, copy, overwrite: true);
        var repo = new Repository(copy);
        var window = OpenWindow(repo);
        foreach (var month in repo.Data.Months)
        {
            window.GoTo(month.Start);
            for (int i = 0; i < 3; i++)
            {
                window.Navigate(i);
                Wait(300);
                Capture(window, $"real-{month.Key}-{i + 1}");
            }
        }
        window.Close();
        Console.WriteLine($"Screens of the data (copy) in: {Path.Combine(Output, "screens")}");
    }
}
