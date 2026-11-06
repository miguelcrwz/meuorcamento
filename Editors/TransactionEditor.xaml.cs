using System.Windows;
using System.Windows.Controls;

namespace MeuOrcamento.Editors;

public partial class TransactionEditor : UserControl, IEditor
{
    readonly Repository _repo;
    readonly BudgetMonth _month;
    readonly Transaction? _existing;
    readonly Dictionary<string, Transaction> _previous;
    readonly List<(RadioButton Option, Category Category, TextBlock Available)> _options = new();

    /// <summary>In a split purchase, whether the person last typed the installment (true) or the total (false).</summary>
    bool _installmentTypedLast;

    public TransactionEditor(Repository repo, BudgetMonth month, Transaction? existing, Guid? initialCategory)
    {
        _repo = repo;
        _month = month;
        _existing = existing;
        InitializeComponent();
        Overline = $"LANÇAMENTO · {Format.ShortMonth(month.Start)}";
        Title = existing is null ? "Novo lançamento" : "Editar lançamento";

        // the latest transaction for each description, to suggest category and amount
        _previous = repo.Data.Months
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .SelectMany(m => m.Transactions.OrderBy(t => t.Date))
            .Where(t => t.Description.Trim().Length > 0)
            .GroupBy(t => t.Description.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.CurrentCultureIgnoreCase);

        foreach (var a in month.Allocations)
            if (repo.FindCategory(a.CategoryId) is { } c) AddOption(c);
        if (existing != null && _options.All(o => o.Category.Id != existing.CategoryId) && repo.FindCategory(existing.CategoryId) is { } old)
            AddOption(old);

        var start = month.Start;
        Day.Maximum = DateTime.DaysInMonth(start.Year, start.Month);
        DayLabel.Text = $"DIA · {Format.PtBr.DateTimeFormat.GetMonthName(start.Month).ToUpper(Format.PtBr)}";

        if (existing != null)
        {
            Description.Text = existing.Description;
            Day.Value = Math.Clamp(existing.Date.Day, 1, Day.Maximum);
            Choose(existing.CategoryId);
            ChoosePayment(existing.Payment);
            if (existing.Installment is { } plan)
            {
                SplitOption.IsChecked = true;
                Amount.Value = plan.Total;
                InstallmentCount.Value = plan.Count;
                InstallmentNumber.Value = plan.Number;
            }
            else
            {
                Amount.Value = existing.Amount;
                MonthlyOption.IsChecked = existing.Recurring;
            }
        }
        else
        {
            bool thisMonth = DateTime.Today.Year == start.Year && DateTime.Today.Month == start.Month;
            Day.Value = thisMonth ? DateTime.Today.Day : 1;
            if (initialCategory is Guid id) Choose(id);
        }

        Amount.ValueTyped += (_, _) => TotalTyped();
        InstallmentAmount.ValueTyped += (_, _) => InstallmentTyped();
        InstallmentCount.ValueTyped += (_, _) => CountTyped();
        InstallmentNumber.ValueTyped += (_, _) => RefreshInstallments();
        foreach (var option in new[] { OnceOption, MonthlyOption, SplitOption })
            option.Checked += (_, _) => RepeatsChanged();
        // a purchase in installments is usually on the credit card; only when the person picks it, so a saved one stays as it was
        SplitOption.Checked += (_, _) => { if (ChosenPayment is null) ChoosePayment(PaymentMethod.Credit); };
        foreach (var option in PaymentOptions.Children.OfType<RadioButton>())
        {
            option.PreviewMouseLeftButtonDown += (_, e) => { if (option.IsChecked == true) { option.IsChecked = false; e.Handled = true; } };
            option.Checked += (_, _) => RefreshPaymentHelp();
            option.Unchecked += (_, _) => RefreshPaymentHelp();
        }
        Day.ValueTyped += (_, _) => RefreshDay();
        Description.TextChanged += (_, _) => ShowSuggestions();
        Description.LostKeyboardFocus += (_, e) => CompleteOnLeave(e.NewFocus as DependencyObject);
        RefreshAvailable();
        RefreshDay();
        RefreshPaymentHelp();
        RepeatsChanged();
    }

    public string Overline { get; }
    public string Title { get; }
    public IInputElement? FirstField => Amount;

    Category? Chosen => _options.FirstOrDefault(o => o.Option.IsChecked == true).Category;

    bool IsSplit => SplitOption.IsChecked == true;

    PaymentMethod? ChosenPayment =>
        CreditOption.IsChecked == true ? PaymentMethod.Credit
        : DebitOption.IsChecked == true ? PaymentMethod.Debit
        : PixOption.IsChecked == true ? PaymentMethod.Pix
        : null;

    void ChoosePayment(PaymentMethod? method)
    {
        CreditOption.IsChecked = method == PaymentMethod.Credit;
        DebitOption.IsChecked = method == PaymentMethod.Debit;
        PixOption.IsChecked = method == PaymentMethod.Pix;
    }

    // one short line, so it never wraps under the options
    void RefreshPaymentHelp() => PaymentHelp.Text = ChosenPayment is null ? "Opcional." : "Clique de novo para tirar.";

    void AddOption(Category category)
    {
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(CategoryColors.Marker(category.Color));
        name.Children.Add(new TextBlock { Text = category.Name, Margin = new Thickness(10, 0, 0, 0) });
        var available = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        available.SetResourceReference(StyleProperty, "Label");
        var content = new Grid();
        content.Children.Add(name);
        content.Children.Add(available);
        var option = new RadioButton { GroupName = "category", Content = content };
        option.SetResourceReference(StyleProperty, "ListOption");
        option.Checked += (_, _) => RefreshOutcome();
        Categories.Children.Add(option);
        _options.Add((option, category, available));
    }

    void Choose(Guid categoryId)
    {
        foreach (var o in _options)
            if (o.Category.Id == categoryId) o.Option.IsChecked = true;
    }

    (decimal Budgeted, decimal Others) Standing(Category c)
    {
        var allocation = _month.Allocations.FirstOrDefault(a => a.CategoryId == c.Id);
        decimal budgeted = allocation != null ? _month.Budgeted(allocation) : 0;
        decimal others = _month.Transactions.Where(t => t.CategoryId == c.Id && t != _existing).Sum(t => t.Amount);
        return (budgeted, others);
    }

    void RefreshAvailable()
    {
        foreach (var (_, category, label) in _options)
        {
            var (budgeted, others) = Standing(category);
            decimal rest = budgeted - others;
            if (category.Kind == CategoryKind.Investment)
                label.Text = budgeted <= 0 ? "SEM META" : rest > 0 ? $"FALTA {Format.Number(rest)}" : "META OK";
            else
                label.Text = Format.SignedNumber(rest);
            label.SetResourceReference(TextBlock.ForegroundProperty, rest < 0 && category.Kind == CategoryKind.Expense ? "Alert" : "Ink3");
        }
    }

    void RefreshDay()
    {
        var start = _month.Start;
        var date = new DateTime(start.Year, start.Month, (int)Math.Clamp(Day.Value, 1, Day.Maximum));
        // just the weekday: the label already names the month, and the full date would wrap beside the payment help
        Weekday.Text = Format.Capitalize(date.ToString("dddd", Format.PtBr));
    }

    // ───────────── installments ─────────────

    void RepeatsChanged()
    {
        InstallmentPanel.Visibility = IsSplit ? Visibility.Visible : Visibility.Collapsed;
        AmountLabel.Text = IsSplit ? "VALOR TOTAL · R$" : "VALOR · R$";
        RepeatsHelp.Text = IsSplit ? "Uma parcela por mês, até a última."
            : MonthlyOption.IsChecked == true ? "Conta fixa, assinatura, aporte."
            : "Entra só neste mês.";
        if (IsSplit && !_installmentTypedLast) InstallmentAmount.Value = RegularInstallment();
        RefreshInstallments();
    }

    // the total and the installment follow each other; whichever the person typed last stays as typed
    void TotalTyped()
    {
        _installmentTypedLast = false;
        if (IsSplit) InstallmentAmount.Value = RegularInstallment();
        RefreshInstallments();
    }

    void InstallmentTyped()
    {
        _installmentTypedLast = true;
        Amount.Value = InstallmentAmount.Value * InstallmentCount.Value;
        RefreshInstallments();
    }

    void CountTyped()
    {
        if (_installmentTypedLast) Amount.Value = InstallmentAmount.Value * InstallmentCount.Value;
        else InstallmentAmount.Value = RegularInstallment();
        RefreshInstallments();
    }

    decimal RegularInstallment() => Math.Round(Amount.Value / InstallmentCount.Value, 2);

    InstallmentPlan PlanFromFields(Guid purchaseId) => new()
    {
        PurchaseId = purchaseId,
        Number = (int)InstallmentNumber.Value,
        Count = (int)InstallmentCount.Value,
        Total = Amount.Value,
    };

    /// <summary>What goes into this month: the whole amount, or this month's installment.</summary>
    decimal ThisMonthAmount => IsSplit ? PlanFromFields(Guid.Empty).AmountOf((int)InstallmentNumber.Value) : Amount.Value;

    void RefreshInstallments()
    {
        if (IsSplit)
        {
            var plan = PlanFromFields(Guid.Empty);
            string first = plan.AmountOf(1) != plan.Regular ? $" (a 1ª fica em {Format.Money(plan.AmountOf(1))})" : "";
            string last = plan.Number <= plan.Count
                ? $" Esta é a {plan.Number}ª; a última cai em {Format.LongMonth(_month.Start.AddMonths(plan.Count - plan.Number)).ToLower(Format.PtBr)}."
                : "";
            InstallmentSummary.Text = $"{plan.Count}× de {Format.Money(plan.Regular)}{first} = {Format.Money(plan.Total)}.{last}";
        }
        RefreshOutcome();
    }

    // ───────────── effect on the budget ─────────────

    void RefreshOutcome()
    {
        if (Chosen is not { } category)
        {
            Outcome.Text = "ESCOLHA UMA CATEGORIA.";
            Outcome.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
            return;
        }
        var (budgeted, others) = Standing(category);
        decimal rest = budgeted - others - ThisMonthAmount;
        string name = category.Name.ToUpper(Format.PtBr);
        string what = IsSplit ? "DESTA PARCELA" : "DESTE LANÇAMENTO";
        bool alert = false;
        if (category.Kind == CategoryKind.Investment)
            Outcome.Text = rest > 0
                ? $"DEPOIS DESTE APORTE, FALTAM {Format.Money(rest)} PARA A META DE {name}."
                : $"COM ESTE APORTE, A META DE {name} É ATINGIDA.";
        else if (rest >= 0)
            Outcome.Text = $"DEPOIS {what}, SOBRAM {Format.Money(rest)} EM {name}.";
        else
        {
            Outcome.Text = $"■ {(IsSplit ? "ESTA PARCELA" : "ESTE LANÇAMENTO")} PASSA {Format.Money(-rest)} DO PLANEJADO EM {name}.";
            alert = true;
        }
        Outcome.SetResourceReference(TextBlock.ForegroundProperty, alert ? "Alert" : "Ink2");
    }

    // ───────────── suggestions ─────────────

    void ShowSuggestions()
    {
        Suggestions.Children.Clear();
        var term = Description.Text.Trim();
        if (_existing != null || term.Length < 2) return;
        foreach (var previous in _previous.Values
                     .Where(t => Format.Contains(t.Description, term) && !string.Equals(t.Description.Trim(), term, StringComparison.CurrentCultureIgnoreCase))
                     .OrderByDescending(t => t.Date).Take(4))
        {
            var button = new Button { Content = $"→ {previous.Description.ToUpper(Format.PtBr)} · {Format.Number(previous.Amount)}", Margin = new Thickness(0, 0, 18, 6) };
            button.SetResourceReference(StyleProperty, "TextButton");
            button.Click += (_, _) => Use(previous, onlyEmpty: false);
            Suggestions.Children.Add(button);
        }
    }

    /// <summary>
    /// When leaving the description for another field of this form, fills an empty category and amount from the last use.
    /// Does nothing if focus went to a suggestion (its click decides) or outside (e.g. SALVAR), so nothing the person didn't see gets saved.
    /// </summary>
    void CompleteOnLeave(DependencyObject? target)
    {
        if (_existing != null || target is null || !IsAncestorOf(target) || Suggestions.IsAncestorOf(target)) return;
        if (_previous.TryGetValue(Description.Text.Trim(), out var previous)) Use(previous, onlyEmpty: true);
    }

    /// <summary>Reuses an older transaction with the same description (as a single amount, not its installments), payment method included.</summary>
    void Use(Transaction previous, bool onlyEmpty)
    {
        if (!onlyEmpty) Description.Text = previous.Description;
        Description.CaretIndex = Description.Text.Length;
        if (!onlyEmpty || Chosen is null) Choose(previous.CategoryId);
        if (!onlyEmpty || Amount.Value == 0)
        {
            Amount.Value = previous.Amount;
            _installmentTypedLast = false;
            if (IsSplit) InstallmentAmount.Value = RegularInstallment();
        }
        if (!onlyEmpty) (previous.Recurring ? MonthlyOption : OnceOption).IsChecked = true;
        if (previous.Payment != null && (!onlyEmpty || ChosenPayment is null)) ChoosePayment(previous.Payment);
        Suggestions.Children.Clear();
        RefreshInstallments();
    }

    // ───────────── saving ─────────────

    public string? Problem()
    {
        if (Amount.Value <= 0) return "Informe um valor maior que zero.";
        if (string.IsNullOrWhiteSpace(Description.Text)) return "Escreva uma descrição.";
        if (Chosen is null) return "Escolha uma categoria.";
        if (IsSplit && InstallmentNumber.Value > InstallmentCount.Value) return "A parcela atual passa do número de parcelas.";
        return null;
    }

    public string? Warning() =>
        _existing?.Installment != null && !IsSplit ? "As próximas parcelas desta compra saem dos meses seguintes." : null;

    public void Apply()
    {
        var start = _month.Start;
        var transaction = _existing ?? new Transaction();
        var previousPlan = transaction.Installment;
        transaction.Description = Description.Text.Trim();
        transaction.CategoryId = Chosen!.Id;
        transaction.Date = new DateTime(start.Year, start.Month, (int)Math.Clamp(Day.Value, 1, Day.Maximum));
        transaction.Payment = ChosenPayment;
        if (IsSplit)
        {
            transaction.Installment = PlanFromFields(previousPlan?.PurchaseId ?? Guid.NewGuid());
            transaction.Amount = transaction.Installment.AmountOf(transaction.Installment.Number);
            transaction.Recurring = false;
        }
        else
        {
            transaction.Installment = null;
            transaction.Amount = Amount.Value;
            transaction.Recurring = MonthlyOption.IsChecked == true;
        }
        if (_existing is null) _month.Transactions.Add(transaction);

        // the following months follow along; this one no longer carries the purchase id, so only the later ones go
        if (transaction.Installment != null) _repo.SyncInstallments(_month, transaction);
        else if (previousPlan != null) _repo.RemoveInstallments(_month, previousPlan.PurchaseId);
    }
}
