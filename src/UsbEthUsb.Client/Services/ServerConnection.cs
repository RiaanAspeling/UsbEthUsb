using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using UsbEthUsb.Client.Config;
using UsbEthUsb.Protocol;

namespace UsbEthUsb.Client.Services;

public class ServerConnection : IDisposable
{
    public ServerEntry Config { get; }
    private GrpcChannel _channel = null!;
    private UsbDeviceService.UsbDeviceServiceClient _client = null!;

    public string DisplayName => string.IsNullOrWhiteSpace(Config.Name)
        ? $"{Config.Host}:{Config.Port}"
        : $"{Config.Name} ({Config.Host}:{Config.Port})";

    static ServerConnection()
    {
        // Allow gRPC over plain HTTP/2 (no TLS) for v1. Must be set before any GrpcChannel is created.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public ServerConnection(ServerEntry config)
    {
        Config = config;
        InitChannel();
    }

    private void InitChannel()
    {
        _channel?.Dispose();
        var address = $"http://{Config.Host}:{Config.Port}";
        _channel = GrpcChannel.ForAddress(address);
        _client = new UsbDeviceService.UsbDeviceServiceClient(_channel);
    }

    public async Task<IReadOnlyList<Device>> ListDevicesAsync()
    {
        try
        {
            return await CallListAsync();
        }
        catch (RpcException)
        {
            // The first failure can leave the underlying socket pool in a bad state when the
            // server only comes online later. Recreate the channel and retry once.
            InitChannel();
            return await CallListAsync();
        }
    }

    private async Task<IReadOnlyList<Device>> CallListAsync()
    {
        var reply = await _client.ListDevicesAsync(new Empty(),
            deadline: DateTime.UtcNow.AddSeconds(3));
        return reply.Devices.ToList();
    }

    public async Task<(bool ok, string message)> BindAsync(string busId)
    {
        try
        {
            var reply = await _client.BindDeviceAsync(new BusIdRequest { BusId = busId },
                deadline: DateTime.UtcNow.AddSeconds(10));
            return (reply.Ok, reply.Message);
        }
        catch (RpcException)
        {
            InitChannel();
            var reply = await _client.BindDeviceAsync(new BusIdRequest { BusId = busId },
                deadline: DateTime.UtcNow.AddSeconds(10));
            return (reply.Ok, reply.Message);
        }
    }

    public async Task<(bool ok, string message)> UnbindAsync(string busId)
    {
        var reply = await _client.UnbindDeviceAsync(new BusIdRequest { BusId = busId },
            deadline: DateTime.UtcNow.AddSeconds(10));
        return (reply.Ok, reply.Message);
    }

    public void Dispose() => _channel.Dispose();
}
