using S7.Net;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Text.RegularExpressions;

namespace SiemensS7DemoConnection
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private ISiemensS7Wrapper s7;
        private string _dataType = "Default";
        private string _wrapperVersion = "Version 1";
        private DateTime _writeDateTime = DateTime.Now;
        private double _writeDouble = 0.0;
        private int _writeInt = 0;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            IpAddress = "192.168.1.72";
            Cpu = "S71500";
            Rack = 0;
            Slot = 1;
            ReadAddress = "DB3.DBX5.2";
            WriteAddress = "DB3.DBX5.2";
            // Expanding available data types to include the problematic ones
            AvailableDataTypes = new string[] { "Default", "String", "DateTime", "Double", "Word" };
            AvailableWrapperVersions = new string[] { "Version 1", "Version 2", "Version 3", "Version 4" };
        }

        public string IpAddress { get; set; }
        public string Cpu { get; set; }
        public int Rack { get; set; }
        public int Slot { get; set; }
        public string ReadValue { get; set; }
        public string WriteValue { get; set; }
        public string ReadAddress { get; set; }
        public string WriteAddress { get; set; }
        public string ProcessResult { get; set; }
        public string[] AvailableDataTypes { get; set; }
        public string[] AvailableWrapperVersions { get; set; }

        public bool IsDateTimeSelected => SelectedDataType == "DateTime";
        public bool IsDoubleSelected => SelectedDataType == "Double";
        public bool IsWordSelected => SelectedDataType == "Word";

        public DateTime WriteDateTime
        {
            get => _writeDateTime;
            set
            {
                if (_writeDateTime != value)
                {
                    _writeDateTime = value;
                    OnPropertyChanged();
                }
            }
        }

        public double WriteDouble
        {
            get => _writeDouble;
            set
            {
                if (_writeDouble != value)
                {
                    _writeDouble = value;
                    OnPropertyChanged();
                }
            }
        }

        public int WriteInt
        {
            get => _writeInt;
            set
            {
                if (_writeInt != value)
                {
                    _writeInt = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedWrapperVersion
        {
            get => _wrapperVersion;
            set
            {
                if (_wrapperVersion != value)
                {
                    _wrapperVersion = value;
                    OnPropertyChanged();

                    // Reset connection when wrapper version changes
                    if (s7 != null && s7.IsConnected)
                    {
                        s7.DisconnectAsync().ConfigureAwait(false);
                        s7 = null;
                        ProcessResult = $"Disconnected due to wrapper version change - {DateTime.Now}";
                        OnPropertyChanged(nameof(ProcessResult));
                    }
                }
            }
        }

        public string SelectedDataType
        {
            get => _dataType;
            set
            {
                if (_dataType != value)
                {
                    _dataType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDateTimeSelected));
                    OnPropertyChanged(nameof(IsDoubleSelected));
                    OnPropertyChanged(nameof(IsWordSelected));

                    // Update addresses based on data type selection
                    if (_dataType == "String")
                    {
                        if (!ReadAddress.Contains("String"))
                            ReadAddress = "DB26.DBB64.String200";
                        if (!WriteAddress.Contains("String"))
                            WriteAddress = "DB26.DBB64.String200";
                    }
                    else if (_dataType == "DateTime")
                    {
                        if (!ReadAddress.Contains("DateTime"))
                            ReadAddress = "DB7.DBB100.DateTime";
                        if (!WriteAddress.Contains("DateTime"))
                            WriteAddress = "DB7.DBB100.DateTime";
                    }
                    else if (_dataType == "Double")
                    {
                        if (!ReadAddress.Contains("DBD"))
                            ReadAddress = "DB1.DBD4";
                        if (!WriteAddress.Contains("DBD"))
                            WriteAddress = "DB1.DBD4";
                    }
                    else if (_dataType == "Word")
                    {
                        if (!ReadAddress.Contains("DBW"))
                            ReadAddress = "DB1.DBW20";
                        if (!WriteAddress.Contains("DBW"))
                            WriteAddress = "DB1.DBW20";
                    }
                    else if (_dataType == "Default")
                    {
                        // Remove specific data type addresses
                        ReadAddress = "DB3.DBX5.2";
                        WriteAddress = "DB3.DBX5.2";
                    }
                    OnPropertyChanged(nameof(ReadAddress));
                    OnPropertyChanged(nameof(WriteAddress));
                }
            }
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cpuType = (CpuType)Enum.Parse(typeof(CpuType), Cpu);

                // Create the appropriate wrapper based on selection
                if (SelectedWrapperVersion == "Version 1")
                {
                    s7 = new SiemensS7WrapperV1(cpuType, IpAddress, Rack, Slot);
                    ProcessResult = $"Using SiemensS7WrapperV1 (Original Implementation)";
                }
                else if (SelectedWrapperVersion == "Version 2")
                {
                    s7 = new SiemensS7WrapperV2(cpuType, IpAddress, Rack, Slot);
                    ProcessResult = $"Using SiemensS7WrapperV2 (Improved Implementation)";
                }
                else if (SelectedWrapperVersion == "Version 3")
                {
                    s7 = new SiemensS7WrapperV3(cpuType, IpAddress, Rack, Slot);
                    ProcessResult = $"Using SiemensS7WrapperV3 (Direct Bytes Implementation)";
                }
                else // Version 4
                {
                    s7 = new SiemensS7WrapperV4(cpuType, IpAddress, Rack, Slot);
                    ProcessResult = $"Using SiemensS7WrapperV4 (Custom Structures Implementation)";
                }

                s7.ReadTimeout = 2000;
                s7.WriteTimeout = 2000;
                await s7.ConnectAsync();

                ProcessResult += s7.IsConnected
                    ? $"\r\nCONNECTION SUCCESSFUL - {DateTime.Now}"
                    : $"\r\nCONNECTION FAILED - {DateTime.Now}";
            }
            catch (Exception ex)
            {
                ProcessResult = $"CONNECTION FAILED - {DateTime.Now}\r\n{FormatExceptionMessage(ex)}";
            }
            OnPropertyChanged(nameof(ProcessResult));
        }

        private async void btnRead_Click(object sender, RoutedEventArgs e)
        {
            if (s7 == null || !s7.IsConnected)
            {
                ProcessResult = $"NOT CONNECTED - Please connect to PLC first - {DateTime.Now}";
                OnPropertyChanged(nameof(ProcessResult));
                return;
            }

            try
            {
                object result;

                // Add wrapper version info to result
                string wrapperInfo = $"Using {GetWrapperName()}";

                switch (SelectedDataType)
                {
                    case "String":
                        result = await s7.ReadAsync<string>(ReadAddress);
                        break;
                    case "DateTime":
                        result = await s7.ReadAsync<DateTime>(ReadAddress);
                        break;
                    case "Double":
                        result = await s7.ReadAsync<double>(ReadAddress);
                        break;
                    case "Word":
                        result = await s7.ReadAsync<ushort>(ReadAddress);
                        break;
                    default:
                        result = await s7.ReadAsync<object>(ReadAddress);
                        break;
                }

                ReadValue = result?.ToString() ?? "null";
                ProcessResult = $"{wrapperInfo}\r\nREAD SUCCESSFUL - {DateTime.Now}";
            }
            catch (Exception ex)
            {
                ProcessResult = $"READ FAILED - {DateTime.Now}\r\n{FormatExceptionMessage(ex)}";
            }
            OnPropertyChanged(string.Empty);
        }

        private async void btnWrite_Click(object sender, RoutedEventArgs e)
        {
            if (s7 == null || !s7.IsConnected)
            {
                ProcessResult = $"NOT CONNECTED - Please connect to PLC first - {DateTime.Now}";
                OnPropertyChanged(nameof(ProcessResult));
                return;
            }

            try
            {
                bool result;

                // Add wrapper version info to result
                string wrapperInfo = $"Using {GetWrapperName()}";

                switch (SelectedDataType)
                {
                    case "String":
                        result = await s7.WriteAsync(WriteAddress, WriteValue);
                        break;
                    case "DateTime":
                        result = await s7.WriteAsync(WriteAddress, WriteDateTime);
                        break;
                    case "Double":
                        result = await s7.WriteAsync(WriteAddress, WriteDouble);
                        break;
                    case "Word":
                        result = await s7.WriteAsync(WriteAddress, WriteInt);
                        break;
                    default:
                        result = await s7.WriteAsyncUnknownType(WriteAddress, WriteValue);
                        break;
                }

                ProcessResult = result
                    ? $"{wrapperInfo}\r\nWRITE SUCCESSFUL - {DateTime.Now}"
                    : $"{wrapperInfo}\r\nWRITE FAILED - {DateTime.Now}";
            }
            catch (Exception ex)
            {
                ProcessResult = $"WRITE FAILED - {DateTime.Now}\r\n{FormatExceptionMessage(ex)}";
            }
            OnPropertyChanged(string.Empty);
        }

        private string GetWrapperName()
        {
            switch (SelectedWrapperVersion)
            {
                case "Version 1": return "SiemensS7WrapperV1";
                case "Version 2": return "SiemensS7WrapperV2";
                case "Version 3": return "SiemensS7WrapperV3";
                case "Version 4": return "SiemensS7WrapperV4";
                default: return SelectedWrapperVersion;
            }
        }

        private string FormatExceptionMessage(Exception ex)
        {
            // Get the exception message without the stack trace
            string message = ex.ToString();

            // If there's an inner exception, include its message too
            if (ex.InnerException != null)
            {
                message += $"\r\n→ {ex.InnerException.Message}";
            }

            // For PlcException, include the error code
            if (ex is PlcException plcEx)
            {
                message = $"Error: {plcEx.ErrorCode} - {message}";
            }

            // Remove or hide file paths from the message if they exist
            message = Regex.Replace(message, @"at .*\\.*\.cs:line \d+", "[stack trace hidden]");

            message += $"\r\n\r\nWrapper Version: {SelectedWrapperVersion}";

            return message;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}