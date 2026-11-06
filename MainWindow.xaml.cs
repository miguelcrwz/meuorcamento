using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MeuOrcamento.Controls;
using MeuOrcamento.Editors;
using Microsoft.Win32;

namespace MeuOrcamento;

public partial class MainWindow : Window
{
    static DateTime ThisMonth => new(DateTime.Today.Year, DateTime.Today.Month, 1);

    readonly Repository _repo;
    DateTime _month = ThisMonth;
    Guid? _filter;

    // drawer
    IEditor? _editor;
    TaskCompletionSource<bool>? _drawerResult;
    string? _warningShown;

    public MainWindow(Repository repo)
    {
        _repo = repo;
        InitializeComponent();

        // small screens (or Windows zoom): the window and its minimum never exceed the work area
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width - 32));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height - 32));

        Strip.SegmentClicked += id => _ = NewTransaction(id);
        SourceInitialized += (_, _) => NativeWindow.Apply(this);
        StateChanged += (_, _) => AdjustForMaximized();
        Loaded += async (_, _) => await FirstRun();
        FolderButton.ToolTip = $"Pasta dos dados · {_repo.FilePath}";
        Refresh();
    }

    // ───────────── navigation ─────────────

    public void Navigate(int page)
    {
        (page switch { 1 => NavTransactions, 2 => NavHistory, _ => NavSummary }).IsChecked = true;
    }

    public void GoTo(DateTime month)
    {
        _month = new DateTime(month.Year, month.Month, 1);
        Refresh();
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (SummaryPage is null) return; // window still being built
        ShowPage();
    }

    void ShowPage()
    {
        int page = NavTransactions.IsChecked == true ? 1 : NavHistory.IsChecked == true ? 2 : 0;
        bool exists = _repo.FindMonth(_month) != null;
        SummaryPage.Visibility = VisibleIf(page == 0 && exists);
        TransactionsPage.Visibility = VisibleIf(page == 1 && exists);
        HistoryPage.Visibility = VisibleIf(page == 2);
        EmptyPage.Visibility = VisibleIf(page != 2 && !exists);
    }

    static Visibility VisibleIf(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    void PreviousMonth_Click(object sender, RoutedEventArgs e) => GoTo(_month.AddMonths(-1));
    void NextMonth_Click(object sender, RoutedEventArgs e) => GoTo(_month.AddMonths(1));
    void ThisMonth_Click(object sender, RoutedEventArgs e) => GoTo(ThisMonth);

    // ───────────── screen ─────────────

    void Refresh()
    {
        MonthText.Text = Format.ShortMonth(_month);
        TodayButton.Visibility = VisibleIf(_month != ThisMonth);
        var month = _repo.FindMonth(_month);
        if (month != null)
        {
            RefreshSummary(month);
            RefreshFilters(month);
            FillTransactions();
        }
        else
        {
            RefreshEmpty();
        }
        FillHistory();
        ShowPage();
    }

    (decimal Expenses, decimal Invested) Totals(BudgetMonth month)
    {
        decimal expenses = 0, invested = 0;
        foreach (var t in month.Transactions)
        {
            if (_repo.FindCategory(t.CategoryId)?.Kind == CategoryKind.Investment) invested += t.Amount;
            else expenses += t.Amount;
        }
        return (expenses, invested);
    }

    void Tint(TextBlock text, bool alert, string normal = "Ink3") =>
        text.SetResourceReference(TextBlock.ForegroundProperty, alert ? "Alert" : normal);

    void RefreshSummary(BudgetMonth month)
    {
        var (expenses, invested) = Totals(month);
        decimal income = month.Income, balance = income - expenses - invested;

        HeroLabel.Text = $"SALDO LIVRE · {Format.ShortMonth(_month)}";
        DeleteMonthButton.Content = $"EXCLUIR {Format.ShortMonth(_month)}";
        HeroCurrency.Text = balance < 0 ? "−R$" : "R$";
        HeroValue.Text = Format.Number(Math.Abs(balance));
        Tint(HeroValue, balance < 0, "Ink");
        HeroNote.Text = income == 0
            ? "Comece informando a renda do mês."
            : balance < 0
                ? "Já foi lançado mais do que a renda deste mês."
                : $"Renda menos tudo que já foi lançado; {Format.Percent((expenses + invested) / income * 100m)} da renda já tem uso.";

        decimal budgetedExpenses = 0, budgetedInvested = 0;
        var rows = new List<CategoryRow>();
        var segments = new List<StripSegment>();
        int number = 0;
        foreach (var a in month.Allocations)
        {
            if (_repo.FindCategory(a.CategoryId) is not { } c) continue;
            decimal budgeted = month.Budgeted(a), pct = month.EffectivePercent(a), posted = month.Posted(c.Id), rest = budgeted - posted;
            bool investment = c.Kind == CategoryKind.Investment;
            if (investment) budgetedInvested += budgeted; else budgetedExpenses += budgeted;
            // amounts are exact to the cent: any cent over the plan is an overspend
            bool over = !investment && rest < 0;
            double usage = budgeted > 0 ? (double)(posted / budgeted) : posted > 0 ? 1 : 0;

            var color = CategoryColors.For(c.Color);
            // a goal is only "ok" when there is a goal; with nothing planned, show a dash
            bool noGoal = investment && budgeted <= 0;
            string remaining = noGoal ? "—"
                : investment ? (rest > 0 ? Format.Number(rest) : "META OK")
                : Format.SignedNumber(rest);
            rows.Add(new CategoryRow(c.Id, (++number).ToString("00"), c.Name, color, investment ? "INVESTIMENTO" : "GASTO",
                Format.Number(budgeted), Format.Number(posted), remaining, over, usage, $"{usage * 100:0}%"));

            string status = noGoal ? "SEM META"
                : investment ? (rest > 0 ? $"FALTA {Format.Number(rest)}" : "META OK")
                : over ? $"+{Format.Number(-rest)}" : $"LIVRE {Format.Number(rest)}";
            string tooltip = $"{c.Name.ToUpper(Format.PtBr)} · {Format.Percent(pct)} DA RENDA\nPLANEJADO {Format.Number(budgeted)} · LANÇADO {Format.Number(posted)}\nCLIQUE PARA LANÇAR";
            segments.Add(new StripSegment(c.Id, c.Name.ToUpper(Format.PtBr), (double)(pct / 100m),
                $"{Format.Percent(pct)} · {Format.Number(budgeted)}", status, over, usage, false, tooltip, color));
        }

        decimal unassigned = income - month.TotalBudgeted;
        decimal totalPct = income > 0 ? month.TotalBudgeted / income * 100m : month.Allocations.Sum(a => a.Percent);
        if (income > 0 && unassigned > 0 && !Format.IsNegligible(unassigned, income))
            segments.Add(new StripSegment(null, "SEM DESTINO", (double)(unassigned / income), Format.Percent(unassigned / income * 100m),
                Format.Number(unassigned), false, 0, true, $"SEM DESTINO · {Format.Money(unassigned)}\nAJUSTE EM DISTRIBUIÇÃO"));
        else if (income == 0 && totalPct < 100)
            segments.Add(new StripSegment(null, "SEM DESTINO", (double)((100 - totalPct) / 100m), Format.Percent(100 - totalPct), "", false, 0, true, "SEM DESTINO"));
        Strip.SetSegments(segments);

        bool exceeds = income > 0 && unassigned < 0 && !Format.IsNegligible(unassigned, income);
        AllocatedText.Text = exceeds ? $"■ EXCEDE A RENDA EM {Format.Number(-unassigned)}" : $"ALOCADO {Format.Percent(totalPct)}";
        Tint(AllocatedText, exceeds, "Ink2");

        CategoryTable.ItemsSource = rows;
        NoCategories.Visibility = VisibleIf(rows.Count == 0);

        // fact sheet
        IncomeValue.Text = Format.Number(income);
        IncomeDetail.Text = income == 0 ? "NÃO INFORMADA"
            : month.ExtraIncome > 0 ? $"SALÁRIO {Format.Number(month.Salary)} + EXTRAS {Format.Number(month.ExtraIncome)}"
            : "SALÁRIO LÍQUIDO";

        ExpensesValue.Text = Format.Number(expenses);
        bool expensesOver = budgetedExpenses > 0 && expenses > budgetedExpenses;
        ExpensesBar.Value = budgetedExpenses > 0 ? (double)(expenses / budgetedExpenses) : 0;
        ExpensesBar.Alert = expensesOver;
        ExpensesDetail.Text = budgetedExpenses <= 0 ? "NADA PLANEJADO PARA GASTOS"
            : expensesOver ? $"■ {Format.Number(expenses - budgetedExpenses)} ACIMA DOS {Format.Number(budgetedExpenses)} PLANEJADOS"
            : $"{Format.Percent(expenses / budgetedExpenses * 100m)} DE {Format.Number(budgetedExpenses)} PLANEJADOS";
        Tint(ExpensesDetail, expensesOver);

        InvestedValue.Text = Format.Number(invested);
        InvestedBar.Value = budgetedInvested > 0 ? (double)(invested / budgetedInvested) : 0;
        InvestedDetail.Text = budgetedInvested <= 0 ? "NENHUMA META DE INVESTIMENTO"
            : invested >= budgetedInvested ? $"META DE {Format.Number(budgetedInvested)} ATINGIDA"
            : $"META {Format.Number(budgetedInvested)} · FALTAM {Format.Number(budgetedInvested - invested)}";
    }

    void RefreshFilters(BudgetMonth month)
    {
        Filters.Children.Clear();
        void Option(string text, Guid? id, int? color)
        {
            object content = text;
            if (color is int slot)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(CategoryColors.Marker(slot, 7));
                row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0) });
                content = row;
            }
            var button = new RadioButton { Content = content, GroupName = "filter", IsChecked = _filter == id };
            button.SetResourceReference(StyleProperty, "Filter");
            button.Checked += (_, _) => { _filter = id; FillTransactions(); };
            Filters.Children.Add(button);
        }
        if (_filter is Guid current && month.Allocations.All(a => a.CategoryId != current)) _filter = null;
        Option("TODAS", null, null);
        foreach (var a in month.Allocations)
            if (_repo.FindCategory(a.CategoryId) is { } c) Option(c.Name.ToUpper(Format.PtBr), c.Id, c.Color);
    }

    void FillTransactions()
    {
        if (_repo.FindMonth(_month) is not { } month) return;
        var term = Search.Text.Trim();
        SearchHint.Visibility = VisibleIf(Search.Text.Length == 0);
        var items = month.Transactions
            .Where(t => _filter == null || t.CategoryId == _filter)
            // the search also finds the payment method: "pix", "credito"
            .Where(t => term.Length == 0 || Format.Contains(t.Description, term) || Format.Contains(Format.Payment(t.Payment), term))
            .OrderByDescending(t => t.Date)
            .ThenBy(t => t.Description, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var rows = new List<TransactionRow>();
        DateTime? previous = null;
        foreach (var t in items)
        {
            // the date only shows on the first row of each day
            var category = _repo.FindCategory(t.CategoryId);
            rows.Add(new TransactionRow(t, t.Date.Date == previous ? "" : Format.Day(t.Date), t.Description,
                category?.Name ?? "—", CategoryColors.For(category?.Color ?? 0), Format.Payment(t.Payment), Repeats(t), Format.Number(t.Amount)));
            previous = t.Date.Date;
        }
        TransactionTable.ItemsSource = rows;

        TransactionsNote.Text = Format.LongMonth(_month).ToLower(Format.PtBr);
        TransactionsCount.Text = $"{items.Count:00} LANÇAMENTO(S){(_filter != null || term.Length > 0 ? " · FILTRADO" : "")}";
        TransactionsTotal.Text = Format.Number(items.Sum(t => t.Amount));
        // how much went on each method, e.g. what the credit card bill will have from this month
        PaymentTotals.Text = string.Join("   ", items.Where(t => t.Payment != null)
            .GroupBy(t => t.Payment!.Value).OrderBy(g => g.Key)
            .Select(g => $"{Format.Payment(g.Key)} {Format.Number(g.Sum(t => t.Amount))}"));
        NoTransactions.Text = items.Count > 0 ? ""
            : month.Transactions.Count == 0 ? "Nenhum lançamento neste mês ainda." : "Nada com esse filtro.";
    }

    /// <summary>"MENSAL", "3 DE 10" for an installment, or nothing.</summary>
    static string Repeats(Transaction t) =>
        t.Installment is { } plan ? $"{plan.Number} DE {plan.Count}" : t.Recurring ? "MENSAL" : "";

    void FillHistory()
    {
        var months = _repo.Data.Months.OrderByDescending(m => m.Key, StringComparer.Ordinal).ToList();
        var totals = months.Select(m => (Month: m, T: Totals(m))).ToList();
        // every bar on the same scale: the largest of income and posted across all months
        decimal scale = totals.Select(x => Math.Max(x.Month.Income, x.T.Expenses + x.T.Invested)).DefaultIfEmpty(1).Max();
        if (scale <= 0) scale = 1;
        var key = Repository.Key(_month);

        HistoryTable.ItemsSource = totals.Select(x =>
        {
            var (expenses, invested) = x.T;
            decimal free = x.Month.Income - expenses - invested;
            var byCategory = PostedByCategory(x.Month);
            var breakdown = byCategory.Select(c => $"{c.Name} · {Format.Number(c.Amount)}").Append($"LIVRE · {Format.SignedNumber(free)}");
            return new HistoryRow(x.Month, Format.ShortMonth(x.Month.Start), x.Month.Key == key,
                Format.Number(x.Month.Income), Format.Number(expenses), Format.Number(invested), Format.SignedNumber(free), free < 0,
                x.Month.Income > 0 ? Format.Percent(invested / x.Month.Income * 100m) : "—",
                byCategory.Select(c => new BarPart((double)(c.Amount / scale), c.Color)).ToList(),
                (double)(Math.Max(0, free) / scale), string.Join("\n", breakdown));
        }).ToList();
    }

    /// <summary>What each category got in the month: expenses first, then investments, each group in the allocation order.</summary>
    List<(string Name, decimal Amount, Brush Color)> PostedByCategory(BudgetMonth month)
    {
        var order = month.Allocations.Select(a => a.CategoryId).ToList();
        int Position(Guid id) => order.IndexOf(id) is var i && i >= 0 ? i : int.MaxValue;
        return month.Transactions
            .GroupBy(t => t.CategoryId)
            .Select(g => (Id: g.Key, Category: _repo.FindCategory(g.Key), Amount: g.Sum(t => t.Amount)))
            .Where(c => c.Amount > 0)
            .OrderBy(c => c.Category?.Kind == CategoryKind.Investment ? 1 : 0)
            .ThenBy(c => Position(c.Id))
            .Select(c => ((c.Category?.Name ?? "Sem categoria").ToUpper(Format.PtBr), c.Amount,
                c.Category is { } category ? CategoryColors.For(category.Color) : (Brush)FindResource("Ink3")))
            .ToList();
    }

    void RefreshEmpty()
    {
        var template = _repo.TemplateFor(_month);
        EmptyMonth.Text = Format.ShortMonth(_month);
        PlanButton.Content = $"PLANEJAR {Format.ShortMonth(_month)}";
        if (template is null)
        {
            EmptyText.Text = "Primeiro mês: começa com uma distribuição sugerida, ajustável a qualquer momento.";
            EmptyItems.ItemsSource = new[] { "→ GASTOS FIXOS 45% · DIA A DIA 10% · LAZER 10%", "→ INVESTIMENTOS 20% · RESERVA 10% · EDUCAÇÃO 5%" };
            return;
        }
        int recurring = template.Transactions.Count(t => t.Recurring && t.Installment is null);
        int distance = Repository.MonthsBetween(template.Start, _month);
        int installments = template.Transactions.Count(t => t.Installment is { } p && p.Number + distance >= 1 && p.Number + distance <= p.Count);
        EmptyText.Text = $"Ainda não planejado. Ao planejar, vem de {Format.ShortMonth(template.Start)}:";
        var items = new List<string>
        {
            $"→ SALÁRIO {Format.Money(template.Salary)}",
            $"→ DISTRIBUIÇÃO EM {template.Allocations.Count} CATEGORIAS",
            $"→ {recurring} LANÇAMENTO(S) RECORRENTE(S)",
        };
        if (installments > 0) items.Add($"→ {installments} PARCELA(S) DE COMPRAS");
        EmptyItems.ItemsSource = items;
    }

    // ───────────── actions ─────────────

    void Save()
    {
        try
        {
            if (_repo.Save() is { } conflict)
                MessageBox.Show(this,
                    "O arquivo de dados foi alterado fora desta janela (outro computador ou outro programa).\n\n" +
                    $"Esta versão foi salva, e a versão de fora ficou guardada em:\n{conflict}",
                    "Meu Orçamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível salvar os dados:\n{ex.Message}", "Meu Orçamento", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        Refresh();
    }

    async Task FirstRun()
    {
        if (_repo.Data.Months.Count > 0) return;
        _repo.CreateMonth(_month);
        Save();
        await OpenIncome(welcome: true);
    }

    async void Plan_Click(object sender, RoutedEventArgs e)
    {
        bool first = _repo.Data.Months.Count == 0;
        _repo.CreateMonth(_month);
        Save();
        if (first) await OpenIncome(welcome: true);
    }

    async void Income_Click(object sender, RoutedEventArgs e) => await OpenIncome();
    async void Allocation_Click(object sender, RoutedEventArgs e) => await OpenAllocation();
    async void New_Click(object sender, RoutedEventArgs e) => await NewTransaction();

    async void Category_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CategoryRow row) await NewTransaction(row.Id);
    }

    async void Transaction_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is TransactionRow row) await Edit(row.Source);
    }

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is TransactionRow row) await Delete(row.Source);
    }

    void History_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryRow row) return;
        _month = row.Month.Start;
        Navigate(0);
        Refresh();
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e) => FillTransactions();

    async Task OpenIncome(bool welcome = false)
    {
        if (_repo.FindMonth(_month) is not { } month) return;
        if (await OpenDrawer(new IncomeEditor(month, welcome))) Save();
    }

    async Task OpenAllocation()
    {
        if (_repo.FindMonth(_month) is not { } month) return;
        if (await OpenDrawer(new AllocationEditor(_repo, month))) Save();
    }

    async Task NewTransaction(Guid? category = null)
    {
        if (_repo.FindMonth(_month) is not { } month) return;
        if (month.Allocations.Count == 0)
        {
            await OpenAllocation();
            return;
        }
        if (await OpenDrawer(new TransactionEditor(_repo, month, null, category))) Save();
    }

    async Task Edit(Transaction transaction)
    {
        if (_repo.FindMonth(_month) is not { } month) return;
        if (await OpenDrawer(new TransactionEditor(_repo, month, transaction, null))) Save();
    }

    async Task Delete(Transaction transaction)
    {
        if (_repo.FindMonth(_month) is not { } month) return;
        var category = _repo.FindCategory(transaction.CategoryId)?.Name ?? "sem categoria";
        string consequence = transaction.Installment is { } plan
            ? $"É a parcela {plan.Number} de {plan.Count}: saem esta e as próximas, as anteriores ficam. Essa exclusão não pode ser desfeita."
            : "Essa exclusão não pode ser desfeita.";
        var question = new ConfirmEditor($"EXCLUIR · {Format.Day(transaction.Date)}", transaction.Description,
            $"{Format.Money(transaction.Amount)} em {category}. {consequence}", "EXCLUIR");
        if (!await OpenDrawer(question)) return;
        // deleting an installment ends the purchase here; the ones already paid stay in their months
        if (transaction.Installment is { } installment) _repo.RemoveInstallments(month, installment.PurchaseId);
        else month.Transactions.Remove(transaction);
        Save();
    }

    async void DeleteMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_repo.FindMonth(_month) is { } month) await DeleteMonth(month);
    }

    async void DeleteHistoryMonth_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't open the month
        if ((sender as FrameworkElement)?.DataContext is HistoryRow row) await DeleteMonth(row.Month);
    }

    public async Task DeleteMonth(BudgetMonth month)
    {
        int count = month.Transactions.Count;
        string transactions = count switch { 0 => "nenhum lançamento", 1 => "o único lançamento", _ => $"os {count} lançamentos" };
        var question = new ConfirmEditor($"EXCLUIR MÊS · {Format.ShortMonth(month.Start)}",
            $"Excluir {Format.LongMonth(month.Start).ToLower(Format.PtBr)}?",
            $"Saem o salário, a distribuição e {transactions} deste mês. Os outros meses continuam como estão. " +
            "Antes de excluir, uma cópia do arquivo de dados fica guardada na pasta dos dados.",
            "EXCLUIR MÊS");
        if (!await OpenDrawer(question)) return;
        try { _repo.DeleteMonth(month); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível excluir o mês; nada foi alterado.\n\n{ex.Message}", "Meu Orçamento", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        Refresh();
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_repo.FindMonth(_month) is not { } month) return;
        var dialog = new SaveFileDialog { Filter = "CSV (abre no Excel)|*.csv", FileName = $"lancamentos-{month.Key}.csv" };
        if (dialog.ShowDialog(this) != true) return;

        static string Field(string s) => s.Contains(';') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        var csv = new StringBuilder("Data;Descrição;Categoria;Tipo;Valor;Recorrente;Parcela;Pagamento\r\n");
        foreach (var t in month.Transactions.OrderBy(t => t.Date))
        {
            var c = _repo.FindCategory(t.CategoryId);
            // the spreadsheet is read by people, so the kind goes out in Portuguese
            string kind = c is null ? "" : c.Kind == CategoryKind.Investment ? "Investimento" : "Gasto";
            // "3 de 10", not "3/10", which Excel would read as a date
            string installment = t.Installment is { } plan ? $"{plan.Number} de {plan.Count}" : "";
            csv.Append($"{t.Date:dd/MM/yyyy};{Field(t.Description)};{Field(c?.Name ?? "")};{kind};")
               .Append($"{t.Amount.ToString("0.00", Format.PtBr)};{(t.Recurring ? "Sim" : "Não")};{installment};")
               .Append($"{Format.Capitalize(Format.Payment(t.Payment).ToLower(Format.PtBr))}\r\n");
        }
        try { File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true)); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível salvar o arquivo:\n{ex.Message}", "Exportar CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(_repo.FilePath)!;
        Directory.CreateDirectory(folder);
        var arguments = File.Exists(_repo.FilePath) ? $"/select,\"{_repo.FilePath}\"" : $"\"{folder}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    // ───────────── window ─────────────

    void AdjustForMaximized()
    {
        // maximized, the window spills slightly past the screen; the margin makes up for it
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Sheet.Visibility == Visibility.Visible || Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.N: _ = NewTransaction(); e.Handled = true; break;
            case Key.PageUp: GoTo(_month.AddMonths(-1)); e.Handled = true; break;
            case Key.PageDown: GoTo(_month.AddMonths(1)); e.Handled = true; break;
        }
    }

    // ───────────── drawer ─────────────

    public Task<bool> OpenDrawer(IEditor editor)
    {
        _drawerResult?.TrySetResult(false);
        _editor = editor;
        _warningShown = null;
        DrawerOverline.Text = editor.Overline;
        DrawerHeading.Text = editor.Title;
        DrawerConfirm.Content = editor.ConfirmText;
        DrawerConfirm.SetResourceReference(StyleProperty, editor.IsDestructive ? "DangerButton" : "PrimaryButton");
        DrawerMessage.Text = "";
        Drawer.Width = Math.Min(editor.DrawerWidth, ActualWidth - 80);
        DrawerContent.Content = editor;
        // on destructive actions, Enter doesn't confirm and focus starts on CANCELAR
        DrawerConfirm.IsDefault = !editor.IsDestructive;
        DrawerCancel.IsCancel = true;
        Sheet.Visibility = Visibility.Visible;

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        Drawer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        DrawerOffset.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(48, 0, duration) { EasingFunction = ease });
        IInputElement focus = editor.FirstField ?? (editor.IsDestructive ? DrawerCancel : DrawerConfirm);
        Dispatcher.BeginInvoke(() => Keyboard.Focus(focus), System.Windows.Threading.DispatcherPriority.Input);

        _drawerResult = new TaskCompletionSource<bool>();
        return _drawerResult.Task;
    }

    public void CloseDrawer(bool confirmed)
    {
        Sheet.Visibility = Visibility.Collapsed;
        DrawerContent.Content = null;
        DrawerConfirm.IsDefault = false;
        DrawerCancel.IsCancel = false;
        _editor = null;
        var result = _drawerResult;
        _drawerResult = null;
        result?.TrySetResult(confirmed);
    }

    void DrawerConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        if (_editor.Problem() is { } problem)
        {
            DrawerMessage.Text = "■ " + problem.ToUpper(Format.PtBr);
            DrawerMessage.SetResourceReference(TextBlock.ForegroundProperty, "Alert");
            return;
        }
        if (_editor.Warning() is { } warning && warning != _warningShown)
        {
            _warningShown = warning;
            DrawerMessage.Text = warning.ToUpper(Format.PtBr);
            DrawerMessage.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
            DrawerConfirm.Content = "SALVAR ASSIM";
            return;
        }
        _editor.Apply();
        CloseDrawer(true);
    }

    void DrawerCancel_Click(object sender, RoutedEventArgs e) => CloseDrawer(false);
    void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => CloseDrawer(false);
}
