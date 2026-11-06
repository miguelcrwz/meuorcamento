using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Windows;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeuOrcamento;

/// <summary>Keeps everything in a single JSON file in %APPDATA%\MeuOrcamento.</summary>
public class Repository
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly (string Name, CategoryKind Kind, decimal Percent)[] SuggestedSplit =
    {
        ("Gastos fixos", CategoryKind.Expense, 45),
        ("Dia a dia", CategoryKind.Expense, 10),
        ("Investimentos", CategoryKind.Investment, 20),
        ("Lazer", CategoryKind.Expense, 10),
        ("Educação", CategoryKind.Expense, 5),
        ("Reserva de emergência", CategoryKind.Investment, 10),
    };

    static string AppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeuOrcamento");

    public static string DefaultPath => Path.Combine(AppFolder, "dados.json");

    /// <summary>Remembers a data file kept somewhere else (e.g. in OneDrive): one line with the file's full path.</summary>
    public static string LocationFile => Path.Combine(AppFolder, "data-location.txt");

    /// <summary>The path saved in a location file, or null when there is none.</summary>
    public static string? ReadLocation(string locationFile)
    {
        if (!File.Exists(locationFile)) return null;
        var path = File.ReadAllText(locationFile).Trim();
        return path.Length > 0 ? path : null;
    }

    public string FilePath { get; }
    public BudgetData Data { get; }

    /// <summary>The file's write time the last time this app read or wrote it.</summary>
    DateTime _lastWriteUtc;

    public Repository(string path)
    {
        FilePath = Path.GetFullPath(path);
        Data = Load();
    }

    BudgetData Load()
    {
        if (!File.Exists(FilePath)) return new BudgetData();
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // can't read it: better not to open than to start empty and overwrite it later
            throw new DataFileUnavailableException(FilePath, ex);
        }
        _lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);

        BudgetData? data;
        try
        {
            data = JsonSerializer.Deserialize<BudgetData>(text, JsonOptions);
        }
        catch (JsonException)
        {
            var copy = BackUp("corrompido");
            MessageBox.Show(
                $"Não consegui ler o arquivo de dados. Uma cópia dele foi guardada em:\n{copy}\n\nO app vai começar do zero.",
                "Meu Orçamento", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new BudgetData();
        }

        data ??= new BudgetData();
        if (Normalize(data))
        {
            var copy = BackUp("antes-de-corrigir");
            MessageBox.Show(
                $"Alguns meses do arquivo estavam inválidos ou repetidos e ficaram de fora. O arquivo original foi guardado em:\n{copy}",
                "Meu Orçamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return data;
    }

    /// <summary>Fixes what a hand-edited file may contain. Returns true if any month was dropped.</summary>
    static bool Normalize(BudgetData data)
    {
        data.Categories = (data.Categories ?? new()).Where(c => c is not null).ToList();
        var keys = new HashSet<string>();
        var months = (data.Months ?? new()).ToList();
        data.Months = months
            .Where(m => m is not null
                        && DateTime.TryParseExact(m.Key, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                        && keys.Add(m.Key))
            .ToList();
        foreach (var m in data.Months)
        {
            m.Allocations = (m.Allocations ?? new()).Where(a => a is not null).ToList();
            m.Transactions = (m.Transactions ?? new()).Where(t => t is not null).ToList();
            // an installment plan that makes no sense turns into a plain transaction
            foreach (var t in m.Transactions.Where(t => t.Installment is { } p && (p.Count < 2 || p.Number < 1 || p.Number > p.Count)))
                t.Installment = null;
        }
        return data.Months.Count != months.Count;
    }

    /// <summary>
    /// Writes everything. If the file changed outside since the last read (another window, another computer),
    /// the outside version is kept in a copy first; returns that copy's path, or null if there was no conflict.
    /// </summary>
    public string? Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string? conflict = File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != _lastWriteUtc
            ? BackUp("conflito")
            : null;
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Data, JsonOptions));
        try
        {
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch
        {
            File.Delete(temporary); // don't leave the .tmp behind
            throw;
        }
        _lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);
        return conflict;
    }

    /// <summary>Copies the current file to "dados.{reason}.{date-time}.json", in the same folder.</summary>
    string BackUp(string reason)
    {
        var copy = Path.Combine(Path.GetDirectoryName(FilePath)!,
            $"{Path.GetFileNameWithoutExtension(FilePath)}.{reason}.{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(FilePath, copy, overwrite: true);
        return copy;
    }

    public static string Key(DateTime month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public BudgetMonth? FindMonth(DateTime month)
    {
        var key = Key(month);
        return Data.Months.FirstOrDefault(m => m.Key == key);
    }

    public Category? FindCategory(Guid id) => Data.Categories.FirstOrDefault(c => c.Id == id);

    /// <summary>Month copied when planning a new one: the latest before it or, if there is none, the earliest after it.</summary>
    public BudgetMonth? TemplateFor(DateTime month)
    {
        var key = Key(month);
        return Data.Months.Where(m => string.CompareOrdinal(m.Key, key) < 0)
                   .OrderByDescending(m => m.Key, StringComparer.Ordinal).FirstOrDefault()
               ?? Data.Months.Where(m => string.CompareOrdinal(m.Key, key) > 0)
                   .OrderBy(m => m.Key, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Creates the month, copying salary, allocation, recurring transactions and the next installments from the template.</summary>
    public BudgetMonth CreateMonth(DateTime month)
    {
        var template = TemplateFor(month);
        var created = new BudgetMonth { Key = Key(month) };
        if (template is null)
        {
            Seed(created);
        }
        else
        {
            created.Salary = template.Salary;
            created.Allocations = template.Allocations
                .Select(a => new Allocation { CategoryId = a.CategoryId, Percent = a.Percent, FixedAmount = a.FixedAmount })
                .ToList();
            foreach (var t in template.Transactions.Where(t => t.Recurring && t.Installment is null))
            {
                created.Transactions.Add(new Transaction
                {
                    Date = SameDay(t.Date, month),
                    Description = t.Description,
                    Amount = t.Amount,
                    CategoryId = t.CategoryId,
                    Recurring = true,
                    Payment = t.Payment,
                });
            }
            // each installment moves on by the months in between; the purchase stops after the last one
            int distance = MonthsBetween(template.Start, month);
            foreach (var t in template.Transactions.Where(t => t.Installment is not null))
            {
                int number = t.Installment!.Number + distance;
                if (number >= 1 && number <= t.Installment.Count) created.Transactions.Add(InstallmentOf(t, number, month));
            }
        }
        Data.Months.Add(created);
        return created;
    }

    /// <summary>
    /// After an installment is saved: the months after this one that already exist get the following installments
    /// (same description, category, day and plan), and none past the last one. Earlier months stay as they are.
    /// </summary>
    public void SyncInstallments(BudgetMonth month, Transaction source)
    {
        var plan = source.Installment!;
        foreach (var later in Data.Months.Where(m => string.CompareOrdinal(m.Key, month.Key) > 0))
        {
            later.Transactions.RemoveAll(t => t.Installment?.PurchaseId == plan.PurchaseId);
            int number = plan.Number + MonthsBetween(month.Start, later.Start);
            if (number <= plan.Count) later.Transactions.Add(InstallmentOf(source, number, later.Start));
        }
    }

    /// <summary>Removes a purchase's installments from this month on; earlier months keep theirs. Returns how many came out.</summary>
    public int RemoveInstallments(BudgetMonth from, Guid purchaseId) =>
        Data.Months.Where(m => string.CompareOrdinal(m.Key, from.Key) >= 0)
            .Sum(m => m.Transactions.RemoveAll(t => t.Installment?.PurchaseId == purchaseId));

    /// <summary>Installment <paramref name="number"/> of the same purchase as <paramref name="source"/>, dated in <paramref name="month"/>.</summary>
    static Transaction InstallmentOf(Transaction source, int number, DateTime month) => new()
    {
        Date = SameDay(source.Date, month),
        Description = source.Description,
        Amount = source.Installment!.AmountOf(number),
        CategoryId = source.CategoryId,
        Payment = source.Payment,
        Installment = source.Installment with { Number = number },
    };

    /// <summary>The same day of the month, in another month (the 31st becomes the last day when the month is shorter).</summary>
    static DateTime SameDay(DateTime date, DateTime month) =>
        new(month.Year, month.Month, Math.Min(date.Day, DateTime.DaysInMonth(month.Year, month.Month)));

    public static int MonthsBetween(DateTime from, DateTime to) => (to.Year - from.Year) * 12 + to.Month - from.Month;

    /// <summary>
    /// Removes the month from the data and saves. Before that, keeps a copy of the file next to it, so it can be undone.
    /// Categories no month uses anymore go too. Returns the copy's path (null if there was no file yet).
    /// </summary>
    public string? DeleteMonth(BudgetMonth month)
    {
        string? copy = File.Exists(FilePath) ? BackUp($"antes-de-excluir-{month.Key}") : null;

        int position = Data.Months.IndexOf(month);
        var categoriesBefore = Data.Categories.ToList();
        Data.Months.Remove(month);
        var used = Data.Months
            .SelectMany(m => m.Allocations.Select(a => a.CategoryId).Concat(m.Transactions.Select(t => t.CategoryId)))
            .ToHashSet();
        Data.Categories.RemoveAll(c => !used.Contains(c.Id));
        try
        {
            Save();
        }
        catch
        {
            // couldn't write: undo in memory, so the screen and the file stay the same
            Data.Months.Insert(Math.Max(0, position), month);
            Data.Categories.Clear();
            Data.Categories.AddRange(categoriesBefore);
            throw;
        }
        return copy;
    }

    /// <summary>Does the category show up in any month besides this one?</summary>
    public bool IsUsedInOtherMonth(Category category, BudgetMonth thisMonth) =>
        Data.Months.Any(m => m != thisMonth &&
            (m.Allocations.Any(a => a.CategoryId == category.Id) || m.Transactions.Any(t => t.CategoryId == category.Id)));

    void Seed(BudgetMonth month)
    {
        for (int i = 0; i < SuggestedSplit.Length; i++)
        {
            var category = new Category { Name = SuggestedSplit[i].Name, Kind = SuggestedSplit[i].Kind, Color = i };
            Data.Categories.Add(category);
            month.Allocations.Add(new Allocation { CategoryId = category.Id, Percent = SuggestedSplit[i].Percent });
        }
    }
}

/// <summary>The data file exists but couldn't be read (locked by another program, no permission).</summary>
public class DataFileUnavailableException : Exception
{
    public DataFileUnavailableException(string path, Exception cause)
        : base($"Não foi possível abrir o arquivo de dados:\n{path}\n\n{cause.Message}", cause) { }
}
