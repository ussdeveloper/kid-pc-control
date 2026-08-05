using KidPcControl.Service;
using KidPcControl.Shared;

Directory.CreateDirectory(AppConstants.ProgramDataDir);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = AppConstants.ServiceName;
});

builder.Services.AddSingleton<PolicyRuntime>();
builder.Services.AddHostedService<KidMonitorWorker>();
builder.Services.AddHostedService<UpdateHostedService>();

var host = builder.Build();
host.Run();
