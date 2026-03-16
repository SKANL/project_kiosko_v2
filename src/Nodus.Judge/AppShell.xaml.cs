using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Presentation.Views;

namespace Nodus.Judge;

public partial class AppShell : Shell
{
	private readonly IAppSettingsService _settings;

	public AppShell(IAppSettingsService settings)
	{
		_settings = settings;
		InitializeComponent();

		// OnboardingPage is NOT a tab — it is a modal wizard pushed over the tab bar.
		Routing.RegisterRoute(nameof(OnboardingPage), typeof(OnboardingPage));
		Routing.RegisterRoute(nameof(ProjectSelectionPage), typeof(ProjectSelectionPage));
		Routing.RegisterRoute(nameof(ProjectScannerPage), typeof(ProjectScannerPage));
		Routing.RegisterRoute(nameof(SyncPage), typeof(SyncPage));
		Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
		Routing.RegisterRoute(nameof(MyVotesPage), typeof(MyVotesPage));
	}

	protected override async void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		if (Handler is null) return;

		// If the judge has never completed setup, show the onboarding wizard as a modal.
		// VotingPage (first tab) will be visible underneath once onboarding completes.
		if (!_settings.IsOnboarded)
			await GoToAsync(nameof(OnboardingPage));
	}
}

