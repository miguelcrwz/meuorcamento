using System.Windows.Media;
using MeuOrcamento.Controls;

namespace MeuOrcamento;

// What each table row shows. They are rebuilt on every change (Refresh).

public record CategoryRow(Guid Id, string Number, string Name, Brush Color, string Kind, string Planned, string Posted,
                          string Remaining, bool Alert, double Usage, string UsageText);

public record TransactionRow(Transaction Source, string Date, string Description, string Category, Brush Color, string Payment,
                             string Repeats, string Amount);

public record HistoryRow(BudgetMonth Month, string Name, bool IsCurrent, string Income, string Expenses, string Invested,
                         string Free, bool FreeNegative, string Rate, IReadOnlyList<BarPart> Parts, double Free01, string Breakdown);
