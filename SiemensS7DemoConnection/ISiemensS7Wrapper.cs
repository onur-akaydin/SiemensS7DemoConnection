using S7.Net;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SiemensS7DemoConnection
{
    public delegate Task AsyncEventHandler();
    public delegate Task AsyncEventHandler<T>(T arg);

    /// <summary>
    /// Common interface for Siemens S7 PLC communication wrappers
    /// </summary>
    public interface ISiemensS7Wrapper
    {
        bool IsConnected { get; }
        CpuType CpuType { get; }
        string IpAddress { get; set; }
        int Rack { get; }
        int Slot { get; }
        bool MonitorConnectivity { get; set; }
        bool MonitorVariables { get; set; }
        int MonitorIntervalMs { get; set; }
        int ReadTimeout { get; set; }
        int WriteTimeout { get; set; }
        bool ThrowExceptionOnError { get; set; }
        Dictionary<string, object> MonitoredVariables { get; }

        // Events
        event AsyncEventHandler Connected;
        event AsyncEventHandler Disconnected;
        event AsyncEventHandler<ErrorCode> PlcErrorOccured;

        // Connection methods
        Task<bool> ConnectAsync();
        Task<bool> DisconnectAsync();

        // Read methods
        Task<T> ReadAsync<T>(string variable);
        Task ReadAllAsync();

        // Write methods
        Task<bool> WriteAsync<T>(string variable, T value);
        Task<bool> WriteAsyncUnknownType(string variable, object value);
    }
}