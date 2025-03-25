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
        private SiemensS7Wrapper s7;
        private string _dataType = "Default";
        private DateTime _writeDateTime = DateTime.Now;

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
            AvailableDataTypes = new string[] { "Default", "String", "DateTime" };
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

        public bool IsDateTimeSelected => SelectedDataType == "DateTime";

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
                    else if (_dataType == "Default")
                    {
                        // Remove String and DateTime from addresses
                        if (ReadAddress.Contains("String") || ReadAddress.Contains("DateTime"))
                            ReadAddress = "DB3.DBX5.2";
                        if (WriteAddress.Contains("String") || WriteAddress.Contains("DateTime"))
                            WriteAddress = "DB3.DBX5.2";
                    }
                    OnPropertyChanged(nameof(ReadAddress));
                    OnPropertyChanged(nameof(WriteAddress));
                }
            }
        }

        private async void btnRead_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                object result;
                switch (SelectedDataType)
                {
                    case "String":
                        result = await s7.ReadAsync<string>(ReadAddress);
                        break;
                    case "DateTime":
                        result = await s7.ReadAsync<DateTime>(ReadAddress);
                        break;
                    default:
                        result = await s7.ReadAsync<object>(ReadAddress);
                        break;
                }

                ReadValue = result?.ToString() ?? "null";
                ProcessResult = $"READ SUCCESSFUL - {DateTime.Now}";
            }
            catch (Exception ex)
            {
                ProcessResult = $"READ FAILED - {DateTime.Now}\r\n{FormatExceptionMessage(ex)}";
            }
            OnPropertyChanged(string.Empty);
        }

        private async void btnWrite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool result;
                switch (SelectedDataType)
                {
                    case "String":
                        result = await s7.WriteAsync(WriteAddress, WriteValue);
                        break;
                    case "DateTime":
                        result = await s7.WriteAsync(WriteAddress, WriteDateTime);
                        break;
                    default:
                        result = await s7.WriteAsyncUnknownType(WriteAddress, WriteValue);
                        break;
                }

                ProcessResult = result
                    ? $"WRITE SUCCESSFUL - {DateTime.Now}"
                    : $"WRITE FAILED - {DateTime.Now}";
            }
            catch (Exception ex)
            {
                ProcessResult = $"WRITE FAILED - {DateTime.Now}\r\n{FormatExceptionMessage(ex)}";
            }
            OnPropertyChanged(string.Empty);
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cpuType = (CpuType)Enum.Parse(typeof(CpuType), Cpu);
                s7 = new SiemensS7Wrapper(cpuType, IpAddress, Rack, Slot);
                s7.ReadTimeout = 2000;
                s7.WriteTimeout = 2000;
                await s7.ConnectAsync();
                ProcessResult = s7.IsConnected
                    ? $"CONNECTION SUCCESSFUL - {DateTime.Now}"
                    : $"CONNECTION FAILED - {DateTime.Now}";
            }
            catch (Exception ex)
            {
                ProcessResult = $"CONNECTION FAILED - {DateTime.Now}\r\n{FormatExceptionMessage(ex)}";
            }
            OnPropertyChanged(nameof(ProcessResult));
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

            //// For PlcException, include the error code
            //if (ex is PlcException plcEx)
            //{
            //    message = $"Error: {plcEx.ErrorCode} - {message}";
            //}

            // Remove or hide file paths from the message if they exist
            message = Regex.Replace(message, @"at .*\\.*\.cs:line \d+", "[stack trace hidden]");

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