using System.IO;
using System.Reflection;
using static MeuOrcamento.Tests.Harness;

namespace MeuOrcamento.Tests;

/// <summary>Repository and model: creating and copying months, saving, deleting, protecting the file.</summary>
static class RepositoryTests
{
    public static void Run()
    {
        Section("DATA");
        var repo = NewRepository("data.json");
        var jan = repo.CreateMonth(new DateTime(2027, 1, 1));
        Check("the first month seeds 6 categories", repo.Data.Categories.Count == 6 && jan.Allocations.Count == 6, $"{repo.Data.Categories.Count}");
        Check("the suggested split adds up to 100%", jan.Allocations.Sum(a => a.Percent) == 100);

        jan.Salary = 5000;
        var cat = repo.Data.Categories[0].Id;
        jan.Transactions.Add(new Transaction { Date = new DateTime(2027, 1, 31), Description = "Aluguel", Amount = 1800.55m, CategoryId = cat, Recurring = true,
            Payment = PaymentMethod.Pix });
        jan.Transactions.Add(new Transaction { Date = new DateTime(2027, 1, 10), Description = "Mercado", Amount = 300m, CategoryId = cat });
        var feb = repo.CreateMonth(new DateTime(2027, 2, 1));
        Check("a new month copies the salary", feb.Salary == 5000);
        Check("a new month copies only the recurring transactions", feb.Transactions.Count == 1, $"{feb.Transactions.Count}");
        Check("a recurring one on the 31st lands on Feb 28", feb.Transactions.Single().Date == new DateTime(2027, 2, 28), feb.Transactions.Single().Date.ToString("d"));
        Check("a recurring one keeps its payment method", feb.Transactions.Single().Payment == PaymentMethod.Pix, $"{feb.Transactions.Single().Payment}");
        Check("extra income doesn't carry over to the next month", feb.ExtraIncome == 0);

        jan.ExtraIncome = 123.45m;
        repo.Save();
        var reread = new Repository(repo.FilePath).FindMonth(new DateTime(2027, 1, 1))!;
        Check("saving and loading keeps the cents", reread.Transactions.Sum(t => t.Amount) == 2100.55m && reread.ExtraIncome == 123.45m);

        var dec = repo.CreateMonth(new DateTime(2026, 12, 1));
        Check("a month before all others copies the first one after it", dec.Salary == 5000);

        jan.Allocations[0].FixedAmount = 2000;
        Check("a fixed amount becomes an effective % of income", Math.Round(jan.EffectivePercent(jan.Allocations[0]), 4) == Math.Round(2000m / 5123.45m * 100, 4));
        Check("budgeted uses the fixed amount", jan.Budgeted(jan.Allocations[0]) == 2000);

        var copy = repo.DeleteMonth(dec);
        Check("deleting a month keeps a copy", copy != null && File.Exists(copy));
        Check("deleting a month removes only that month", repo.FindMonth(new DateTime(2026, 12, 1)) == null && repo.Data.Months.Count == 2);
        repo.DeleteMonth(feb);
        repo.DeleteMonth(jan);
        Check("with no months left, orphan categories go too", repo.Data.Categories.Count == 0, $"{repo.Data.Categories.Count}");
        var mar = repo.CreateMonth(new DateTime(2027, 3, 1));
        Check("after that, a new month doesn't duplicate categories", repo.Data.Categories.Count == 6, $"{repo.Data.Categories.Count}");

        mar.Allocations[0].Percent = 30;
        mar.Allocations[0].FixedAmount = 1500;
        Check("zero income + fixed amount uses the stored %", mar.EffectivePercent(mar.Allocations[0]) == 30, $"{mar.EffectivePercent(mar.Allocations[0])}");

        Protections(repo);
        FileFormat();
        DataLocation();
    }

    /// <summary>The location file that points to a data file kept elsewhere, like OneDrive.</summary>
    static void DataLocation()
    {
        var location = DataPath("data-location.txt");
        Check("without a location file, the default path is used", Repository.ReadLocation(location) == null);
        File.WriteAllText(location, "  C:\\Users\\alguem\\OneDrive\\MeuOrcamento\\dados.json \r\n");
        Check("a location file points to the data file kept elsewhere",
            Repository.ReadLocation(location) == @"C:\Users\alguem\OneDrive\MeuOrcamento\dados.json", Repository.ReadLocation(location));
        File.WriteAllText(location, "\r\n");
        Check("an empty location file is ignored", Repository.ReadLocation(location) == null);
    }

    /// <summary>What prevents losing data: conflict, failed write, locked or malformed file.</summary>
    static void Protections(Repository repo)
    {
        repo.Save();
        File.SetLastWriteTimeUtc(repo.FilePath, DateTime.UtcNow.AddMinutes(1)); // "another program touched the file"
        var conflict = repo.Save();
        Check("an outside change becomes a conflict copy", conflict != null && File.Exists(conflict));
        Check("without an outside change, no copy is made", repo.Save() == null);

        repo.CreateMonth(new DateTime(2027, 4, 1));
        repo.Save();
        int months = repo.Data.Months.Count, categories = repo.Data.Categories.Count;
        File.SetAttributes(repo.FilePath, FileAttributes.ReadOnly);
        bool failed = false;
        try { repo.DeleteMonth(repo.FindMonth(new DateTime(2027, 4, 1))!); }
        catch { failed = true; }
        File.SetAttributes(repo.FilePath, FileAttributes.Normal);
        Check("a delete that can't be written is undone in memory", failed && repo.Data.Months.Count == months && repo.Data.Categories.Count == categories,
            $"failed={failed} months={repo.Data.Months.Count}/{months}");
        Check("a failed write leaves no .tmp behind", !File.Exists(repo.FilePath + ".tmp"));

        using (new FileStream(repo.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool refused = false;
            try { _ = new Repository(repo.FilePath); }
            catch (DataFileUnavailableException) { refused = true; }
            Check("locked file: refuses to open instead of starting empty", refused);
        }

        var normalize = typeof(Repository).GetMethod("Normalize", BindingFlags.NonPublic | BindingFlags.Static)!;
        var malformed = new BudgetData
        {
            Categories = null!,
            Months = new List<BudgetMonth>
            {
                new() { Key = "2026-10", Allocations = null!, Transactions = null! },
                new() { Key = "2026-1" },   // invalid key
                new() { Key = "2026-10" },  // duplicate
            },
        };
        bool dropped = (bool)normalize.Invoke(null, new object[] { malformed })!;
        Check("malformed file: fixes null lists and drops invalid months",
            dropped && malformed.Categories != null && malformed.Months.Count == 1 && malformed.Months[0].Allocations != null && malformed.Months[0].Transactions != null,
            $"dropped={dropped} months={malformed.Months.Count}");
    }

    /// <summary>The JSON keeps its Portuguese names, so files saved by earlier versions still open.</summary>
    static void FileFormat()
    {
        var path = DataPath("format.json");
        File.WriteAllText(path, """
            {
              "Versao": 1,
              "Categorias": [
                { "Id": "6f1c0e8a-0000-4000-8000-000000000001", "Nome": "Lazer", "Tipo": "Investimento", "Cor": 3 }
              ],
              "Meses": [
                {
                  "Chave": "2026-09",
                  "Salario": 5000.5,
                  "RendaExtra": 200,
                  "Alocacoes": [ { "CategoriaId": "6f1c0e8a-0000-4000-8000-000000000001", "Percentual": 10, "ValorFixo": 300 } ],
                  "Lancamentos": [
                    { "Id": "6f1c0e8a-0000-4000-8000-000000000002", "Data": "2026-09-05T00:00:00", "Descricao": "Show",
                      "Valor": 120.9, "CategoriaId": "6f1c0e8a-0000-4000-8000-000000000001", "Recorrente": true }
                  ]
                }
              ]
            }
            """);
        var repo = new Repository(path);
        var month = repo.Data.Months.Single();
        var t = month.Transactions.Single();
        Check("reads a data file saved by earlier versions",
            repo.Data.Categories.Single() is { Name: "Lazer", Kind: CategoryKind.Investment, Color: 3 }
            && month.Salary == 5000.5m && month.ExtraIncome == 200 && month.Allocations.Single().FixedAmount == 300
            && t is { Description: "Show", Amount: 120.9m, Recurring: true } && t.Date == new DateTime(2026, 9, 5));

        repo.Save();
        var saved = File.ReadAllText(path);
        Check("saves with the same names", saved.Contains("\"Lancamentos\"") && saved.Contains("\"Descricao\"") && saved.Contains("\"Investimento\"")
            && !saved.Contains("\"Transactions\"") && !saved.Contains("\"Investment\""));
        Check("a transaction from before payment methods has none, and none is written", t.Payment == null && !saved.Contains("\"FormaPagamento\""));

        t.Payment = PaymentMethod.Debit;
        repo.Save();
        saved = File.ReadAllText(path);
        Check("the payment method is saved in portuguese and read back",
            saved.Contains("\"FormaPagamento\": \"Debito\"") && new Repository(path).Data.Months.Single().Transactions.Single().Payment == PaymentMethod.Debit);
    }
}
