using Microsoft.VisualBasic;
using S7.Net;
using S7.Net.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using static System.Runtime.InteropServices.JavaScript.JSType;

// Use alias to avoid ambiguity between System.DateTime and S7.Net.Types.DateTime
using SDateTime = System.DateTime;

namespace SiemensS7DemoConnection
{
    /// <summary>
    /// Version 4 of Siemens S7 wrapper - uses S7 specific data structures with manual alignment
    /// SiemensS7WrapperV4 (Custom Structures Implementation)
    /// Uses custom structures that match S7 PLC data types exactly
    /// Implements multiple approaches with fallbacks for each data type
    /// Custom DTL structure with proper byte mapping
    /// More robust error handling and diagnostic information
    /// Tries multiple methods in sequence for each operation to maximize compatibility
    /// </summary>
    public class SiemensS7WrapperV4 : ISiemensS7Wrapper
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

        public event AsyncEventHandler Connected;
        public event AsyncEventHandler Disconnected;
        public event AsyncEventHandler<ErrorCode> PlcErrorOccured;

        #endregion

        #region CUSTOM STRUCTURES

        // Define constant buffer sizes for specific operations
        private const int REAL_SIZE = 4;  // Size of REAL (float) data - 4 bytes
        private const int WORD_SIZE = 2;  // Size of WORD data - 2 bytes
        private const int DWORD_SIZE = 4; // Size of DWORD (double word) data - 4 bytes
        private const int DTL_SIZE = 12;  // Size of DTL data - 12 bytes

        // Custom DTL structure to match S7-1200/1500 format
        private struct Dtl
        {
            public ushort Year;   // 2 bytes
            public byte Month;    // 1 byte
            public byte Day;      // 1 byte
            public byte Weekday;  // 1 byte (optional)
            public byte Hour;     // 1 byte
            public byte Minute;   // 1 byte
            public byte Second;   // 1 byte
            public uint Nanosec;  // 4 bytes

            // Convert to .NET DateTime
            public SDateTime ToDateTime()
            {
                // Validate components
                if (Month < 1 || Month > 12)
                    throw new ArgumentOutOfRangeException($"Invalid month value in DTL: {Month}");
                if (Day < 1 || Day > 31)
                    throw new ArgumentOutOfRangeException($"Invalid day value in DTL: {Day}");
                if (Hour > 23)
                    throw new ArgumentOutOfRangeException($"Invalid hour value in DTL: {Hour}");
                if (Minute > 59)
                    throw new ArgumentOutOfRangeException($"Invalid minute value in DTL: {Minute}");
                if (Second > 59)
                    throw new ArgumentOutOfRangeException($"Invalid second value in DTL: {Second}");

                return new SDateTime(Year, Month, Day, Hour, Minute, Second);
            }

            // Convert from .NET DateTime
            public static Dtl FromDateTime(SDateTime dateTime)
            {
                return new Dtl
                {
                    Year = (ushort)dateTime.Year,
                    Month = (byte)dateTime.Month,
                    Day = (byte)dateTime.Day,
                    Weekday = (byte)((int)dateTime.DayOfWeek + 1), // S7 uses 1-7 (Sunday=1)
                    Hour = (byte)dateTime.Hour,
                    Minute = (byte)dateTime.Minute,
                    Second = (byte)dateTime.Second,
                    Nanosec = 0 // Initialize to 0
                };
            }

            // Convert to raw byte array for S7 PLC
            public byte[] ToByteArray()
            {
                byte[] bytes = new byte[DTL_SIZE];

                // Year - 2 bytes (little-endian format for S7)
                bytes[0] = (byte)(Year & 0xFF);
                bytes[1] = (byte)((Year >> 8) & 0xFF);

                // Rest of the fields - 1 byte each
                bytes[2] = Month;
                bytes[3] = Day;
                bytes[4] = Weekday;
                bytes[5] = Hour;
                bytes[6] = Minute;
                bytes[7] = Second;

                // Nanoseconds - 4 bytes
                bytes[8] = (byte)(Nanosec & 0xFF);
                bytes[9] = (byte)((Nanosec >> 8) & 0xFF);
                bytes[10] = (byte)((Nanosec >> 16) & 0xFF);
                bytes[11] = (byte)((Nanosec >> 24) & 0xFF);

                return bytes;
            }

            // Parse from raw byte array from S7 PLC
            public static Dtl FromByteArray(byte[] bytes)
            {
                if (bytes == null || bytes.Length < DTL_SIZE)
                    throw new ArgumentException("Byte array must be at least 12 bytes for DTL", nameof(bytes));

                Dtl dtl = new Dtl();

                // Year - 2 bytes (little-endian)
                dtl.Year = (ushort)(bytes[0] | (bytes[1] << 8));

                // Rest of the fields
                dtl.Month = bytes[2];
                dtl.Day = bytes[3];
                dtl.Weekday = bytes[4];
                dtl.Hour = bytes[5];
                dtl.Minute = bytes[6];
                dtl.Second = bytes[7];

                // Nanoseconds - 4 bytes (little-endian)
                dtl.Nanosec = (uint)(bytes[8] | (bytes[9] << 8) | (bytes[10] << 16) | (bytes[11] << 24));

                return dtl;
            }
        }

        #endregion

        public SiemensS7WrapperV4(CpuType cpuType, string ipAddress, int rack = 0, int slot = 1)
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
                    // Handle DateTime type using V4 DTL structure
                    return await ReadDtlAsync<T>(variable);
                }
                else if (typeof(T) == typeof(double) || typeof(T) == typeof(float) ||
                         variable.Contains("DBD", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle REAL data type
                    return await ReadRealAsync<T>(variable);
                }
                else if (typeof(T) == typeof(ushort) || typeof(T) == typeof(int) ||
                         variable.Contains("DBW", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle WORD data type
                    return await ReadWordAsync<T>(variable);
                }
                else
                {
                    // Default handling for other types
                    object result = await plc.ReadAsync(variable);
                    if (result == null)
                    {
                        return default(T);
                    }
                    var convertedResult = (T)Convert.ChangeType(result, typeof(T));
                    return convertedResult;
                }
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

        private async Task<T> ReadRealAsync<T>(string variable)
        {
            try
            {
                // Approach 4: Use specialized VarType for Real, but add redundancy and validation steps
                var address = ParseAddress(variable);

                // Try different approaches in sequence, with fallbacks

                // Approach 1: First try using the proper VarType.Real
                try
                {
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

                    if (updatedItems[0].Value is float floatValue)
                    {
                        if (typeof(T) == typeof(double))
                            return (T)(object)(double)floatValue;
                        else if (typeof(T) == typeof(float))
                            return (T)(object)floatValue;
                        else
                            return (T)Convert.ChangeType(floatValue, typeof(T));
                    }
                }
                catch
                {
                    // If Approach 1 fails, continue to next approach
                }

                // Approach 2: Try reading as raw DWord and manually interpret as Real
                try
                {
                    var dataItem = new DataItem
                    {
                        DataType = DataType.DataBlock,
                        DB = address.DbNumber,
                        StartByteAdr = address.StartByte,
                        VarType = VarType.DWord,
                        Count = 1,
                        Value = new object()
                    };

                    var items = new List<DataItem> { dataItem };
                    var updatedItems = await plc.ReadMultipleVarsAsync(items);

                    if (updatedItems[0].Value is uint uintValue)
                    {
                        // Convert DWord value to float using byte reinterpretation
                        byte[] bytes = BitConverter.GetBytes(uintValue);
                        float floatValue = BitConverter.ToSingle(bytes, 0);

                        if (typeof(T) == typeof(double))
                            return (T)(object)(double)floatValue;
                        else if (typeof(T) == typeof(float))
                            return (T)(object)floatValue;
                        else
                            return (T)Convert.ChangeType(floatValue, typeof(T));
                    }
                }
                catch
                {
                    // If Approach 2 fails, continue to next approach
                }

                // Approach 3: Try reading raw bytes and manually convert
                try
                {
                    var rawBytes = await ReadBytesAsync(address.DbNumber, address.StartByte, REAL_SIZE);
                    float floatValue = BitConverter.ToSingle(rawBytes, 0);

                    if (typeof(T) == typeof(double))
                        return (T)(object)(double)floatValue;
                    else if (typeof(T) == typeof(float))
                        return (T)(object)floatValue;
                    else
                        return (T)Convert.ChangeType(floatValue, typeof(T));
                }
                catch
                {
                    // If Approach 3 fails, try with reversed byte order
                    var rawBytes = await ReadBytesAsync(address.DbNumber, address.StartByte, REAL_SIZE);
                    Array.Reverse(rawBytes);
                    float floatValue = BitConverter.ToSingle(rawBytes, 0);

                    if (typeof(T) == typeof(double))
                        return (T)(object)(double)floatValue;
                    else if (typeof(T) == typeof(float))
                        return (T)(object)floatValue;
                    else
                        return (T)Convert.ChangeType(floatValue, typeof(T));
                }
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

                // Try multiple approaches with fallbacks

                // Approach 1: Try using proper VarType.Word
                try
                {
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
                        if (typeof(T) == typeof(ushort))
                            return (T)(object)wordValue;
                        else if (typeof(T) == typeof(int))
                            return (T)(object)(int)wordValue;
                        else
                            return (T)Convert.ChangeType(wordValue, typeof(T));
                    }
                }
                catch
                {
                    // If Approach 1 fails, try the next approach
                }

                // Approach 2: Read individual bytes and manually construct Word
                try
                {
                    var rawBytes = await ReadBytesAsync(address.DbNumber, address.StartByte, WORD_SIZE);
                    ushort wordValue = BitConverter.ToUInt16(rawBytes, 0);

                    if (typeof(T) == typeof(ushort))
                        return (T)(object)wordValue;
                    else if (typeof(T) == typeof(int))
                        return (T)(object)(int)wordValue;
                    else
                        return (T)Convert.ChangeType(wordValue, typeof(T));
                }
                catch
                {
                    // If Approach 2 fails, try with reversed byte order
                    var rawBytes = await ReadBytesAsync(address.DbNumber, address.StartByte, WORD_SIZE);
                    Array.Reverse(rawBytes);
                    ushort wordValue = BitConverter.ToUInt16(rawBytes, 0);

                    if (typeof(T) == typeof(ushort))
                        return (T)(object)wordValue;
                    else if (typeof(T) == typeof(int))
                        return (T)(object)(int)wordValue;
                    else
                        return (T)Convert.ChangeType(wordValue, typeof(T));
                }
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

        private async Task<T> ReadDtlAsync<T>(string variable)
        {
            try
            {
                var address = ParseAddress(variable);

                // Read raw bytes for DTL structure
                byte[] rawBytes = await ReadBytesAsync(address.DbNumber, address.StartByte, DTL_SIZE);

                // Convert bytes to DTL structure
                Dtl dtl = Dtl.FromByteArray(rawBytes);

                // Convert DTL to DateTime
                var dateTime = dtl.ToDateTime();

                if (typeof(T) == typeof(SDateTime) || typeof(T) == typeof(object))
                {
                    return (T)(object)dateTime;
                }
                else
                {
                    return (T)Convert.ChangeType(dateTime, typeof(T));
                }
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

        private async Task<byte[]> ReadBytesAsync(int dbNumber, int startByte, int count)
        {
            // Read raw bytes directly
            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = dbNumber,
                StartByteAdr = startByte,
                VarType = VarType.Byte,
                Count = count
            };

            var items = new List<DataItem> { dataItem };
            var results = await plc.ReadMultipleVarsAsync(items);

            if (results[0].Value is byte[] bytes)
            {
                return bytes;
            }

            throw new InvalidOperationException("Failed to read bytes from PLC");
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

        private class AddressInfo
        {
            public int DbNumber { get; set; }
            public int StartByte { get; set; }
            public int BitPosition { get; set; } = -1;
            public string AddressType { get; set; } = ""; // DBX, DBB, DBW, DBD, etc.
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
                info.AddressType = "DBX";
                string addrPart = afterDb.Substring(3);
                string[] parts = addrPart.Split('.');

                if (parts.Length >= 2 && int.TryParse(parts[0], out int byteAddr) && int.TryParse(parts[1], out int bitPos))
                {
                    info.StartByte = byteAddr;
                    info.BitPosition = bitPos;
                }
            }
            // Check for byte address (DBB format)
            else if (afterDb.StartsWith("DBB", StringComparison.OrdinalIgnoreCase))
            {
                info.AddressType = "DBB";
                string addrPart = afterDb.Substring(3);
                int dotPos = addrPart.IndexOf('.');
                string byteAddrStr = dotPos > 0 ? addrPart.Substring(0, dotPos) : addrPart;

                if (int.TryParse(byteAddrStr, out int byteAddr))
                {
                    info.StartByte = byteAddr;
                }
            }
            // Check for word address (DBW format)
            else if (afterDb.StartsWith("DBW", StringComparison.OrdinalIgnoreCase))
            {
                info.AddressType = "DBW";
                string addrPart = afterDb.Substring(3);
                int dotPos = addrPart.IndexOf('.');
                string byteAddrStr = dotPos > 0 ? addrPart.Substring(0, dotPos) : addrPart;

                if (int.TryParse(byteAddrStr, out int byteAddr))
                {
                    info.StartByte = byteAddr;
                }
            }
            // Check for double word address (DBD format)
            else if (afterDb.StartsWith("DBD", StringComparison.OrdinalIgnoreCase))
            {
                info.AddressType = "DBD";
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
                else if ((value is double || value is float) ||
                         variable.Contains("DBD", StringComparison.OrdinalIgnoreCase))
                {
                    // Special handling for floating point values
                    return await WriteRealAsync(variable, value);
                }
                else if (variable.Contains("DBW", StringComparison.OrdinalIgnoreCase))
                {
                    // Special handling for Word
                    return await WriteWordAsync(variable, value);
                }
                else
                {
                    // Default handling for other types
                    await plc.WriteAsync(variable, value);
                    return true;
                }
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

                // Use multiple approaches and fallbacks

                // Approach 1: Try using proper VarType.Real
                try
                {
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
                catch
                {
                    // If Approach 1 fails, try next approach
                }

                // Approach 2: Try using byte representation
                try
                {
                    byte[] bytes = BitConverter.GetBytes(floatValue);
                    await WriteBytesAsync(address.DbNumber, address.StartByte, bytes);
                    return true;
                }
                catch
                {
                    // If Approach 2 fails, try with reversed byte order
                    byte[] bytes = BitConverter.GetBytes(floatValue);
                    Array.Reverse(bytes);
                    await WriteBytesAsync(address.DbNumber, address.StartByte, bytes);
                    return true;
                }
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

                // Try multiple approaches with fallbacks

                // Approach 1: Try with proper VarType.Word
                try
                {
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
                catch
                {
                    // If Approach 1 fails, try byte-by-byte approach
                }

                // Approach 2: Write each byte individually
                try
                {
                    byte[] bytes = BitConverter.GetBytes(wordValue);

                    // Write low byte and high byte separately
                    if (BitConverter.IsLittleEndian)
                    {
                        // Write in the PLC's expected byte order (which might be different)
                        await WriteBytesAsync(address.DbNumber, address.StartByte, bytes);
                        return true;
                    }
                    else
                    {
                        // If we're on a big-endian system (rare), might need to reverse
                        byte[] reversed = new byte[bytes.Length];
                        Array.Copy(bytes, reversed, bytes.Length);
                        Array.Reverse(reversed);
                        await WriteBytesAsync(address.DbNumber, address.StartByte, reversed);
                        return true;
                    }
                }
                catch
                {
                    // If Approach 2 fails, try with reversed byte order
                    byte[] bytes = BitConverter.GetBytes(wordValue);
                    Array.Reverse(bytes);
                    await WriteBytesAsync(address.DbNumber, address.StartByte, bytes);
                    return true;
                }
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

        private async Task<bool> WriteDtlAsync(string variable, SDateTime value)
        {
            try
            {
                var address = ParseAddress(variable);

                // Convert DateTime to DTL structure
                Dtl dtl = Dtl.FromDateTime(value);

                // Convert DTL to byte array
                byte[] dtlBytes = dtl.ToByteArray();

                // Write bytes to PLC
                await WriteBytesAsync(address.DbNumber, address.StartByte, dtlBytes);
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

        private async Task WriteBytesAsync(int dbNumber, int startByte, byte[] data)
        {
            // Write raw bytes to PLC
            var dataItem = new DataItem
            {
                DataType = DataType.DataBlock,
                DB = dbNumber,
                StartByteAdr = startByte,
                VarType = VarType.Byte,
                Count = data.Length,
                Value = data
            };

            await plc.WriteAsync(new[] { dataItem });
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

            // Try standard write
            try
            {
                await plc.WriteAsync(variable, value);
                return true;
            }
            catch (Exception)
            {
                // Try type conversion as fallback
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
                        return true;
                    }
                    catch { }
                }

                // If everything fails, throw the exception
                throw;
            }
        }

        #endregion

        #region UTILITY METHODS

        private static async Task FireEventIfNotNull(AsyncEventHandler eventToFire)
        {
            if (eventToFire != null)
                await Task.WhenAll(Array.ConvertAll(
                  eventToFire.GetInvocationList(),
                  e => ((AsyncEventHandler)e).Invoke()));
        }

        private static async Task FireEventIfNotNull<T>(AsyncEventHandler<T> eventToFire, T arg)
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

        private async void timer_Elapsed(object sender, ElapsedEventArgs e)
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
}