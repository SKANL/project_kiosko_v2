using Microsoft.Extensions.DependencyInjection;
using Nodus.Admin.Application.Services;
using Nodus.Admin.Application.Interfaces.Services;

namespace Nodus.Admin;

public partial class App : global::Microsoft.Maui.Controls.Application
{
	public App(IServiceProvider services)
	{
		// Catch any unhandled exception and write to a log file so we can diagnose startup crashes
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			File.WriteAllText(
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nodus_crash.txt"),
				e.ExceptionObject?.ToString() ?? "unknown");
		};

		// InitializeComponent MUST run before resolving AppShell so that Application-level
		// resources (Colors.xaml / Styles.xaml) are loaded before AppShell.InitializeComponent()
		// tries to look up StaticResource keys such as NodusGroupedBackground.
		InitializeComponent();

		// Create SQLite tables before any page tries to query them.
		// Task.Run avoids deadlock: SQLiteAsyncConnection continuations must not
		// post back to the MAUI main-thread SynchronizationContext.
		var db = services.GetRequiredService<Infrastructure.Persistence.NodusDatabase>();
		Task.Run(() => db.InitializeAsync()).GetAwaiter().GetResult();

		// Start vote processing service to listen for incoming votes from judges
		var voteProcessor = services.GetRequiredService<VoteProcessingService>();
		voteProcessor.Start();

		// Start BLE server automatically so Judge devices can discover/connect immediately.
		var bleServer = services.GetRequiredService<IBleGattServerService>();
		var bleStart = Task.Run(() => bleServer.StartAsync()).GetAwaiter().GetResult();
		if (bleStart.IsFail)
			System.Diagnostics.Debug.WriteLine($"Admin BLE auto-start failed: {bleStart.Error}");

		// Start periodic backup (every 5 min, 3-file rotation)
		var backup = services.GetRequiredService<Application.Services.IBackupService>();
		backup.Start();

		var shell = services.GetRequiredService<AppShell>();
		MainPage = shell;
	}
}