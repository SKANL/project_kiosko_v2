using Microsoft.Extensions.DependencyInjection;
using Nodus.Admin.Application.Services;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Infrastructure.Http;

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

		try
		{
			// 1. Initialize SQLite (Synchronous block is risky but often necessary for first page)
			// Using Task.Run to ensure SQLite extensions don't capture the UI SynchronizationContext.
			var db = services.GetRequiredService<Infrastructure.Persistence.NodusDatabase>();
			Task.Run(() => db.InitializeAsync()).Wait();

			// 2. Start Services (Background - do not block UI thread)
			_ = Task.Run(async () =>
			{
				try
				{
					// BLE server
					var bleServer = services.GetRequiredService<IBleGattServerService>();
					await bleServer.StartAsync().ConfigureAwait(false);

					// HTTP server
					var httpServer = services.GetRequiredService<ILocalHttpServerService>();
					await httpServer.StartAsync().ConfigureAwait(false);

					// Backup service
					var backup = services.GetRequiredService<Application.Services.IBackupService>();
					backup.Start();

					// Vote processing
					var voteProcessor = services.GetRequiredService<VoteProcessingService>();
					voteProcessor.Start();
				}
				catch (Exception backgroundEx)
				{
					System.Diagnostics.Debug.WriteLine($"Admin background services failure: {backgroundEx.Message}");
				}
			});
		}
		catch (Exception startupEx)
		{
			// Critical startup failure
			System.Diagnostics.Debug.WriteLine($"Admin critical startup failure: {startupEx.Message}");
			File.WriteAllText(
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nodus_critical_error.txt"),
				startupEx.ToString());
		}

		_shell = services.GetRequiredService<AppShell>();
	}

	private readonly AppShell _shell;

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_shell);
	}
}