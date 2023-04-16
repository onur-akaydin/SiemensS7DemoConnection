using S7.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace SiemensS7DemoConnection;
public class S7Device : S7DeviceBase
{
    private System.Timers.Timer _timer;

    private Plc plc;

    private uint _currentAbsolutePosition = 0u;

    private List<uint> _absolutePositionBuffer;

    private bool _isInAutomaticMovingMode;

    private uint _lastRead = 0u;

    private SynchronizationContext _syncContext;

    public S7Device(ScannerConnectionArgs connectionArgs)
        : base(connectionArgs)
    {
        construct();
    }

    public S7Device(ScannerConnectionArgs connectionArgs, IEventAggregator eventAggregator)
        : base(connectionArgs, eventAggregator)
    {
        construct();
    }

    private void construct()
    {
        base.Name = "Tarayıcı S7-1200";
        _absolutePositionBuffer = new List<uint>();
        _timer = new System.Timers.Timer(base.CollectionIntervalInMs);
        _timer.Elapsed += _timer_Elapsed;
        _syncContext = new SynchronizationContext();
    }

    public override bool Connect()
    {
        //IL_001a: Unknown result type (might be due to invalid IL or missing references)
        //IL_0024: Expected O, but got Unknown
        if (plc == null)
        {
            plc = new Plc((CpuType)30, base.ipAddress, (short)0, (short)1);
            plc.set_ReadTimeout(100);
            plc.set_WriteTimeout(100);
        }

        _syncContext.Send(delegate
        {
            TryCloseAndOpenConnection();
        }, null);
        if (plc.get_IsConnected())
        {
            _syncContext.Send(delegate
            {
                AlarmReset();
            }, null);
            _syncContext.Send(delegate
            {
                ReadAbsolutePosition();
            }, null);
            base.IsConnected = true;
            _ea.Publish(new DeviceStatusChanged(this, "Connected"));
            return true;
        }

        return false;
    }

    public override bool Disconnect()
    {
        base.IsConnected = false;
        _ea.Publish(new DeviceStatusChanged(this, "Disconnected"));
        return true;
    }

    public override bool Prepare()
    {
        Debug.WriteLine("DemoScanner prepared.");
        return true;
    }

    public override bool Start()
    {
        _absolutePositionBuffer.Clear();
        _timer.Start();
        return true;
    }

    public override bool Stop()
    {
        _timer.Stop();
        _syncContext.Send(delegate
        {
            OtoPosuDurdur();
        }, null);
        return true;
    }

    public override uint ReadAbsolutePosition()
    {
        if (!_isInAutomaticMovingMode)
        {
            _currentAbsolutePosition = convertToAbsoulte(readDevicePosition100());
        }

        return _currentAbsolutePosition;
    }

    public override uint[] ReadRelativePositionVsTime(uint sampleLength)
    {
        return _absolutePositionBuffer.Select((uint x) => convertToRelativePosition(x, sampleLength)).ToArray();
    }

    private void _timer_Elapsed(object sender, ElapsedEventArgs e)
    {
        _absolutePositionBuffer.Add(_currentAbsolutePosition);
    }

    public override bool MoveToAbsolutePosition(uint absolutePosition, uint speedInMmPerSecond, CancellationToken token)
    {
        uint devicePosition = convertToDevicePosition100(absolutePosition);
        uint deviceSpeed = speedInMmPerSecond * 60;
        _syncContext.Send(delegate
        {
            AutomaticMoveToPosition(devicePosition, deviceSpeed, 3u);
        }, null);
        while (_currentAbsolutePosition != absolutePosition)
        {
            if (token.IsCancellationRequested)
            {
                _syncContext.Send(delegate
                {
                    OtoPosuDurdur();
                }, null);
                token.ThrowIfCancellationRequested();
            }

            _currentAbsolutePosition = ReadAbsolutePosition();
            Thread.Sleep(50);
        }

        return true;
    }

    protected override uint readDevicePosition100()
    {
        try
        {
            _syncContext.Send(delegate
            {
                _lastRead = (uint)plc.Read("DB1.DBD52");
            }, null);
            return _lastRead;
        }
        catch (Exception)
        {
            return _lastRead;
        }
    }

    private void TryCloseAndOpenConnection()
    {
        try
        {
            plc.Close();
            ConnectWithTimeout();
        }
        catch
        {
        }
    }

    private void TryOpenIfNotConnected()
    {
        try
        {
            if (!plc.get_IsConnected())
            {
                plc.Open();
            }
        }
        catch
        {
        }
    }

    private bool ConnectWithTimeout()
    {
        Task task = plc.OpenAsync(default(CancellationToken));
        if (Task.WhenAny(task, Task.Delay(2000)).Result == task)
        {
            return true;
        }

        return false;
    }

    protected uint readDeviceSpeed60()
    {
        return (uint)plc.Read("DB1.DBD62");
    }

    protected void AlarmReset()
    {
        try
        {
            plc.Write("DB15.DBX0.0", (object)1);
        }
        catch (Exception)
        {
        }
    }

    protected void AutomaticMoveToPosition(uint devicePosition, uint deviceSpeed = 5000u, uint commandCount = 1u)
    {
        try
        {
            _isInAutomaticMovingMode = true;
            if (devicePosition > (base.totalStroke + base.minSetPosition) * 100 || devicePosition < 0)
            {
                throw new ArgumentOutOfRangeException("Tarayıcıya gönderilecek pozisyon set değerleri, geçerli aralık dışında.");
            }

            _syncContext.Send(delegate
            {
                TryOpenIfNotConnected();
            }, null);
            for (uint num = 0u; num < commandCount; num++)
            {
                plc.Write("DB1.DBD62", (object)deviceSpeed);
                plc.Write("DB1.DBD76", (object)devicePosition);
                plc.Write("DB15.DBX0.3", (object)1);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isInAutomaticMovingMode = false;
        }
    }

    public void OtoPosuDurdur()
    {
        TryOpenIfNotConnected();
        try
        {
            plc.Write("DB15.DBX0.3", (object)0);
        }
        catch
        {
        }
    }

    public bool HataVarMi()
    {
        return (bool)plc.Read("DB15.DBX0.4");
    }

    public bool UyariVarMi()
    {
        return (bool)plc.Read("DB15.DBX0.5");
    }

    public void JogHiziSet(uint speed)
    {
        plc.Write("DB1.DBD66", (object)speed);
    }

    public void JogIleri(bool isActive)
    {
        plc.Write("DB15.DBX0.1", (object)isActive);
    }

    public void JogGeri(bool isActive)
    {
        plc.Write("DB15.DBX0.2", (object)isActive);
    }
}

