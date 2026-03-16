using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Nodus.Admin.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		// Register BEFORE InitializeComponent so WinUI XAML exceptions are captured
		// instead of causing a silent native crash (STATUS_STOWED_EXCEPTION 0xc000027b).
		this.UnhandledException += (_, e) =>
		{
			e.Handled = true; // prevent native crash — app stays alive long enough to log
			var msg = e.Exception?.ToString() ?? "unknown WinUI exception";
			// Write to multiple locations so at least one succeeds
			var paths = new[]
			{
				System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nodus_winui_crash.txt"),
				@"C:\nodus_winui_crash.txt",
				System.IO.Path.Combine(AppContext.BaseDirectory, "nodus_winui_crash.txt"),
			};
			foreach (var p in paths)
			{
				try { System.IO.File.WriteAllText(p, msg); break; } catch { }
			}
			System.Diagnostics.Debug.WriteLine("[NODUS CRASH] " + msg);
		};

		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

