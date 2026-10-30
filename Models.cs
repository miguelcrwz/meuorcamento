using System.Globalization;
using System.Text.Json.Serialization;

namespace MeuOrcamento;

// The JSON names stay in Portuguese: dados.json files saved by earlier versions must keep loading.

public enum CategoryKind
{
    [JsonStringEnumMemberName("Gasto")] Expense,
    [JsonStringEnumMemberName("Investimento")] Investment,
}

/// <summary>A "share" of the salary: fixed costs, leisure, investments... The color is tied to the category.</summary>
public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("Nome")] public string Name { get; set; } = "";
    [JsonPropertyName("Tipo")] public CategoryKind Kind { get; set; }
    [JsonPropertyName("Cor")] public int Color { get; set; }
}

/// <summary>How much of a month's income goes to a category.</summary>
public class Allocation
{
    [JsonPropertyName("CategoriaId")] public Guid CategoryId { get; set; }
    [JsonPropertyName("Percentual")] public decimal Percent { get; set; }

    /// <summary>When set, the category gets this fixed amount instead of a percentage of the income.</summary>
    [JsonPropertyName("ValorFixo")] public decimal? FixedAmount { get; set; }
}

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("Data")] public DateTime Date { get; set; }
    [JsonPropertyName("Descricao")] public string Description { get; set; } = "";
    [JsonPropertyName("Valor")] public decimal Amount { get; set; }
    [JsonPropertyName("CategoriaId")] public Guid CategoryId { get; set; }

    /// <summary>Copied automatically when the next month is planned.</summary>
    [JsonPropertyName("Recorrente")] public bool Recurring { get; set; }

    /// <summary>Set when the transaction is one installment of a purchase split over several months.</summary>
    [JsonPropertyName("Parcelamento"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstallmentPlan? Installment { get; set; }
}

/// <summary>A purchase split over several months: each month gets the next installment, until the last one.</summary>
public record InstallmentPlan
{
    /// <summary>The same in every installment of the purchase.</summary>
    [JsonPropertyName("Compra")] public Guid PurchaseId { get; init; }

    /// <summary>Which installment this is, starting at 1.</summary>
    [JsonPropertyName("Parcela")] public int Number { get; init; }
    [JsonPropertyName("Parcelas")] public int Count { get; init; }
    [JsonPropertyName("Total")] public decimal Total { get; init; }

    /// <summary>The total split evenly, rounded to the cent.</summary>
    [JsonIgnore] public decimal Regular => Math.Round(Total / Count, 2);

    /// <summary>Installment n's amount: the regular one, with the 1st absorbing the rounding cents.</summary>
    public decimal AmountOf(int number) => number == 1 ? Total - Regular * (Count - 1) : Regular;
}

public class BudgetMonth
{
    [JsonPropertyName("Chave")] public string Key { get; set; } = "";
    [JsonPropertyName("Salario")] public decimal Salary { get; set; }
    [JsonPropertyName("RendaExtra")] public decimal ExtraIncome { get; set; }
    [JsonPropertyName("Alocacoes")] public List<Allocation> Allocations { get; set; } = new();
    [JsonPropertyName("Lancamentos")] public List<Transaction> Transactions { get; set; } = new();

    [JsonIgnore] public decimal Income => Salary + ExtraIncome;
    [JsonIgnore] public DateTime Start => DateTime.ParseExact(Key, "yyyy-MM", CultureInfo.InvariantCulture);
    [JsonIgnore] public decimal TotalBudgeted => Allocations.Sum(Budgeted);

    public decimal Budgeted(Allocation a) => a.FixedAmount ?? Math.Round(Income * a.Percent / 100m, 2);

    /// <summary>% of the income this share represents. A fixed amount with no income yet uses the % stored on save.</summary>
    public decimal EffectivePercent(Allocation a) =>
        a.FixedAmount is decimal amount && Income > 0 ? amount / Income * 100m : a.Percent;

    public decimal Posted(Guid categoryId) => Transactions.Where(t => t.CategoryId == categoryId).Sum(t => t.Amount);
}

public class BudgetData
{
    [JsonPropertyName("Versao")] public int Version { get; set; } = 1;
    [JsonPropertyName("Categorias")] public List<Category> Categories { get; set; } = new();
    [JsonPropertyName("Meses")] public List<BudgetMonth> Months { get; set; } = new();
}
