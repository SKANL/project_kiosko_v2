using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Nodus.Judge.Presentation.Views;

public partial class RawAiJsonPage : ContentPage
{
    private readonly string _content;
    private readonly string _copyMessage;

    public RawAiJsonPage(string rawJson)
        : this(rawJson, "Resultado AI (crudo)", "JSON copiado al portapapeles.")
    {
    }

    public RawAiJsonPage(string content, string title, string copyMessage)
    {
        InitializeComponent();
        _content = content ?? string.Empty;
        _copyMessage = string.IsNullOrWhiteSpace(copyMessage) ? "Contenido copiado al portapapeles." : copyMessage;

        Title = string.IsNullOrWhiteSpace(title) ? "Resultado AI" : title;
        HeaderLabel.Text = Title;
        RawEditor.Text = _content;
    }

    private async void OnCopyClicked(object? sender, System.EventArgs e)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(_content);
            await DisplayAlertAsync("Copiado", _copyMessage, "OK");
        }
        catch (System.Exception ex)
        {
            await DisplayAlertAsync("Error", $"No se pudo copiar: {ex.Message}", "OK");
        }
    }

    private async void OnCloseClicked(object? sender, System.EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
