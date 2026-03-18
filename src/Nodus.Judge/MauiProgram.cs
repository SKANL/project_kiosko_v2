using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Application.Services;
using Nodus.Judge.Application.UseCases.Onboarding;
using Nodus.Judge.Application.UseCases.Voting;
using Nodus.Judge.Infrastructure.Ble;
using Nodus.Judge.Infrastructure.Crypto;
using Nodus.Judge.Infrastructure.Persistence;
using Nodus.Judge.Infrastructure.Settings;
using Nodus.Judge.Presentation.ViewModels;
using Nodus.Judge.Presentation.Views;
using Shiny;
using ZXing.Net.Maui.Controls;

namespace Nodus.Judge;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseShiny()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                // SF-Pro fonts are Apple system fonts — available natively on iOS/macOS
            });

        // ── Infrastructure ────────────────────────────────────────────────
        builder.Services.AddBluetoothLE();
        builder.Services.AddBluetoothLeHosting();

        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        builder.Services.AddSingleton<IProjectScanBuffer, ProjectScanBuffer>();

        builder.Services.AddSingleton<NodusDatabase>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            return new NodusDatabase(settings.DatabasePath);
        });

        builder.Services.AddSingleton<ILocalEventRepository, LocalEventRepository>();
        builder.Services.AddSingleton<ILocalProjectRepository, LocalProjectRepository>();
        builder.Services.AddSingleton<ILocalJudgeRepository, LocalJudgeRepository>();
        builder.Services.AddSingleton<ILocalVoteRepository, LocalVoteRepository>();

        builder.Services.AddSingleton<ICryptoService, CryptoService>();

        // BleGattClientService is needed both as a concrete type (by BleSwarmService)
        // and via its interface (by use cases). Register concrete first, then alias.
        builder.Services.AddSingleton<BleGattClientService>();
        builder.Services.AddSingleton<IBleGattClientService>(
            sp => sp.GetRequiredService<BleGattClientService>());

        builder.Services.AddSingleton<IBleGattServerService, BleGattServerService>();
        builder.Services.AddSingleton<IBleSwarmService, BleSwarmService>();
        builder.Services.AddSingleton<PinValidationService>();
        builder.Services.AddSingleton<EventChangeListenerService>();
        builder.Services.AddSingleton<RelayForwardingService>();

        // ── Use Cases ────────────────────────────────────────────────────
        builder.Services.AddSingleton<SyncFromAdminUseCase>();
        builder.Services.AddTransient<SubmitVoteUseCase>();

        // ── Presentation ─────────────────────────────────────────────────
        builder.Services.AddTransient<OnboardingViewModel>();
        builder.Services.AddTransient<VotingViewModel>();
        builder.Services.AddTransient<SyncViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<MyVotesViewModel>();
        builder.Services.AddTransient<ProjectDetailsViewModel>();

        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<VotingPage>();
        builder.Services.AddTransient<ProjectSelectionPage>();
        builder.Services.AddTransient<ProjectDetailsPage>();
        builder.Services.AddTransient<ProjectScannerPage>();
        builder.Services.AddTransient<SyncPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<MyVotesPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
