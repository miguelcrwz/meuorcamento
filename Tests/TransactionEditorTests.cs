using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeuOrcamento.Controls;
using MeuOrcamento.Editors;
using static MeuOrcamento.Tests.Harness;

namespace MeuOrcamento.Tests;

/// <summary>The transaction drawer: validations, overspend warning, editing and suggestions.</summary>
static class TransactionEditorTests
{
    public static void Run()
    {
        Section("TRANSACTION");
        var repo = NewRepository("transaction.json");
        var month = repo.CreateMonth(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        month.Salary = 4000;

        var ed = new TransactionEditor(repo, month, null, null);
        Check("no amount: asks for an amount", ed.Problem()?.Contains("valor") == true, ed.Problem());
        Field<NumberBox>(ed, "Amount").Value = 120;
        Check("no description: asks for a description", ed.Problem()?.Contains("descrição") == true, ed.Problem());
        Field<TextBox>(ed, "Description").Text = "Mercado";
        Check("no category: asks for a category", ed.Problem()?.Contains("categoria") == true, ed.Problem());
        ((RadioButton)Field<StackPanel>(ed, "Categories").Children[1]).IsChecked = true;
        Check("everything filled in: saving is allowed", ed.Problem() == null, ed.Problem());
        Check("the default day is today", Field<NumberBox>(ed, "Day").Value == DateTime.Today.Day, $"{Field<NumberBox>(ed, "Day").Value}");
        ed.Apply();
        var t = month.Transactions.Single();
        Check("saving creates the right transaction",
            t.Amount == 120 && t.Description == "Mercado" && t.CategoryId == month.Allocations[1].CategoryId && t.Date == DateTime.Today);

        var ed2 = new TransactionEditor(repo, month, null, month.Allocations[1].CategoryId);
        Field<NumberBox>(ed2, "Amount").Text = "9.999,00";
        var outcome = Field<TextBlock>(ed2, "Outcome").Text;
        Check("warns when it's going to go over", outcome.Contains("PASSA"), outcome);

        var ed3 = new TransactionEditor(repo, month, t, null);
        Field<NumberBox>(ed3, "Amount").Value = 150;
        ed3.Apply();
        Check("editing doesn't duplicate", month.Transactions.Count == 1 && month.Transactions[0].Amount == 150);

        var ed4 = new TransactionEditor(repo, month, null, null);
        Field<TextBox>(ed4, "Description").Text = "Merc";
        Check("suggests a description already used", Field<WrapPanel>(ed4, "Suggestions").Children.Count >= 1, $"{Field<WrapPanel>(ed4, "Suggestions").Children.Count}");

        // leaving the description: fills in only when focus goes to another field of the same form
        var ed5 = new TransactionEditor(repo, month, null, null);
        ed5.Measure(new Size(500, 1200));
        ed5.Arrange(new Rect(0, 0, 500, 1200)); // builds the visual tree, as in the drawer
        Field<TextBox>(ed5, "Description").Text = "Mercado";
        Call(ed5, "CompleteOnLeave", new Button());
        Check("doesn't fill in when clicking outside (e.g. SALVAR)", Field<NumberBox>(ed5, "Amount").Value == 0, $"{Field<NumberBox>(ed5, "Amount").Value}");
        Call(ed5, "CompleteOnLeave", Field<NumberBox>(ed5, "Amount"));
        Check("fills in when moving to another field of the form", Field<NumberBox>(ed5, "Amount").Value == 150, $"{Field<NumberBox>(ed5, "Amount").Value}");

        Installments();
        Payment();
    }

    /// <summary>Crédito, débito or pix: optional, cleared by clicking again, reused by suggestions, carried to the next months.</summary>
    static void Payment()
    {
        Section("PAYMENT");
        var repo = NewRepository("payment.json");
        var oct = repo.CreateMonth(new DateTime(2026, 10, 1));
        oct.Salary = 5000;
        var nov = repo.CreateMonth(new DateTime(2026, 11, 1));

        var ed = new TransactionEditor(repo, oct, null, oct.Allocations[1].CategoryId);
        Field<NumberBox>(ed, "Amount").Value = 80;
        Field<TextBox>(ed, "Description").Text = "Farmácia";
        Check("the payment method is optional", ed.Problem() == null, ed.Problem());
        var pix = Field<RadioButton>(ed, "PixOption");
        pix.IsChecked = true;
        Check("once chosen, the help says how to clear it", Field<TextBlock>(ed, "PaymentHelp").Text.Contains("de novo"), Field<TextBlock>(ed, "PaymentHelp").Text);
        pix.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        Check("clicking the chosen method again clears it", pix.IsChecked == false);
        pix.IsChecked = true;
        ed.Apply();
        var pharmacy = oct.Transactions.Single(t => t.Description == "Farmácia");
        Check("saving keeps the method", pharmacy.Payment == PaymentMethod.Pix, $"{pharmacy.Payment}");
        Check("editing shows the saved method", Field<RadioButton>(new TransactionEditor(repo, oct, pharmacy, null), "PixOption").IsChecked == true);

        var again = new TransactionEditor(repo, oct, null, null);
        Call(again, "Use", pharmacy, false);
        Check("a suggestion brings its payment method too", Field<RadioButton>(again, "PixOption").IsChecked == true);

        var tv = new TransactionEditor(repo, oct, null, oct.Allocations[3].CategoryId);
        Field<NumberBox>(tv, "Amount").Text = "3.000,00";
        Field<TextBox>(tv, "Description").Text = "TV";
        Field<RadioButton>(tv, "SplitOption").IsChecked = true;
        Field<NumberBox>(tv, "InstallmentCount").Text = "10";
        Check("choosing PARCELADO picks CRÉDITO", Field<RadioButton>(tv, "CreditOption").IsChecked == true);
        tv.Apply();
        var tvNov = nov.Transactions.Single(t => t.Description == "TV");
        Check("the next months' installments keep the method", tvNov.Payment == PaymentMethod.Credit, $"{tvNov.Payment}");

        var debit = new TransactionEditor(repo, oct, null, oct.Allocations[3].CategoryId);
        Field<RadioButton>(debit, "DebitOption").IsChecked = true;
        Field<RadioButton>(debit, "SplitOption").IsChecked = true;
        Check("PARCELADO keeps a method already chosen",
            Field<RadioButton>(debit, "DebitOption").IsChecked == true && Field<RadioButton>(debit, "CreditOption").IsChecked == false);

        var window = OpenWindow(repo);
        window.GoTo(new DateTime(2026, 10, 1));
        List<TransactionRow> Rows() => ((IEnumerable)Field<ItemsControl>(window, "TransactionTable").ItemsSource).Cast<TransactionRow>().ToList();
        Check("the table shows the method",
            Rows().Single(r => r.Description == "Farmácia").Payment == "PIX" && Rows().Single(r => r.Description == "TV").Payment == "CRÉDITO");
        var totals = Field<TextBlock>(window, "PaymentTotals").Text;
        Check("the footer adds up each method", totals == "CRÉDITO 300,00   PIX 80,00", totals);
        Field<TextBox>(window, "Search").Text = "credito";
        Check("searching \"credito\" finds what went on the credit card", Rows().Select(r => r.Description).SequenceEqual(new[] { "TV" }),
            string.Join(", ", Rows().Select(r => r.Description)));
        window.Close();
    }

    /// <summary>A purchase split into installments: the split, the following months, editing and deleting.</summary>
    static void Installments()
    {
        Section("INSTALLMENTS");
        var repo = NewRepository("installments.json");
        var oct = repo.CreateMonth(new DateTime(2026, 10, 1));
        oct.Salary = 5000;
        var nov = repo.CreateMonth(new DateTime(2026, 11, 1)); // already planned when the purchase is made

        var ed = new TransactionEditor(repo, oct, null, oct.Allocations[3].CategoryId);
        Field<NumberBox>(ed, "Amount").Text = "1.000,00";
        Field<TextBox>(ed, "Description").Text = "Geladeira";
        Field<RadioButton>(ed, "SplitOption").IsChecked = true;
        Field<NumberBox>(ed, "InstallmentCount").Text = "3";
        Check("a total of 1.000,00 in 3: installments of 333,33", Field<NumberBox>(ed, "InstallmentAmount").Value == 333.33m,
            $"{Field<NumberBox>(ed, "InstallmentAmount").Value}");
        Check("a split purchase is ready to save", ed.Problem() == null, ed.Problem());
        ed.Apply();
        var first = oct.Transactions.Single(t => t.Description == "Geladeira");
        Check("this month gets installment 1 of 3, with the rounding cents", first.Amount == 333.34m && first.Installment is { Number: 1, Count: 3 },
            $"{first.Amount} {first.Installment?.Number}/{first.Installment?.Count}");
        var second = nov.Transactions.SingleOrDefault(t => t.Description == "Geladeira");
        Check("a month already planned gets installment 2", second is { Amount: 333.33m, Installment.Number: 2 }, $"{second?.Amount} {second?.Installment?.Number}");

        var dec = repo.CreateMonth(new DateTime(2026, 12, 1));
        var jan = repo.CreateMonth(new DateTime(2027, 1, 1));
        var third = dec.Transactions.SingleOrDefault(t => t.Description == "Geladeira");
        Check("planning the next month brings installment 3", third?.Installment?.Number == 3, $"{third?.Installment?.Number}");
        Check("the purchase stops after the last installment", jan.Transactions.All(t => t.Description != "Geladeira"));
        Check("the installments add up to the total", first.Amount + (second?.Amount ?? 0) + (third?.Amount ?? 0) == 1000m);

        // a purchase already under way: 12 of 551,75, and this is the 3rd
        var phone = new TransactionEditor(repo, nov, null, nov.Allocations[0].CategoryId);
        Field<TextBox>(phone, "Description").Text = "iPhone";
        Field<RadioButton>(phone, "SplitOption").IsChecked = true;
        Field<NumberBox>(phone, "InstallmentCount").Text = "12";
        Field<NumberBox>(phone, "InstallmentAmount").Text = "551,75";
        Field<NumberBox>(phone, "InstallmentNumber").Text = "3";
        Check("typing the installment fills in the total", Field<NumberBox>(phone, "Amount").Value == 6621m, $"{Field<NumberBox>(phone, "Amount").Value}");
        phone.Apply();
        var phoneJan = jan.Transactions.SingleOrDefault(t => t.Description == "iPhone");
        Check("the months after it get the next installments", phoneJan is { Amount: 551.75m, Installment.Number: 5 }, $"{phoneJan?.Amount} {phoneJan?.Installment?.Number}");
        Check("earlier months stay as they were", oct.Transactions.All(t => t.Description != "iPhone"));

        var phoneDec = dec.Transactions.Single(t => t.Description == "iPhone");
        var single = new TransactionEditor(repo, dec, phoneDec, null);
        Field<RadioButton>(single, "OnceOption").IsChecked = true;
        Check("making an installment a single amount warns about the next ones", single.Warning() != null, single.Warning());
        single.Apply();
        Check("and then removes the next ones", phoneDec.Installment == null && jan.Transactions.All(t => t.Description != "iPhone"));

        var window = OpenWindow(repo);
        window.GoTo(new DateTime(2026, 10, 1));
        var row = ((IEnumerable)Field<ItemsControl>(window, "TransactionTable").ItemsSource).Cast<TransactionRow>().Single(r => r.Description == "Geladeira");
        Check("the transactions table shows which installment it is", row.Repeats == "1 DE 3", row.Repeats);
        window.Close();

        repo.Save();
        var saved = File.ReadAllText(repo.FilePath);
        var reread = new Repository(repo.FilePath).FindMonth(new DateTime(2026, 10, 1))!.Transactions.Single(t => t.Description == "Geladeira");
        Check("the installment is saved and read back", reread.Installment == first.Installment && saved.Contains("\"Parcelamento\"") && saved.Contains("\"Parcelas\""));

        int removed = repo.RemoveInstallments(nov, first.Installment!.PurchaseId);
        Check("deleting an installment removes it and the next ones; earlier months keep theirs",
            removed == 2 && oct.Transactions.Any(t => t.Description == "Geladeira") && dec.Transactions.All(t => t.Description != "Geladeira"), $"{removed}");
    }
}
