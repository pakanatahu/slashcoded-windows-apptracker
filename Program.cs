using Microsoft.Extensions.DependencyInjection;
using Slashcoded.DesktopTracker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient();
builder.Services.Configure<TrackerOptions>(builder.Configuration.GetSection("Tracker"));
builder.Services.AddSingleton<TrustedSourceCredentialStore>();
builder.Services.AddSingleton<TrustedUploadClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
