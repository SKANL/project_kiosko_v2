using Microsoft.Extensions.Logging;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.Services;
using Nodus.Admin.Application.UseCases.Events;
using Nodus.Admin.Application.UseCases.Votes;
using Nodus.Admin.Infrastructure.Backup;
using Nodus.Admin.Infrastructure.Ble;
using Nodus.Admin.Infrastructure.Crypto;
using Nodus.Admin.Infrastructure.Export;
using Nodus.Admin.Infrastructure.Http;
using Nodus.Admin.Infrastructure.Persistence;
using Nodus.Admin.Infrastructure.Settings;
using Nodus.Admin.Presentation.ViewModels;
using Nodus.Admin.Presentation.Views;
using Shiny;

namespace Nodus.Admin;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        try
        {
            return BuildApp();
        }
        catch (Exception ex)
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nodus_startup_crash.txt");
            File.WriteAllText(log, ex.ToString());
            throw;
        }
    }

    private static MauiApp BuildApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseShiny()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                // SF-Pro fonts are Apple system fonts (iOS/macOS only) — no .otf files shipped
                // On Windows, MAUI uses Segoe UI by default
            });

        // ── Infrastructure ────────────────────────────────────────────────
        // BleGattServerService uses WinRT GattServiceProvider directly (Shiny.BluetoothLE.Hosting has no Windows runtime)

        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();

        builder.Services.AddSingleton<NodusDatabase>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            return new NodusDatabase(settings.DatabasePath);
        });

        builder.Services.AddSingleton<IEventRepository, EventRepository>();
        builder.Services.AddSingleton<IProjectRepository, ProjectRepository>();
        builder.Services.AddSingleton<IJudgeRepository, JudgeRepository>();
        builder.Services.AddSingleton<IVoteRepository, VoteRepository>();

        builder.Services.AddSingleton<ICryptoService, CryptoService>();
        builder.Services.AddSingleton<IBleGattServerService, BleGattServerService>();
        builder.Services.AddSingleton<IExcelExportService, ExcelExportService>();
        builder.Services.AddSingleton<VoteProcessingService>();
        builder.Services.AddSingleton<IBackupService, BackupService>();
        builder.Services.AddSingleton<ILocalHttpServerService, LocalHttpServerService>();

        // ── Use Cases ────────────────────────────────────────────────────
        builder.Services.AddTransient<BuildBootstrapPayloadUseCase>();
        builder.Services.AddTransient<ProcessVoteUseCase>();
        builder.Services.AddTransient<GetVoteSummaryUseCase>();
        builder.Services.AddTransient<CreateEventUseCase>();
        builder.Services.AddTransient<ActivateEventUseCase>();
        builder.Services.AddTransient<CloseEventUseCase>();

        // ── Presentation ─────────────────────────────────────────────────
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<EventSetupViewModel>();
        builder.Services.AddTransient<EventQrViewModel>();
        builder.Services.AddTransient<EventsListViewModel>();
        builder.Services.AddTransient<MonitorViewModel>();
        builder.Services.AddTransient<ResultsViewModel>();
        builder.Services.AddTransient<ManageProjectsViewModel>();
        builder.Services.AddTransient<ManageJudgesViewModel>();

        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<EventSetupPage>();
        builder.Services.AddTransient<EventQrPage>();
        builder.Services.AddTransient<EventsListPage>();
        builder.Services.AddTransient<MonitorPage>();
        builder.Services.AddTransient<ResultsPage>();
        builder.Services.AddTransient<ManageProjectsPage>();
        builder.Services.AddTransient<ManageJudgesPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
