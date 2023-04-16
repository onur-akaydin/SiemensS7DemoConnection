using S7.Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SiemensS7DemoConnection;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private SiemensS7Wrapper s7;
    public MainWindow()
    {
        InitializeComponent();
        this.DataContext = this;
        IpAddress = "192.168.1.100";
        Cpu = "S71200";
        Rack = 0;
        Slot = 1;

        ReadAddress = "DB1.DBD52";
        WriteAddress = "DB15.DBX0.0";
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

    private async void btnRead_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await s7.ReadAsync<object>(ReadAddress);
            ReadValue = result.ToString();
            ProcessResult = $"OKUMA BAŞARILI - {DateTime.Now}";
        }
        catch (Exception ex)
        {
            ProcessResult = $"OKUMA BAŞARISIZ - {DateTime.Now} \r\n{ex}";
        }

        //OnPropertyChanged(nameof(ReadValue));
        //OnPropertyChanged(nameof(ProcessResult));
        OnPropertyChanged(string.Empty);
    }

    private async void btnWrite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await s7.WriteAsyncUnknownType(WriteAddress, WriteValue);
            ProcessResult = result ? $"YAZMA BAŞARILI - {DateTime.Now}" : $"YAZMA BAŞARISIZ - {DateTime.Now}";
        }
        catch (Exception ex)
        {
            ProcessResult = $"YAZMA BAŞARISIZ - {DateTime.Now} \r\n{ex}";
        }

        //OnPropertyChanged(nameof(ReadValue));
        //OnPropertyChanged(nameof(ProcessResult));
        OnPropertyChanged(string.Empty);
    }

    private async void btnConnect_Click(object sender, RoutedEventArgs e)
    {
        var cpuType = (CpuType)Enum.Parse(typeof(CpuType), Cpu);
        s7 = new SiemensS7Wrapper(cpuType, IpAddress, Rack, Slot);
        s7.ReadTimeout = 2000;
        s7.WriteTimeout = 2000;
        await s7.ConnectAsync();

        ProcessResult = s7.IsConnected ? $"BAĞLANTI BAŞARILI - {DateTime.Now}" : $"BAĞLANTI BAŞARISIZ - {DateTime.Now}";
        OnPropertyChanged(nameof(ProcessResult));
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;

        // ReSharper disable once ExplicitCallerInfoArgument
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
