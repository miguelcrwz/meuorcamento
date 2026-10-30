using System.Windows;
using System.Windows.Controls;

namespace MeuOrcamento.Editors;

/// <summary>Content of the side drawer. The window takes care of opening, validating, confirming and closing.</summary>
public interface IEditor
{
    /// <summary>Small line above the title, e.g. "LANÇAMENTO · OUT.2026".</summary>
    string Overline { get; }
    string Title { get; }
    string ConfirmText => "SALVAR";
    bool IsDestructive => false;
    double DrawerWidth => 500;

    /// <summary>What prevents saving (null = nothing).</summary>
    string? Problem();

    /// <summary>What asks for a second confirmation before saving (null = nothing).</summary>
    string? Warning() => null;

    /// <summary>Writes to the model. The caller saves the file.</summary>
    void Apply();

    IInputElement? FirstField => null;
}

/// <summary>A simple question, like "delete this transaction?".</summary>
public class ConfirmEditor : ContentControl, IEditor
{
    public ConfirmEditor(string overline, string title, string message, string button)
    {
        Overline = overline;
        Title = title;
        ConfirmText = button;
        var text = new TextBlock { Text = message };
        text.SetResourceReference(StyleProperty, "Note");
        Content = text;
    }

    public string Overline { get; }
    public string Title { get; }
    public string ConfirmText { get; }
    public bool IsDestructive => true;
    public double DrawerWidth => 440;
    public string? Problem() => null;
    public void Apply() { }
}
