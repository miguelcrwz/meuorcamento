using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeuOrcamento.Controls;

/// <summary>Text box for money and percentages in Brazilian format ("1.234,56").</summary>
public class NumberBox : TextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(decimal), typeof(NumberBox), new PropertyMetadata(0m, (d, _) => ((NumberBox)d).OnValueChanged()));

    bool _typing, _writing;
    int _decimals = 2;

    /// <summary>Raised on every keystroke, when the value was changed by the person.</summary>
    public event EventHandler? ValueTyped;

    public NumberBox()
    {
        // inherits the TextBox look (the implicit style doesn't apply to subclasses)
        SetResourceReference(StyleProperty, typeof(TextBox));
        WriteText(FormatValue(0));
    }

    public decimal Value
    {
        get => (decimal)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    decimal _minimum, _maximum = 100_000_000m;

    /// <summary>When the limits change, the current value is brought inside them.</summary>
    public decimal Minimum
    {
        get => _minimum;
        set { _minimum = value; if (Value < value) Value = value; }
    }

    public decimal Maximum
    {
        get => _maximum;
        set { _maximum = value; if (Value > value) Value = value; }
    }

    public int Decimals
    {
        get => _decimals;
        set { _decimals = value; WriteText(FormatValue(Value)); }
    }

    string FormatValue(decimal value) => value.ToString("N" + _decimals, Format.PtBr);

    void WriteText(string text)
    {
        _writing = true;
        Text = text;
        _writing = false;
    }

    void OnValueChanged()
    {
        if (!_typing) WriteText(FormatValue(Value));
    }

    /// <summary>
    /// Reads what the person typed. With a comma, it's the Brazilian format ("1.234,56").
    /// Without a comma, a dot followed by 3 digits means thousands ("5.000"); by 1 or 2 digits, a decimal ("45.90").
    /// Returns false when the text isn't a number (the previous value then stays).
    /// </summary>
    public static bool TryParse(string text, out decimal value)
    {
        value = 0;
        text = text.Trim();
        if (text.Length == 0) return true;
        if (!text.Contains(',') && text.Contains('.'))
        {
            int dots = text.Count(c => c == '.');
            int afterLastDot = text.Length - text.LastIndexOf('.') - 1;
            text = dots == 1 && afterLastDot != 3 ? text.Replace('.', ',') : text.Replace(".", "");
        }
        return decimal.TryParse(text, NumberStyles.Number, Format.PtBr, out value);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        if (_writing || !TryParse(Text, out var value)) return;
        _typing = true;
        Value = Math.Round(Math.Clamp(value, Minimum, Maximum), _decimals, MidpointRounding.AwayFromZero);
        _typing = false;
        ValueTyped?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        // digits only; comma and dot only in fields with decimal places
        bool accepted = e.Text.All(c => char.IsDigit(c) || (_decimals > 0 && (c == ',' || c == '.')));
        if (!accepted) e.Handled = true;
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // the first click selects everything, so typing replaces it
        if (!IsKeyboardFocusWithin)
        {
            e.Handled = true;
            Focus();
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SelectAll();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        WriteText(FormatValue(Value));
    }
}
