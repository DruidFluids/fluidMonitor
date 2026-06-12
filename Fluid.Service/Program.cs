using Fluid.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(o => { o.ServiceName = "fluidsvc"; });
builder.Services.AddSingleton<ServiceConfig>();
builder.Services.AddSingleton<HardwareMonitor>();
builder.Services.AddSingleton<PipeServer>();
builder.Services.AddSingleton<TcpServer>();
builder.Services.AddSingleton<CmdServer>();
builder.Services.AddHostedService<SensorWorker>();

var host = builder.Build();
host.Run();
