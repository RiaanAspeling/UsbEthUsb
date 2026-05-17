using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using UsbEthUsb.Protocol;
using UsbEthUsb.Server.Usbip;

namespace UsbEthUsb.Server.Services;

public class UsbDeviceServiceImpl : UsbDeviceService.UsbDeviceServiceBase
{
    private readonly UsbipCli _usbip;
    private readonly ILogger<UsbDeviceServiceImpl> _logger;

    public UsbDeviceServiceImpl(UsbipCli usbip, ILogger<UsbDeviceServiceImpl> logger)
    {
        _usbip = usbip;
        _logger = logger;
    }

    public override async Task<DeviceList> ListDevices(Empty request, ServerCallContext context)
    {
        var devices = await _usbip.ListLocalDevicesAsync(context.CancellationToken);
        var reply = new DeviceList { CapturedAt = Timestamp.FromDateTime(DateTime.UtcNow) };
        reply.Devices.AddRange(devices);
        return reply;
    }

    public override async Task<BindResponse> BindDevice(BusIdRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Bind requested for {BusId}", request.BusId);
        var (ok, msg) = await _usbip.BindAsync(request.BusId, context.CancellationToken);
        return new BindResponse { Ok = ok, Message = msg };
    }

    public override async Task<UnbindResponse> UnbindDevice(BusIdRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Unbind requested for {BusId}", request.BusId);
        var (ok, msg) = await _usbip.UnbindAsync(request.BusId, context.CancellationToken);
        return new UnbindResponse { Ok = ok, Message = msg };
    }

    public override async Task StreamEvents(Empty request, IServerStreamWriter<DeviceEvent> responseStream, ServerCallContext context)
    {
        // v1 stub: holds the stream open as a keepalive. Real hotplug wiring lands in a later iteration
        // (likely via libudev / netlink uevents or polling `usbip list -l`).
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), context.CancellationToken);
        }
    }
}
