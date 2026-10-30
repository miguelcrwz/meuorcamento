using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MeuOrcamento.Editors;
using static MeuOrcamento.Tests.Harness;

namespace MeuOrcamento.Tests;

/// <summary>The main window: pages, planning a month, the drawer, alerts and "a single window".</summary>
static class WindowTests
{
    public static void Run()
    {
        Section("WINDOW");
        var repo = NewRepository("window.json");
        var today = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var month = repo.CreateMonth(today);
        month.Salary = 3000;
        month.Transactions.Add(new Transaction { Date = today, Description = "Aluguel", Amount = 1200, CategoryId = month.Allocations[0].CategoryId, Recurring = true });
        repo.Save();

        var window = OpenWindow(repo);
        Check("opens on the current month with the summary", Field<FrameworkElement>(window, "SummaryPage").Visibility == Visibility.Visible);

        window.GoTo(today.AddMonths(1));
        Check("a month without a plan shows the empty page", Field<FrameworkElement>(window, "EmptyPage").Visibility == Visibility.Visible);
        Call(window, "Plan_Click", null, new RoutedEventArgs());
        Wait(150);
        var next = repo.FindMonth(today.AddMonths(1))!;
        Check("planning creates the month with the recurring transaction", next?.Transactions.Count == 1, $"{next?.Transactions.Count}");
        Check("after planning, the summary shows", Field<FrameworkElement>(window, "SummaryPage").Visibility == Visibility.Visible);

        Drawer(window, repo, next!);
        Alerts(window, next!, today.AddMonths(1));
        window.Close();

        SingleWindow();
    }

    static void Drawer(MainWindow window, Repository repo, BudgetMonth month)
    {
        _ = window.OpenDrawer(new TransactionEditor(repo, month, null, null));
        Wait(250);
        Call(window, "DrawerConfirm_Click", null, new RoutedEventArgs());
        Check("saving with an error shows the message and doesn't close",
            Field<FrameworkElement>(window, "Sheet").Visibility == Visibility.Visible && Field<TextBlock>(window, "DrawerMessage").Text.Length > 0,
            Field<TextBlock>(window, "DrawerMessage").Text);
        window.CloseDrawer(false);
        Check("cancel closes the drawer", Field<FrameworkElement>(window, "Sheet").Visibility == Visibility.Collapsed);

        month.Allocations[0].Percent = 40;
        _ = window.OpenDrawer(new AllocationEditor(repo, month));
        Wait(250);
        Call(window, "DrawerConfirm_Click", null, new RoutedEventArgs());
        bool askedToConfirm = Field<FrameworkElement>(window, "Sheet").Visibility == Visibility.Visible
                              && (string)Field<Button>(window, "DrawerConfirm").Content == "SALVAR ASSIM";
        Call(window, "DrawerConfirm_Click", null, new RoutedEventArgs());
        Check("an incomplete allocation asks for confirmation and saves on the 2nd click",
            askedToConfirm && Field<FrameworkElement>(window, "Sheet").Visibility == Visibility.Collapsed);

        _ = window.OpenDrawer(new ConfirmEditor("EXCLUIR", "Teste", "Teste.", "EXCLUIR"));
        Wait(200);
        Check("destructive confirmation: Enter doesn't confirm", !Field<Button>(window, "DrawerConfirm").IsDefault);
        window.CloseDrawer(false);
        _ = window.OpenDrawer(new IncomeEditor(month, false));
        Wait(200);
        Check("regular drawer: Enter saves", Field<Button>(window, "DrawerConfirm").IsDefault);
        window.CloseDrawer(false);
    }

    static void Alerts(MainWindow window, BudgetMonth month, DateTime start)
    {
        month.Transactions.Add(new Transaction { Date = start, Description = "Viagem", Amount = 5000, CategoryId = month.Allocations[3].CategoryId });
        window.GoTo(start);
        Check("a negative balance shows with its sign", Field<TextBlock>(window, "HeroCurrency").Text == "−R$", Field<TextBlock>(window, "HeroCurrency").Text);

        month.Salary = 20000;
        month.Allocations[1].FixedAmount = 100;
        month.Transactions.Add(new Transaction { Date = start, Description = "Café", Amount = 101.50m, CategoryId = month.Allocations[1].CategoryId });
        window.GoTo(start);
        var row = ((IEnumerable)Field<ItemsControl>(window, "CategoryTable").ItemsSource).Cast<CategoryRow>().ElementAt(1);
        Check("a small overspend (R$ 1,50 on a 20 thousand income) is flagged", row.Alert && row.Remaining == "−1,50", $"{row.Alert} {row.Remaining}");
    }

    /// <summary>Opens the real app twice on the same file: the second one has to quit on its own.</summary>
    static void SingleWindow()
    {
        var app = Path.Combine(AppContext.BaseDirectory, "MeuOrcamento.dll");
        var file = DataPath("instance.json");
        Process Launch() => Process.Start(new ProcessStartInfo("dotnet", $"\"{app}\" --dados \"{file}\"") { UseShellExecute = false })!;

        var first = Launch();
        for (int i = 0; i < 60 && first.MainWindowHandle == IntPtr.Zero && !first.HasExited; i++)
        {
            Thread.Sleep(250);
            first.Refresh();
        }
        var second = Launch();
        bool secondExited = second.WaitForExit(10_000);
        Check("a second window on the same file doesn't open (it only brings the first forward)", secondExited && !first.HasExited,
            $"second exited={secondExited}, first open={!first.HasExited}");
        if (!secondExited) second.Kill();
        if (!first.HasExited)
        {
            first.CloseMainWindow();
            if (!first.WaitForExit(3000)) first.Kill();
        }
    }
}
