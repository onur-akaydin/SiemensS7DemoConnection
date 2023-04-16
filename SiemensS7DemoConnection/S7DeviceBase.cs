using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SiemensS7DemoConnection;
public abstract class S7DeviceBase
{
    public string Name { get; protected set; }

    public bool IsConnected { get; protected set; }

    public string IpAddress { get; set; }

    public bool ThrowExceptionOnError { get; set; }

    public bool MonitorConnectivity { get; set; }

    public uint CollectionIntervalInMs { get; set; }

    public uint TotalStroke { get; set; }

    public uint MinSetPosition { get; set; }

    protected uint MicrophoneTubeStartDistance { get; set; }


    public async Task<bool> ConnectAsync()
    {
        return await Task.Run(() => Connect());
    }

    public async Task<bool> DisconnectAsync()
    {
        return await Task.Run(() => Disconnect());
    }

    public async Task<bool> PrepareAsync()
    {
        return await Task.Run(() => Prepare());
    }

    public async Task<bool> StartAsync()
    {
        return await Task.Run(() => Start());
    }

    public async Task<bool> StopAsync()
    {
        return await Task.Run(() => Stop());
    }

    public abstract bool Connect();

    public abstract bool Disconnect();

    public abstract bool Start();

    public abstract bool Stop();

    public abstract bool Prepare();

    public abstract bool MoveToAbsolutePosition(uint absolutePosition, uint speedInMmPerSecond, CancellationToken token);

    public abstract uint[] ReadRelativePositionVsTime(uint sampleLength);

    protected abstract uint readDevicePosition100();

    public virtual uint ReadAbsolutePosition()
    {
        return convertToAbsoulte(readDevicePosition100());
    }

    public uint ReadRelativePosition(uint sampleLength)
    {
        return convertToRelativePosition(ReadAbsolutePosition(), sampleLength);
    }

    protected uint convertToAbsoulte(uint device100position)
    {
        int num = (int)device100position / 100 - (int)MinSetPosition;
        if (num < 0)
        {
            return 0u;
        }

        checked
        {
            return unchecked(device100position / 100u) - MinSetPosition;
        }
    }

    protected uint convertToDevicePosition100(uint absolutePosition)
    {
        return checked((absolutePosition + MinSetPosition) * 100u);
    }

    protected uint convertToRelativePosition(uint absolutePosition, uint sampleLength)
    {
        return checked(MicrophoneTubeStartDistance - sampleLength + absolutePosition);
    }
}

