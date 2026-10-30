using System.Windows;
using System.Windows.Input;
using MeuOrcamento.Controls;
using static MeuOrcamento.Tests.Harness;

namespace MeuOrcamento.Tests;

/// <summary>The money/percentage field: how it reads what the person types.</summary>
static class NumberBoxTests
{
    public static void Run()
    {
        Section("NUMBER BOX");
        var box = new NumberBox();
        box.Text = "45,90"; Check("reads 45,90", box.Value == 45.90m, $"{box.Value}");
        box.Text = "1.234,56"; Check("reads 1.234,56", box.Value == 1234.56m, $"{box.Value}");
        box.Text = ""; Check("empty is 0", box.Value == 0, $"{box.Value}");
        box.Text = "abc"; Check("text that isn't a number doesn't become a value", box.Value == 0, $"{box.Value}");

        var capped = new NumberBox { Maximum = 100 };
        capped.Text = "150"; Check("respects the maximum", capped.Value == 100, $"{capped.Value}");

        var day = new NumberBox { Decimals = 0, Minimum = 1, Maximum = 31 };
        Check("field starts inside the minimum", day.Value == 1, $"{day.Value}");
        day.Text = "5"; day.Text = "0"; Check("day 0 becomes 1", day.Value == 1, $"{day.Value}");

        box.Value = 1500.5m; Check("shows 1.500,50", box.Text == "1.500,50", box.Text);
        box.Text = "45.90"; Check("\"45.90\" (decimal dot) = 45,90", box.Value == 45.90m, $"{box.Value}");
        box.Text = "5.000"; Check("\"5.000\" (thousands dot) = 5 thousand", box.Value == 5000m, $"{box.Value}");
        box.Text = "1.234.567"; Check("\"1.234.567\" = 1.2 million", box.Value == 1234567m, $"{box.Value}");
        box.Text = "300"; box.Text = "1,2,3"; Check("invalid text keeps the last value (doesn't reset)", box.Value == 300m, $"{box.Value}");
        box.Text = "12,345"; Check("rounds to the field's decimal places", box.Value == 12.35m, $"{box.Value}");

        Check("a field without decimals rejects the comma", Press(new NumberBox { Decimals = 0 }, ","));
        Check("a letter is rejected", Press(box, "x"));
        Check("a digit is accepted", !Press(box, "7"));
    }

    /// <summary>Simulates a keystroke and says whether the field rejected it (Handled = true).</summary>
    static bool Press(NumberBox box, string text)
    {
        var key = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, box, text))
        {
            RoutedEvent = UIElement.PreviewTextInputEvent,
        };
        box.RaiseEvent(key);
        return key.Handled;
    }
}
