using System.Windows;
using System.Windows.Controls;

namespace MeuOrcamento.Editors;

public partial class IncomeEditor : UserControl, IEditor
{
    readonly BudgetMonth _month;

    public IncomeEditor(BudgetMonth month, bool welcome)
    {
        _month = month;
        InitializeComponent();
        Overline = $"RENDA · {Format.ShortMonth(month.Start)}";
        Title = welcome ? "Quanto entra por mês?" : "Renda do mês";
        Welcome.Visibility = welcome ? Visibility.Visible : Visibility.Collapsed;
        Salary.Value = month.Salary;
        Extra.Value = month.ExtraIncome;
        Salary.ValueTyped += (_, _) => RefreshTotal();
        Extra.ValueTyped += (_, _) => RefreshTotal();
        RefreshTotal();
    }

    public string Overline { get; }
    public string Title { get; }
    public IInputElement? FirstField => Salary;

    void RefreshTotal() => Total.Text = Format.Money(Salary.Value + Extra.Value);

    public string? Problem() => null;

    public void Apply()
    {
        _month.Salary = Salary.Value;
        _month.ExtraIncome = Extra.Value;
    }
}
