using Microsoft.Extensions.DependencyInjection;
using Nodus.Judge.Application.Services;

namespace Nodus.Judge;

public partial class App : global::Microsoft.Maui.Controls.Application
{
	public static bool IsInForeground { get; private set; } = true;

	public App(IServiceProvider services)
	{
		InitializeComponent();

		var db = services.GetRequiredService<Infrastructure.Persistence.NodusDatabase>();
		Task.Run(() => db.InitializeAsync()).GetAwaiter().GetResult();

		// Start BLE EventChanged listener — shows alert when Admin switches active event.
		services.GetRequiredService<EventChangeListenerService>().Start();
		services.GetRequiredService<RelayForwardingService>().Start();

		var shell = services.GetRequiredService<AppShell>();
		MainPage = shell;
	}

	protected override void OnSleep()
	{
		base.OnSleep();
		IsInForeground = false;
	}

	protected override void OnResume()
	{
		base.OnResume();
		IsInForeground = true;
	}
}