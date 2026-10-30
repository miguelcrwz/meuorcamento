using System.Windows;
using System.Windows.Controls;
using MeuOrcamento.Controls;

namespace MeuOrcamento.Editors;

/// <summary>How much of the income goes to each category, in % or in R$.</summary>
public partial class AllocationEditor : UserControl, IEditor
{
    const int MaxCategories = CategoryColors.Count; // one distinct color per category

    sealed class Row
    {
        public Category? Existing;
        public int Color;
        /// <summary>true when set in R$ (fixed amount); false when in %.</summary>
        public bool FixedMode;
        public Border Visual = null!;
        public TextBlock Number = null!;
        public TextBox Name = null!;
        public RadioButton Investment = null!;
        public NumberBox Percent = null!, Amount = null!;
    }

    readonly Repository _repo;
    readonly BudgetMonth _month;
    readonly decimal _income;
    readonly List<Row> _rows = new();

    public AllocationEditor(Repository repo, BudgetMonth month)
    {
        _repo = repo;
        _month = month;
        _income = month.Income;
        InitializeComponent();
        Overline = $"DISTRIBUIÇÃO · {Format.ShortMonth(month.Start)}";
        Intro.Text = _income > 0
            ? $"Renda de {Format.LongMonth(month.Start).ToLower(Format.PtBr)}: {Format.Money(_income)}. Cada linha é uma parte do salário."
            : "A renda deste mês ainda não foi informada. Por enquanto, dá para definir só os percentuais.";
        foreach (var a in month.Allocations)
            if (repo.FindCategory(a.CategoryId) is { } c)
                AddRow(c, c.Name, c.Kind, c.Color, month.EffectivePercent(a), a.FixedAmount);
        Refresh();
    }

    public string Overline { get; }
    public string Title => "Para onde vai o salário";
    public double DrawerWidth => 800;

    Row AddRow(Category? existing, string name, CategoryKind kind, int color, decimal pct, decimal? fixedAmount)
    {
        var row = new Row { Existing = existing, Color = color, FixedMode = fixedAmount.HasValue };
        var grid = new Grid { MinHeight = 54 };
        foreach (var width in new[] { 48.0, double.NaN, 204, 96, 140, 44 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(width) ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });

        row.Number = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        row.Number.SetResourceReference(StyleProperty, "Label");
        var number = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        number.Children.Add(CategoryColors.Marker(color));
        number.Children.Add(row.Number);

        row.Name = new TextBox { Text = name, VerticalAlignment = VerticalAlignment.Center };
        row.Name.SetResourceReference(StyleProperty, "NameField");
        Grid.SetColumn(row.Name, 1);

        var group = Guid.NewGuid().ToString();
        var expense = new RadioButton { Content = "GASTO", GroupName = group, IsChecked = kind == CategoryKind.Expense };
        row.Investment = new RadioButton { Content = "INVESTIMENTO", GroupName = group, IsChecked = kind == CategoryKind.Investment, Margin = new Thickness(-1, 0, 0, 0) };
        expense.SetResourceReference(StyleProperty, "Segment");
        row.Investment.SetResourceReference(StyleProperty, "Segment");
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        kinds.Children.Add(expense);
        kinds.Children.Add(row.Investment);
        Grid.SetColumn(kinds, 2);

        row.Percent = new NumberBox { Decimals = 1, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
        row.Percent.SetResourceReference(StyleProperty, "TableField");
        row.Percent.Value = Math.Clamp(Math.Round(pct, 2), 0, 100);
        Grid.SetColumn(row.Percent, 3);

        row.Amount = new NumberBox { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = _income > 0 };
        row.Amount.SetResourceReference(StyleProperty, "TableField");
        row.Amount.Value = Math.Max(0, fixedAmount ?? Math.Round(_income * pct / 100m, 2));
        Grid.SetColumn(row.Amount, 4);

        var remove = new Button { Content = "", ToolTip = "Remover categoria", HorizontalAlignment = HorizontalAlignment.Right };
        remove.SetResourceReference(StyleProperty, "IconButton");
        Grid.SetColumn(remove, 5);

        grid.Children.Add(number);
        grid.Children.Add(row.Name);
        grid.Children.Add(kinds);
        grid.Children.Add(row.Percent);
        grid.Children.Add(row.Amount);
        grid.Children.Add(remove);
        row.Visual = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        row.Visual.SetResourceReference(Border.BorderBrushProperty, "Rule");

        // % and R$ follow each other; the last one the person touched decides the mode
        row.Percent.ValueTyped += (_, _) =>
        {
            row.FixedMode = false;
            row.Amount.Value = Math.Round(_income * row.Percent.Value / 100m, 2);
            Refresh();
        };
        row.Amount.ValueTyped += (_, _) =>
        {
            if (_income <= 0) return;
            row.FixedMode = true;
            row.Percent.Value = Math.Clamp(Math.Round(row.Amount.Value / _income * 100m, 2), 0, 100);
            Refresh();
        };
        row.Name.TextChanged += (_, _) => Refresh();
        remove.Click += (_, _) => RemoveRow(row);

        _rows.Add(row);
        RowsPanel.Children.Add(row.Visual);
        return row;
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count >= MaxCategories) return;
        var used = _rows.Select(r => r.Color).ToHashSet();
        int color = Enumerable.Range(0, MaxCategories).First(i => !used.Contains(i));
        var added = AddRow(null, "", CategoryKind.Expense, color, 0, null);
        Refresh();
        added.Name.Focus();
    }

    void RemoveRow(Row row)
    {
        if (row.Existing != null)
        {
            int count = _month.Transactions.Count(t => t.CategoryId == row.Existing.Id);
            if (count > 0)
            {
                RowsWarning.Text = $"■ “{row.Existing.Name.ToUpper(Format.PtBr)}” TEM {count} LANÇAMENTO(S) NESTE MÊS. MOVA OU EXCLUA ANTES DE REMOVER.";
                return;
            }
        }
        RowsWarning.Text = "";
        _rows.Remove(row);
        RowsPanel.Children.Remove(row.Visual);
        Refresh();
    }

    decimal AmountOf(Row r) => r.FixedMode ? r.Amount.Value : Math.Round(_income * r.Percent.Value / 100m, 2);
    decimal PercentOf(Row r) => _income > 0 ? AmountOf(r) / _income * 100m : r.Percent.Value;

    /// <summary>What still has no destination: in R$ when there is income, in percentage points when there isn't.</summary>
    decimal Remainder() => _income > 0 ? _income - _rows.Sum(AmountOf) : 100m - _rows.Sum(r => r.Percent.Value);
    decimal RemainderPercent(decimal rest) => _income > 0 ? rest / _income * 100m : rest;
    bool IsClosed(decimal rest) => _income > 0 ? Format.IsNegligible(rest, _income) : Math.Abs(rest) < 0.01m;
    string Describe(decimal rest) =>
        _income > 0 ? $"{Format.Percent(RemainderPercent(rest))} · {Format.Money(rest)}" : Format.Percent(rest);

    void Refresh()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].Number.Text = (i + 1).ToString("00");

        decimal rest = Remainder();
        var segments = _rows.Select(r => new StripSegment(null,
            r.Name.Text.Trim().Length > 0 ? r.Name.Text.Trim().ToUpper(Format.PtBr) : "SEM NOME",
            (double)(PercentOf(r) / 100m), Format.Percent(PercentOf(r)), "", false, 1, false, "", CategoryColors.For(r.Color))).ToList();
        if (!IsClosed(rest) && rest > 0)
            segments.Add(new StripSegment(null, "SEM DESTINO", (double)(RemainderPercent(rest) / 100m), Format.Percent(RemainderPercent(rest)), "", false, 0, true, ""));
        Preview.SetSegments(segments);

        if (IsClosed(rest))
        {
            Summary.Text = "ALOCADO 100%";
            Summary.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        }
        else if (rest > 0)
        {
            Summary.Text = $"ALOCADO {Format.Percent(100 - RemainderPercent(rest))} · FALTAM {Describe(rest)}";
            Summary.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
        }
        else
        {
            Summary.Text = $"■ EXCEDE {Describe(-rest)}";
            Summary.SetResourceReference(TextBlock.ForegroundProperty, "Alert");
        }
        AddButton.IsEnabled = _rows.Count < MaxCategories;
    }

    public IInputElement? FirstField => _rows.FirstOrDefault()?.Percent;

    public string? Problem()
    {
        if (_rows.Any(r => string.IsNullOrWhiteSpace(r.Name.Text))) return "Dê um nome para cada categoria.";
        var repeated = _rows.GroupBy(r => r.Name.Text.Trim(), StringComparer.CurrentCultureIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (repeated != null) return $"Há mais de uma categoria chamada “{repeated.Key}”.";
        var rest = Remainder();
        if (!IsClosed(rest) && rest < 0) return $"A distribuição passa da renda em {Describe(-rest)}.";
        return null;
    }

    public string? Warning()
    {
        var rest = Remainder();
        return !IsClosed(rest) && rest > 0 ? $"Faltam {Describe(rest)} sem destino; vai aparecer como não distribuído." : null;
    }

    public void Apply()
    {
        var allocations = new List<Allocation>();
        foreach (var r in _rows)
        {
            string name = r.Name.Text.Trim();
            var kind = r.Investment.IsChecked == true ? CategoryKind.Investment : CategoryKind.Expense;
            Category category;
            if (r.Existing is null)
            {
                category = new Category { Color = r.Color, Name = name, Kind = kind };
                _repo.Data.Categories.Add(category);
            }
            else if ((r.Existing.Name != name || r.Existing.Kind != kind) && _repo.IsUsedInOtherMonth(r.Existing, _month))
            {
                // the category shows up in other months: the change applies from this month on, without rewriting history
                category = new Category { Color = r.Existing.Color, Name = name, Kind = kind };
                _repo.Data.Categories.Add(category);
                foreach (var transaction in _month.Transactions.Where(t => t.CategoryId == r.Existing.Id))
                    transaction.CategoryId = category.Id;
            }
            else
            {
                category = r.Existing;
                category.Name = name;
                category.Kind = kind;
            }
            allocations.Add(new Allocation
            {
                CategoryId = category.Id,
                Percent = r.FixedMode ? Math.Round(PercentOf(r), 2) : r.Percent.Value,
                FixedAmount = r.FixedMode ? r.Amount.Value : null,
            });
        }
        _month.Allocations = allocations;
    }
}
