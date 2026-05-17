using Microsoft.AspNetCore.Server.Kestrel.Core;
using UsbEthUsb.Server.Services;
using UsbEthUsb.Server.Usbip;

var builder = WebApplication.CreateBuilder(args);

var port = builder.Configuration.GetValue("Server:Port", 5557);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddSingleton<UsbipCli>();
builder.Services.AddHostedService<UsbipEnvironmentCheck>();

var app = builder.Build();
app.MapGrpcService<UsbDeviceServiceImpl>();
app.MapGet("/", () => $"UsbEthUsb gRPC server listening on port {port}. Use a gRPC client.");

app.Run();
