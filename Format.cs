using System.Globalization;

namespace MeuOrcamento;

/// <summary>How numbers, months and dates show up on screen.</summary>
public static class Format
{
    public static readonly CultureInfo PtBr = new("pt-BR");
    static readonly string[] MonthAbbreviations = { "JAN", "FEV", "MAR", "ABR", "MAI", "JUN", "JUL", "AGO", "SET", "OUT", "NOV", "DEZ" };

    public static string Money(decimal value) => value.ToString("C2", PtBr);

    /// <summary>"1.440,10" — the "R$" is up to whoever displays it.</summary>
    public static string Number(decimal value) => value.ToString("N2", PtBr);

    /// <summary>Value with a typographic minus sign when negative.</summary>
    public static string SignedNumber(decimal value) => value < 0 ? "−" + Number(-value) : Number(value);

    public static string Percent(decimal percent) => percent.ToString("0.#", PtBr) + "%";

    /// <summary>"OUT.2026"</summary>
    public static string ShortMonth(DateTime month) => $"{MonthAbbreviations[month.Month - 1]}.{month.Year}";

    /// <summary>"Outubro de 2026"</summary>
    public static string LongMonth(DateTime month) => Capitalize(month.ToString("MMMM 'de' yyyy", PtBr));

    /// <summary>"22.10"</summary>
    public static string Day(DateTime date) => date.ToString("dd'.'MM", PtBr);

    /// <summary>"CRÉDITO", "DÉBITO", "PIX", or "" when not informed.</summary>
    public static string Payment(PaymentMethod? method) => method switch
    {
        PaymentMethod.Credit => "CRÉDITO",
        PaymentMethod.Debit => "DÉBITO",
        PaymentMethod.Pix => "PIX",
        _ => "",
    };

    public static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], PtBr) + text[1..];

    public static bool Contains(string text, string term) =>
        PtBr.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    /// <summary>Rounding differences (cents) don't count as "left over" or "went over".</summary>
    public static bool IsNegligible(decimal rest, decimal income) => Math.Abs(rest) < Math.Max(0.01m, income * 0.0001m);
}
