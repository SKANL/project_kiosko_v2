namespace Nodus.Admin;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(Presentation.Views.EventSetupPage), typeof(Presentation.Views.EventSetupPage));
        Routing.RegisterRoute(nameof(Presentation.Views.EventQrPage), typeof(Presentation.Views.EventQrPage));
    }
}
