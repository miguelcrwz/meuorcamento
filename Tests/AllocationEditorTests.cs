using System.Collections;
using System.Windows.Controls;
using MeuOrcamento.Controls;
using MeuOrcamento.Editors;
using static MeuOrcamento.Tests.Harness;

namespace MeuOrcamento.Tests;

/// <summary>The allocation drawer: % and R$, limits, names, and category history.</summary>
static class AllocationEditorTests
{
    public static void Run()
    {
        Section("ALLOCATION");
        var repo = NewRepository("allocation.json");
        var month = repo.CreateMonth(new DateTime(2026, 10, 1));
        month.Salary = 5000;
        month.Transactions.Add(new Transaction { Date = new DateTime(2026, 10, 2), Description = "X", Amount = 10, CategoryId = month.Allocations[0].CategoryId });

        var ed = new AllocationEditor(repo, month);
        var rows = Field<IList>(ed, "_rows");
        T Of<T>(int i, string field) => (T)rows[i]!.GetType().GetField(field)!.GetValue(rows[i])!;

        Check("opens with the 6 categories", rows.Count == 6);
        Check("the suggested split closes at 100%: no warning", ed.Warning() == null && ed.Problem() == null, $"{ed.Warning()} {ed.Problem()}");
        Of<NumberBox>(1, "Percent").Text = "20";
        Check("changing the % updates the R$", Of<NumberBox>(1, "Amount").Value == 1000, $"{Of<NumberBox>(1, "Amount").Value}");
        Check("going over 100% blocks saving", ed.Problem()?.Contains("passa") == true, ed.Problem());
        Of<NumberBox>(1, "Amount").Text = "250";
        Check("changing the R$ updates the %", Of<NumberBox>(1, "Percent").Value == 5, $"{Of<NumberBox>(1, "Percent").Value}");
        Check("% left over: asks for confirmation", ed.Warning() != null, ed.Warning());

        Call(ed, "RemoveRow", rows[0]);
        Check("doesn't remove a category that has transactions", rows.Count == 6 && Field<TextBlock>(ed, "RowsWarning").Text.Length > 0);
        Call(ed, "RemoveRow", rows[5]);
        Check("removes a category without transactions", rows.Count == 5, $"{rows.Count}");
        for (int i = 0; i < 3; i++) Call(ed, "Add_Click", null, null);
        Check("limit of 7 categories", rows.Count == 7 && !Field<Button>(ed, "AddButton").IsEnabled, $"{rows.Count}");
        Check("an empty name blocks saving", ed.Problem()?.Contains("nome") == true, ed.Problem());
        Of<TextBox>(5, "Name").Text = "Saúde";
        Of<TextBox>(6, "Name").Text = "saúde";
        Check("a repeated name blocks saving", ed.Problem()?.Contains("mais de uma") == true, ed.Problem());
        Of<TextBox>(6, "Name").Text = "Pets";
        Of<NumberBox>(5, "Percent").Text = "10";
        Of<NumberBox>(6, "Percent").Text = "5";
        Check("closing at 100%: saving is allowed", ed.Warning() == null && ed.Problem() == null, $"{ed.Warning()} {ed.Problem()}");

        ed.Apply();
        Check("writes 7 allocations", month.Allocations.Count == 7, $"{month.Allocations.Count}");
        Check("the fixed amount is written", month.Allocations[1].FixedAmount == 250, $"{month.Allocations[1].FixedAmount}");
        Check("total budgeted = income", month.TotalBudgeted == 5000, $"{month.TotalBudgeted}");
        Check("each category has its own color", month.Allocations.Select(a => repo.FindCategory(a.CategoryId)!.Color).Distinct().Count() == 7);

        CategoryHistory(repo, month);
    }

    /// <summary>Renaming / changing the kind of a category used in another month doesn't rewrite the past.</summary>
    static void CategoryHistory(Repository repo, BudgetMonth october)
    {
        var november = repo.CreateMonth(new DateTime(2026, 11, 1));
        var leisure = repo.FindCategory(october.Allocations[3].CategoryId)!;
        october.Transactions.Add(new Transaction { Date = new DateTime(2026, 10, 5), Description = "Show", Amount = 500, CategoryId = leisure.Id });
        november.Transactions.Add(new Transaction { Date = new DateTime(2026, 11, 5), Description = "Cinema", Amount = 80, CategoryId = leisure.Id });

        var ed = new AllocationEditor(repo, november);
        var row = Field<IList>(ed, "_rows")[3]!;
        ((TextBox)row.GetType().GetField("Name")!.GetValue(row)!).Text = "Diversão";
        ((RadioButton)row.GetType().GetField("Investment")!.GetValue(row)!).IsChecked = true;
        ed.Apply();

        var created = repo.FindCategory(november.Allocations[3].CategoryId)!;
        Check("the old month keeps its name and kind", leisure.Name == "Lazer" && leisure.Kind == CategoryKind.Expense, $"{leisure.Name}/{leisure.Kind}");
        Check("the current month uses the new category, with the same color",
            created.Id != leisure.Id && created.Name == "Diversão" && created.Kind == CategoryKind.Investment && created.Color == leisure.Color);
        Check("this month's transactions move to the new category; older ones stay",
            november.Transactions.Single(x => x.Description == "Cinema").CategoryId == created.Id &&
            october.Transactions.Single(x => x.Description == "Show").CategoryId == leisure.Id);
    }
}
