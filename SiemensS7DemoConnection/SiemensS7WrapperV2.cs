using S7.Net;
using S7.Net.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
// Use alias to avoid ambiguity between System.DateTime and S7.Net.Types.DateTime
using SDateTime = System.DateTime;

namespace SiemensS7DemoConnection;

/// <summary>
/// Version 2 of Siemens S7 wrapper - improved implementation with type-specific handling
/// SiemensS7WrapperV2 (Improved Implementation)
/// Adds specialized handling for each problematic data type
/// Uses specific VarType for Real, Word, and custom DTL handling
/// Implements dedicated methods for each data type
/// Properly handles byte order for floating point and integer values
/// Implements custom DTL format handling for S7-1200/1500 PLCs
/// </summary>
public class SiemensS7WrapperV2 : ISiemensS7Wrapper
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

    public SiemensS7WrapperV2(CpuType cpuType, string ipAddress, int rack = 0, int slot = 1)
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

    #region CONNECTION METHODS

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

    #endregion

    #region READ METHODS

    public async Task<T> ReadAsync<T>(string variable)
    {
        try
        {
            if (variable.Contains("String", StringComparison.OrdinalIgnoreCase))
            {
                // Handle S7 String type
                return await ReadStringAsync<T>(variable);
            }
            else if (variable.Contains("DateTime", StringComparison.OrdinalIgnoreCase))
            {
                // Handle DateTime type
                return await ReadDtlAsync<T>(variable);
            }
            else if (typeof(T) == typeof(double) || typeof(T) == typeof(float))
            {
                // Special handling for floating point values
                return await ReadRealAsync<T>(variable);
            }
            else if (variable.Contains("DBW", StringComparison.OrdinalIgnoreCase))
            {
                // Special handling for Word
                return await ReadWordAsync<T>(variable);
            }

            object result = await plc.ReadAsync(variable);
            if (result is null)
            {
                return default(T);
            }
            var convertedResult = (T)Convert.ChangeType(result, typeof(T));
            return convertedResult;
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            // Always propagate the exception
            throw;
        }
    }

    private async Task<T> ReadRealAsync<T>(string variable)
    {
        try
        {
            var address = ParseAddress(variable);
            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = address.DbNumber,
                StartByteAdr = address.StartByte,
                VarType = VarType.Real,
                Count = 1,
                Value = new object()
            };

            var items = new List<DataItem> { dataItem };
            var updatedItems = await plc.ReadMultipleVarsAsync(items);

            // Now handle the Real value properly
            if (updatedItems[0].Value is float floatValue)
            {
                if (typeof(T) == typeof(double))
                    return (T)(object)(double)floatValue;
                else if (typeof(T) == typeof(float))
                    return (T)(object)floatValue;
                else
                    return (T)Convert.ChangeType(floatValue, typeof(T));
            }
            return default(T);
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            throw;
        }
    }

    private async Task<T> ReadWordAsync<T>(string variable)
    {
        try
        {
            var address = ParseAddress(variable);
            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = address.DbNumber,
                StartByteAdr = address.StartByte,
                VarType = VarType.Word,
                Count = 1,
                Value = new object()
            };

            var items = new List<DataItem> { dataItem };
            var updatedItems = await plc.ReadMultipleVarsAsync(items);

            if (updatedItems[0].Value is ushort wordValue)
            {
                if (typeof(T) == typeof(ushort) || typeof(T) == typeof(int) || typeof(T) == typeof(uint))
                    return (T)Convert.ChangeType(wordValue, typeof(T));
                else
                    return (T)Convert.ChangeType(wordValue, typeof(T));
            }
            return default(T);
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            throw;
        }
    }

    private async Task<T> ReadStringAsync<T>(string variable)
    {
        try
        {
            var address = ParseAddress(variable);
            // Set max length to 200 as requested
            int maxLength = 200;

            // Extract string length using regex for more reliable parsing
            if (variable.Contains("String", StringComparison.OrdinalIgnoreCase))
            {
                int startIndex = variable.IndexOf("String", StringComparison.OrdinalIgnoreCase) + 6;
                string remainingPart = variable.Substring(startIndex);

                // Extract digits that might follow "String"
                string digits = "";
                foreach (char c in remainingPart)
                {
                    if (char.IsDigit(c))
                        digits += c;
                    else
                        break;
                }

                if (!string.IsNullOrEmpty(digits) && int.TryParse(digits, out int parsedLength))
                {
                    maxLength = parsedLength;
                }
            }

            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = address.DbNumber,
                StartByteAdr = address.StartByte,
                VarType = VarType.S7String,
                Count = maxLength,
                Value = new object()
            };

            var items = new List<DataItem> { dataItem };
            // Use the returned list from ReadMultipleVarsAsync
            var updatedItems = await plc.ReadMultipleVarsAsync(items);

            if (typeof(T) == typeof(string) || typeof(T) == typeof(object))
            {
                return (T)updatedItems[0].Value;
            }
            else
            {
                throw new InvalidOperationException("Cannot convert S7 string to requested type");
            }
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            // Always throw the exception to properly notify the caller
            throw;
        }
    }

    private async Task<T> ReadDtlAsync<T>(string variable)
    {
        try
        {
            var address = ParseAddress(variable);

            // For DTL format, we need to read raw bytes
            // DTL structure: 12 bytes total
            // Byte  0-1: Year (uint16)
            // Byte  2:   Month (uint8)
            // Byte  3:   Day (uint8)
            // Byte  4:   Weekday (uint8) - ignored
            // Byte  5:   Hour (uint8)
            // Byte  6:   Minute (uint8)
            // Byte  7:   Second (uint8)
            // Byte  8-11: Nanoseconds (uint32)

            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = address.DbNumber,
                StartByteAdr = address.StartByte,
                VarType = VarType.Byte,
                Count = 12, // DTL is 12 bytes
                Value = new object()
            };

            var items = new List<DataItem> { dataItem };
            var updatedItems = await plc.ReadMultipleVarsAsync(items);

            if (updatedItems[0].Value is byte[] bytes && bytes.Length >= 12)
            {
                // Extract DTL components
                ushort year = (ushort)(bytes[0] | (bytes[1] << 8));
                byte month = bytes[2];
                byte day = bytes[3];
                byte hour = bytes[5];
                byte minute = bytes[6];
                byte second = bytes[7];
                // We'll ignore nanoseconds for simplicity

                // Validate date components
                if (month < 1 || month > 12 || day < 1 || day > 31 ||
                    hour > 23 || minute > 59 || second > 59)
                {
                    throw new ArgumentOutOfRangeException("Invalid date/time component in DTL value");
                }

                var dateTime = new SDateTime(year, month, day, hour, minute, second);

                if (typeof(T) == typeof(SDateTime) || typeof(T) == typeof(object))
                {
                    return (T)(object)dateTime;
                }
                else
                {
                    return (T)Convert.ChangeType(dateTime, typeof(T));
                }
            }
            throw new InvalidOperationException("Failed to read DTL value");
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            throw;
        }
    }

    private class AddressInfo
    {
        public int DbNumber { get; set; }
        public int StartByte { get; set; }
        public int BitPosition { get; set; } = -1;
    }

    private AddressInfo ParseAddress(string variable)
    {
        var info = new AddressInfo();

        // Parse DB number
        if (variable.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
        {
            int dbEndPos = variable.IndexOf('.');
            if (dbEndPos > 0)
            {
                string dbNumStr = variable.Substring(2, dbEndPos - 2);
                if (int.TryParse(dbNumStr, out int dbNum))
                {
                    info.DbNumber = dbNum;
                }
            }
        }

        // Parse byte address and bit position if specified
        string afterDb = variable.Contains('.') ? variable.Substring(variable.IndexOf('.') + 1) : string.Empty;

        // Check for bit address (DBX format)
        if (afterDb.StartsWith("DBX", StringComparison.OrdinalIgnoreCase))
        {
            string addrPart = afterDb.Substring(3);
            string[] parts = addrPart.Split('.');

            if (parts.Length >= 2 && int.TryParse(parts[0], out int byteAddr) && int.TryParse(parts[1], out int bitPos))
            {
                info.StartByte = byteAddr;
                info.BitPosition = bitPos;
            }
        }
        // Check for byte, word, or double word address
        else if (afterDb.StartsWith("DBB", StringComparison.OrdinalIgnoreCase) ||
                 afterDb.StartsWith("DBW", StringComparison.OrdinalIgnoreCase) ||
                 afterDb.StartsWith("DBD", StringComparison.OrdinalIgnoreCase))
        {
            string addrPart = afterDb.Substring(3);
            int dotPos = addrPart.IndexOf('.');
            string byteAddrStr = dotPos > 0 ? addrPart.Substring(0, dotPos) : addrPart;

            if (int.TryParse(byteAddrStr, out int byteAddr))
            {
                info.StartByte = byteAddr;
            }
        }

        return info;
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

    #endregion

    #region WRITE METHODS

    public async Task<bool> WriteAsync<T>(string variable, T value)
    {
        try
        {
            if (variable.Contains("String", StringComparison.OrdinalIgnoreCase))
            {
                return await WriteStringAsync(variable, value?.ToString() ?? string.Empty);
            }
            else if (variable.Contains("DateTime", StringComparison.OrdinalIgnoreCase) && value is SDateTime)
            {
                return await WriteDtlAsync(variable, (SDateTime)(object)value);
            }
            else if (value is double || value is float)
            {
                // Special handling for floating point values
                return await WriteRealAsync(variable, value);
            }
            else if (variable.Contains("DBW", StringComparison.OrdinalIgnoreCase))
            {
                // Special handling for Word
                return await WriteWordAsync(variable, value);
            }

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

    private async Task<bool> WriteRealAsync<T>(string variable, T value)
    {
        try
        {
            var address = ParseAddress(variable);
            float floatValue;

            if (value is double doubleValue)
                floatValue = (float)doubleValue;
            else if (value is float directFloatValue)
                floatValue = directFloatValue;
            else
                floatValue = Convert.ToSingle(value);

            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = address.DbNumber,
                StartByteAdr = address.StartByte,
                VarType = VarType.Real,
                Count = 1,
                Value = floatValue
            };

            await plc.WriteAsync(new[] { dataItem });
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

    private async Task<bool> WriteWordAsync<T>(string variable, T value)
    {
        try
        {
            var address = ParseAddress(variable);
            ushort wordValue;

            if (value is int intValue)
                wordValue = (ushort)intValue;
            else if (value is ushort directWordValue)
                wordValue = directWordValue;
            else if (value is string stringValue && ushort.TryParse(stringValue, out ushort parsedValue))
                wordValue = parsedValue;
            else
                wordValue = Convert.ToUInt16(value);

            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = address.DbNumber,
                StartByteAdr = address.StartByte,
                VarType = VarType.Word,
                Count = 1,
                Value = wordValue
            };

            await plc.WriteAsync(new[] { dataItem });
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

    private async Task<bool> WriteStringAsync(string variable, string value)
    {
        try
        {
            if (!IsConnected)
                throw new PlcException(ErrorCode.ConnectionError, "PLC is not connected");

            var address = ParseAddress(variable);
            // Set max length to 200 as requested
            int maxLength = 200;

            // Extract string length using better parsing method
            if (variable.Contains("String", StringComparison.OrdinalIgnoreCase))
            {
                int startIndex = variable.IndexOf("String", StringComparison.OrdinalIgnoreCase) + 6;
                if (startIndex < variable.Length)
                {
                    string remainingPart = variable.Substring(startIndex);

                    // Extract digits that might follow "String"
                    string digits = "";
                    foreach (char c in remainingPart)
                    {
                        if (char.IsDigit(c))
                            digits += c;
                        else
                            break;
                    }

                    if (!string.IsNullOrEmpty(digits) && int.TryParse(digits, out int parsedLength))
                    {
                        maxLength = parsedLength;
                    }
                }
            }

            // Truncate the string if it's too long for the specified maxLength
            if (value.Length > maxLength)
            {
                value = value.Substring(0, maxLength);
            }

            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = address.DbNumber,
                StartByteAdr = address.StartByte,
                VarType = VarType.S7String,
                Count = maxLength,
                Value = value
            };

            await plc.WriteAsync(new[] { dataItem });
            return true;
        }
        catch (Exception e)
        {
            if (e is PlcException pe)
            {
                await FireEventIfNotNull(PlcErrorOccured, pe.ErrorCode);
            }
            // Always throw the exception to properly notify the caller
            throw;
        }
    }

    private async Task<bool> WriteDtlAsync(string variable, SDateTime value)
    {
        try
        {
            var address = ParseAddress(variable);

            // Create DTL bytes structure (12 bytes)
            byte[] dtlBytes = new byte[12];

            // Convert DateTime to DTL format
            // Year (2 bytes, little-endian)
            dtlBytes[0] = (byte)(value.Year & 0xFF);
            dtlBytes[1] = (byte)((value.Year >> 8) & 0xFF);

            // Month (1 byte)
            dtlBytes[2] = (byte)value.Month;

            // Day (1 byte)
            dtlBytes[3] = (byte)value.Day;

            // Weekday (1 byte) - can calculate or set to 0
            dtlBytes[4] = (byte)((int)value.DayOfWeek + 1); // S7 uses 1-7 (Sunday=1)

            // Hour (1 byte)
            dtlBytes[5] = (byte)value.Hour;

            // Minute (1 byte)
            dtlBytes[6] = (byte)value.Minute;

            // Second (1 byte)
            dtlBytes[7] = (byte)value.Second;

            // Nanoseconds (4 bytes) - set to 0 for now
            dtlBytes[8] = 0;
            dtlBytes[9] = 0;
            dtlBytes[10] = 0;
            dtlBytes[11] = 0;

            // Write the bytes directly
            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = address.DbNumber,
                StartByteAdr = address.StartByte,
                VarType = VarType.Byte,
                Count = dtlBytes.Length,
                Value = dtlBytes
            };

            await plc.WriteAsync(new[] { dataItem });
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
        // Handle string variables
        if (variable.Contains("String", StringComparison.OrdinalIgnoreCase))
        {
            return await WriteStringAsync(variable, value?.ToString() ?? string.Empty);
        }

        // Handle DateTime variables
        if (variable.Contains("DateTime", StringComparison.OrdinalIgnoreCase))
        {
            // Try to convert to DateTime
            if (value is SDateTime dateTimeValue)
            {
                return await WriteDtlAsync(variable, dateTimeValue);
            }
            else if (value is string dateTimeStr && SDateTime.TryParse(dateTimeStr, out SDateTime parsedDateTime))
            {
                return await WriteDtlAsync(variable, parsedDateTime);
            }
        }

        // Handle Real (Double/Float) variables
        if (variable.Contains("DBD", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (double.TryParse(value?.ToString(), out double doubleValue))
                {
                    return await WriteRealAsync(variable, doubleValue);
                }
            }
            catch { }
        }

        // Handle Word variables
        if (variable.Contains("DBW", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (ushort.TryParse(value?.ToString(), out ushort wordValue))
                {
                    return await WriteWordAsync(variable, wordValue);
                }
            }
            catch { }
        }

        // Original method for other types
        bool success = false;
        try
        {
            await plc.WriteAsync(variable, value);
            success = true;
        }
        catch { }

        if (!success)
        {
            // Try various type conversions
            Type[] typesToTry = new Type[]
            {
                typeof(bool), typeof(byte), typeof(short), typeof(ushort),
                typeof(int), typeof(uint), typeof(float), typeof(double)
            };

            foreach (Type type in typesToTry)
            {
                try
                {
                    object convertedValue = Convert.ChangeType(value, type);
                    await plc.WriteAsync(variable, convertedValue);
                    success = true;
                    break;
                }
                catch { }
            }
        }

        return success;
    }

    #endregion

    #region UTILITY METHODS

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

    #endregion
}