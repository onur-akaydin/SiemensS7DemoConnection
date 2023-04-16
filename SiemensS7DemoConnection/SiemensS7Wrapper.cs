using S7.Net;
using S7.Net.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Input;

namespace SiemensS7DemoConnection;

public delegate Task AsyncEventHandler();
public delegate Task AsyncEventHandler<T>(T arg);

public class SiemensS7Wrapper
{
    #region PROPERTIES

    public bool IsConnected { get; protected set; }

    public CpuType CpuType { get; }

    public string IpAddress { get; set; }
    public int Rack { get; }
    public int Slot { get; }
    public bool MonitorConnectivity { get; set; }

    public bool MonitorVariables { get; set; }


    private int _monitorIntervalMs;
    /// <summary>
    /// Gets or sets the interval, in milliseconds, that the connectivity or variable read query will be made. Default is 500 ms
    /// </summary>
    public int MonitorIntervalMs
    {
        get => _monitorIntervalMs;
        set
        {
            if (_monitorIntervalMs != value)
            {
                _monitorIntervalMs = value;
                if (timer is not null) timer.Interval = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the count of retry attampts, while reading / writing or connecting to the device
    /// </summary>
    public int RetryCount { get; set; }


    private int _readTimeout;
    /// <summary>Gets or sets the amount of time that a read operation blocks waiting for data from PLC.</summary>
    /// <returns>A <see cref="T:System.Int32" /> that specifies the amount of time, in milliseconds, that will elapse before a read operation fails. The default value, <see cref="F:System.Threading.Timeout.Infinite" />, specifies that the read operation does not time out.</returns>
    public int ReadTimeout
    {
        get => _readTimeout;
        set
        {
            if (_readTimeout != value)
            {
                _readTimeout = value;
                if (plc is not null) plc.ReadTimeout = value;
            }
        }
    }

    private int _writeTimeout;
    /// <summary>Gets or sets the amount of time that a write operation blocks waiting for data to PLC. </summary>
    /// <returns>A <see cref="T:System.Int32" /> that specifies the amount of time, in milliseconds, that will elapse before a write operation fails. The default value, <see cref="F:System.Threading.Timeout.Infinite" />, specifies that the write operation does not time out.</returns>
    public int WriteTimeout
    {
        get => _writeTimeout;
        set
        {
            if (_writeTimeout != value)
            {
                _writeTimeout = value;
                if (plc is not null) plc.WriteTimeout = value;
            }
        }
    }

    public Dictionary<string, object> MonitoredVariables { get; }

    public bool ThrowExceptionOnError { get; set; }

    #endregion

    #region EVENTS

    public event AsyncEventHandler? Connected;
    public event AsyncEventHandler? Disconnected;
    public event AsyncEventHandler<ErrorCode>? PlcErrorOccured;

    #endregion

    public SiemensS7Wrapper(CpuType cpuType, string ipAddress, int rack = 0, int slot = 1)
    {
        MonitoredVariables = new Dictionary<string, object>();
        ReadTimeout = -1;
        WriteTimeout = -1;
        RetryCount = 3;
        MonitorIntervalMs = 500;
        CpuType = cpuType;
        IpAddress = ipAddress;
        Rack = rack;
        Slot = slot;

        // INIT TIMER
        timer = new System.Timers.Timer(MonitorIntervalMs);
        timer.Elapsed += timer_Elapsed;

        // INIT PLC
        plc = new Plc(CpuType, IpAddress, (short)Rack, (short)Slot);
        plc.ReadTimeout = ReadTimeout;
        plc.WriteTimeout = WriteTimeout;

    }

    #region PRIVATE FIELDS

    private System.Timers.Timer timer;
    private Plc plc;

    #endregion

    public async Task<bool> ConnectAsync()
    {
        try
        {
            plc.Close();
            bool connectionRequestSent = await SendConnectionRequestWithTimeoutAsync();
            if (connectionRequestSent)
            {
                IsConnected = plc.IsConnected;
                if (IsConnected)
                {
                    await FireEventIfNotNull(Connected);
                    timer.Start();
                }
                return IsConnected;
            }
            else
            {
                IsConnected = false;
                return false;
            }
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            IsConnected = false;
            if (ThrowExceptionOnError) throw;
            return false;
        }
    }

    public async Task<bool> DisconnectAsync()
    {
        try
        {
            timer.Stop();
            if (plc is not null)
            {
                plc.Close();
            }
            IsConnected = false;
            await FireEventIfNotNull(Disconnected);
            return true;
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            IsConnected = false;
            if (ThrowExceptionOnError) throw;
            return false;
        }
    }

    public async Task<T> ReadAsync<T>(string variable)
    {
        try
        {
            object result = await plc.ReadAsync(variable);
            if (result is null)
            {
                return default(T);
            }
            var convertedResult = (T)Convert.ChangeType(result, typeof(T));
            return convertedResult;
            //return (T)result;
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            if (ThrowExceptionOnError) throw;
            return default(T);
        }
    }

    public async Task ReadAllAsync()
    {
        try
        {
            var keys = MonitoredVariables.Keys.ToArray();
            var dataItems = keys.Select(DataItem.FromAddress).ToList();

            var results = await plc.ReadMultipleVarsAsync(dataItems);

            for (int i = 0; i < keys.Length; i++)
            {
                MonitoredVariables[keys[i]] = results[i];
            }
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            if (ThrowExceptionOnError) throw;
        }
    }

    public async Task<bool> WriteAsync<T>(string variable, T value)
    {
        try
        {
            await plc.WriteAsync(variable, value);
            return true;
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            if (ThrowExceptionOnError) throw;
            return false;
        }
    }

    public async Task<bool> WriteAsyncUnknownType(string variable, object value)
    {
        bool success = false;
        try
        {
            await plc.WriteAsync(variable, value);
            success = true;
        }
        catch { }

        try
        {
            await plc.WriteAsync(variable, Convert.ToInt16(value));
            success = true;
        }
        catch { }

        try
        {
            await plc.WriteAsync(variable, Convert.ToInt32(value));
            success = true;
        }
        catch { }
      
        try
        {
            await plc.WriteAsync(variable, Convert.ToUInt32(value));
            success = true;
        }
        catch { }

        try
        {
            await plc.WriteAsync(variable, Convert.ToUInt16(value));
            success = true;
        }
        catch { }

        try
        {
            await plc.WriteAsync(variable, Convert.ToDouble(value));
            success = true;
        }
        catch { }
        
        try
        {
            await plc.WriteAsync(variable, Convert.ToSingle(value));
            success = true;
        }
        catch { }

        try
        {
            await plc.WriteAsync(variable, value.ToString());
            success = true;
        }
        catch { }

        try
        {
            await plc.WriteAsync(variable, Convert.ToByte(value));
            success = true;
        }
        catch { }

        try
        {
            await plc.WriteAsync(variable, Convert.ToBoolean(value));
            success = true;
        }
        catch { }

        try
        {
            await plc.WriteAsync(variable, Convert.ToBoolean(Convert.ToInt32(value)));
            success = true;
        }
        catch { }

        return success;
    }

    private static async Task FireEventIfNotNull(AsyncEventHandler? eventToFire)
    {
        if (eventToFire != null)
            await Task.WhenAll(Array.ConvertAll(
              eventToFire.GetInvocationList(),
              e => ((AsyncEventHandler)e).Invoke()));
    }

    private static async Task FireEventIfNotNull<T>(AsyncEventHandler<T>? eventToFire, T arg)
    {
        if (eventToFire != null)
            await Task.WhenAll(Array.ConvertAll(
              eventToFire.GetInvocationList(),
              e => ((AsyncEventHandler<T>)e).Invoke(arg)));
    }

    private async Task<bool> SendConnectionRequestWithTimeoutAsync()
    {
        Task task = plc.OpenAsync(CancellationToken.None);
        if (await Task.WhenAny(task, Task.Delay(ReadTimeout)) == task)
        {
            return true;
        }

        return false;
    }

    private async void timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (MonitorConnectivity && IsConnected != plc.IsConnected)
        {
            if (IsConnected == true) await FireEventIfNotNull(Disconnected); // plc.IsConnected == false
            else await FireEventIfNotNull(Connected); // plc.IsConnected == true
        }
        if (MonitorVariables)
        {
            await ReadAllAsync();
        }
    }
}
