using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

[QueryProperty(nameof(SourceEventId), "sourceEventId")]
public partial class EventSetupPage : ContentPage
{
    private readonly EventSetupViewModel _vm;

    public EventSetupPage(EventSetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    private void SyncRubricChanged(object? sender, EventArgs e)
    {
        _vm.SyncRubricJsonCommand.Execute(null);
    }

    public int SourceEventId
    {
        set => _ = _vm.LoadExistingEventAsync(value);
    }
}
