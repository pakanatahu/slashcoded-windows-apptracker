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
builder.Services.AddSingleton<TrustedSourceCredentialStore>();
builder.Services.AddSingleton<TrustedUploadClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
