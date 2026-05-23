using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Slashcoded.DesktopObserver;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
builder.Services.AddHttpClient();
var observerSection = builder.Configuration.GetSection("Observer");
builder.Services.Configure<ObserverOptions>(
    observerSection.Exists()
        ? observerSection
        : builder.Configuration.GetSection("Tracker"));
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IIdleMonitor, WindowsIdleMonitor>();
builder.Services.AddSingleton<IActiveWindowMonitor, ActiveWindowMonitor>();
builder.Services.AddSingleton<IHostTrackingConfigProvider, HostTrackingConfigProvider>();
builder.Services.AddSingleton<TrustedSourceCredentialStore>();
builder.Services.AddSingleton<TrustedUploadClient>();
builder.Services.AddSingleton<ITrustedUploadClient>(services => services.GetRequiredService<TrustedUploadClient>());
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
