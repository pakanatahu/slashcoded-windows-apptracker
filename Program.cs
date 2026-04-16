using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Slashcoded.DesktopTracker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
builder.Services.AddHttpClient();
builder.Services.Configure<TrackerOptions>(builder.Configuration.GetSection("Tracker"));
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
