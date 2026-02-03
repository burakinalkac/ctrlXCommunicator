using comm.datalayer;
using Datalayer;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace YourNamespace
{
    /// Interaction logic for ctrlXCommunicator.xaml
    /// Provides browsing, reading, and writing capabilities for ctrlX Data Layer.
    public partial class ctrlXCommunicator : Window
    {
        // Data Layer System and Client
        private DatalayerSystem _system;
        private IClient _client;

        // Async Loop Controls
        private CancellationTokenSource _pingCts;
        private CancellationTokenSource _readCts;
        private volatile bool _connectionHealthy;

        // Path and Type Management
        private string _currentBrowsingPath = "";
        private string _selectedMonitoringPath = "";
        private DLR_VARIANT_TYPE _currentSelectedType;

        private const string PATH_START = "plc/app/Application/sym/GVL_CUSTOMER/stSetUp/Vacuum1_Feedback";
        private const string PATH_PAUSE = "plc/app/Application/sym/GVL_CUSTOMER/stSetUp/Vacuum1_Use";
        private const string PATH_ABORT = "plc/app/Application/sym/GVL_CUSTOMER/stSetUp/Vacuum2_Feedback";
        private const string PATH_RESET = "plc/app/Application/sym/GVL_CUSTOMER/stSetUp/Vacuum2_Use";

        public ctrlXCommunicator()
        {
            InitializeComponent();
            Loaded += CtrlXCommunicator_Loaded;
            Closed += CtrlXCommunicator_Closed;
        }

        private void CtrlXCommunicator_Loaded(object sender, RoutedEventArgs e)
        {
            _pingCts = new CancellationTokenSource();
            _readCts = new CancellationTokenSource();

            // Start background tasks for connection monitoring and live value reading
            Task.Run(() => PingMonitorAsync(_pingCts.Token));
            Task.Run(() => ReadLoopAsync(_readCts.Token));
        }

        #region CONNECTION MANAGEMENT

        /// Establishes connection to the ctrlX device using the IP address from the UI.
        private bool Connect()
        {
            try
            {
                Disconnect();

                _system = new DatalayerSystem();
                _system.Start(startBroker: false);

                string targetIp = "";
                Dispatcher.Invoke(() => { targetIp = IpAddressInput.Text; });

                var remote = new Remote(ip: targetIp, sslPort: 443).ToString();
                _client = _system.Factory.CreateClient(remote);

                var pingResult = _client.Ping();
                if (pingResult.IsGood())
                {
                    Task.Delay(500).Wait();
                    _connectionHealthy = true;
                    Dispatcher.Invoke(() => BrowseNodes("")); // Initialize browser at root
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => { ConnectionStatusText.Text = "ERR: " + ex.Message; });
                return false;
            }
        }

        /// Properly disposes of client and system resources.
        private void Disconnect()
        {
            _connectionHealthy = false;
            _client?.Dispose();
            _system?.Stop();
            _system?.Dispose();
            _client = null;
            _system = null;
        }

        /// Triggers manual reconnection or IP update.
        private void BtnApplyIp_Click(object sender, RoutedEventArgs e)
        {
            _connectionHealthy = false;
            Disconnect();

            Task.Run(() =>
            {
                if (Connect())
                    SetConnectionState("Connected", Brushes.LimeGreen);
                else
                    SetConnectionState("Failed", Brushes.Red);
            });
        }

        #endregion

        #region BROWSER LOGIC

        /// Fetches child nodes for a given path and updates the UI list.
        private void BrowseNodes(string path)
        {
            if (_client == null || !_connectionHealthy) return;

            var browseResult = _client.Browse(path);

            if (browseResult.Item1.IsGood())
            {
                _currentBrowsingPath = path;

                using (var val = browseResult.Item2)
                {
                    var nodes = val.ToStringArray();

                    if (nodes != null && nodes.Length > 0)
                        NodeListBox.ItemsSource = nodes.OrderBy(n => n).ToList();
                    else
                        NodeListBox.ItemsSource = new List<string> { "(No child nodes found)" };
                }
            }
            else
            {
                MessageBox.Show($"Browse Error: {browseResult.Item1} for path: {path}");
            }
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => BrowseNodes(_currentBrowsingPath);

        private void NodeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedName = NodeListBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedName) || selectedName.Contains("(")) return;

            _selectedMonitoringPath = string.IsNullOrEmpty(_currentBrowsingPath)
                                     ? selectedName
                                     : $"{_currentBrowsingPath}/{selectedName}";

            CurrentPathText.Text = _selectedMonitoringPath;
            WriteErrorText.Text = "";
            UpdateWriteUI();
        }

        private void NodeListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selectedName = NodeListBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedName) || selectedName.Contains("(")) return;

            string newPath = string.IsNullOrEmpty(_currentBrowsingPath)
                             ? selectedName
                             : $"{_currentBrowsingPath}/{selectedName}";

            BrowseNodes(newPath);
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentBrowsingPath)) return;

            var parts = _currentBrowsingPath.Split('/').ToList();
            if (parts.Count > 1)
            {
                parts.RemoveAt(parts.Count - 1);
                BrowseNodes(string.Join("/", parts));
            }
            else
            {
                BrowseNodes("");
            }
        }

        #endregion

        #region MONITORING (LIVE READ)

        /// Asynchronous loop to read the selected node value every 500ms.
        private async Task ReadLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_connectionHealthy && _client != null && !string.IsNullOrEmpty(_selectedMonitoringPath))
                {
                    var readResult = _client.Read(_selectedMonitoringPath);
                    if (readResult.Item1.IsGood())
                    {
                        using (var val = readResult.Item2)
                        {
                            string displayValue = "Unknown";
                            if (val.IsBool)
                                displayValue = val.ToBool().ToString();
                            else if (val.IsString)
                                displayValue = val.ToString();
                            else if (val.DataType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_INT16)
                                displayValue = val.ToInt16().ToString();
                            else if (val.DataType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_INT8)
                                displayValue = val.ToSByte().ToString();
                            else if (val.DataType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_UINT8)
                                displayValue = val.ToByte().ToString();
                            else if (val.IsNumber)
                                displayValue = "Number (Check DataType)";
                            
                            Dispatcher.Invoke(() => 
                            {
                                ValueText.Text = displayValue;
                                ValueText.Foreground = Brushes.Black;
                                if (ResultStatusText != null)
                                {
                                    string fullType = val.DataType.ToString();
                                    string shortType = fullType.Substring(fullType.LastIndexOf('_') + 1);
                                    ResultStatusText.Text = $"Type: {shortType}";
                                }
                            });
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ValueText.Text = "---";
                            ValueText.Foreground = Brushes.Gray;
                        });
                    }
                }
                await Task.Delay(500, token);
            }
        }

        private string ProcessVariantValue(Variant val)
        {
            if (val.IsBool) return val.ToBool().ToString();
            if (val.IsString) return val.ToString();

            switch (val.DataType)
            {
                case DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_INT8: return val.ToSByte().ToString();
                case DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_UINT8: return val.ToByte().ToString();
                case DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_INT16: return val.ToInt16().ToString();
                case DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_INT32: return val.ToInt32().ToString();
                case DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_FLOAT32: return val.ToFloat().ToString();
                default:
                    return val.IsNumber ? "Number" : "Unknown Type";
            }
        }

        #endregion

        #region WRITE LOGIC

        /// Configures the Write Panel UI based on the selected node's data type.
        private void UpdateWriteUI()
        {
            if (_client == null || string.IsNullOrEmpty(_selectedMonitoringPath)) return;

            var readResult = _client.Read(_selectedMonitoringPath);
            if (readResult.Item1.IsGood())
            {
                using (var val = readResult.Item2)
                {
                    _currentSelectedType = val.DataType;
                    WritePanel.Visibility = Visibility.Visible;

                    if (val.IsBool)
                    {
                        BoolWriteCombo.Visibility = Visibility.Visible;
                        ValueWriteInput.Visibility = Visibility.Collapsed;
                        BoolWriteCombo.SelectedIndex = val.ToBool() ? 0 : 1;
                    }
                    else
                    {
                        BoolWriteCombo.Visibility = Visibility.Collapsed;
                        ValueWriteInput.Visibility = Visibility.Visible;
                        ValueWriteInput.Text = val.ToString();
                    }
                }
            }
            else
            {
                WritePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnWrite_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null) return;
            WriteErrorText.Text = "";

            try
            {
                Variant writeValue = null;

                if (_currentSelectedType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_BOOL8)
                {
                    writeValue = new Variant(BoolWriteCombo.SelectedIndex == 0);
                }
                else if (_currentSelectedType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_INT16)
                {
                    if (short.TryParse(ValueWriteInput.Text, out short val)) writeValue = new Variant(val);
                    else throw new Exception("Invalid Int16 format!");
                }
                else if (_currentSelectedType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_FLOAT32)
                {
                    if (float.TryParse(ValueWriteInput.Text, out float val)) writeValue = new Variant(val);
                    else throw new Exception("Invalid Float32 format!");
                }
                else if (_currentSelectedType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_STRING)
                {
                    writeValue = new Variant(ValueWriteInput.Text);
                }

                if (writeValue != null)
                {
                    var result = _client.Write(_selectedMonitoringPath, writeValue);
                    if (result.IsBad()) WriteErrorText.Text = "Write Error: " + result.ToString();
                    writeValue.Dispose();
                }
            }
            catch (Exception ex)
            {
                WriteErrorText.Text = ex.Message;
            }
        }

        #endregion

        #region HEALTH MONITORING

        private async Task PingMonitorAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_client == null || !_connectionHealthy)
                {
                    SetConnectionState("Connecting...", Brushes.Orange);
                    if (Connect())
                    {
                        _connectionHealthy = true;
                        SetConnectionState("Connected", Brushes.LimeGreen);
                    }
                }
                else
                {
                    if (!_client.Ping().IsGood())
                    {
                        _connectionHealthy = false;
                        SetConnectionState("Disconnected", Brushes.Red);
                        Disconnect();
                    }
                }
                await Task.Delay(2000, token);
            }
        }

        private void SetConnectionState(string text, Brush color)
        {
            Dispatcher.Invoke(() => {
                ConnectionStatusText.Text = text;
                ConnectionLed.Fill = color;
            });
        }

        private void CtrlXCommunicator_Closed(object sender, EventArgs e)
        {
            _pingCts?.Cancel();
            _readCts?.Cancel();
            Disconnect();
        }

        #endregion

        #region PROCESS BUTTONS
        private void SetVariableTrue(string path)
        {
            if (_client == null || !_connectionHealthy)
            {
                MessageBox.Show("Lütfen önce bağlantı kurun!", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var val = new Variant(true)) // Değeri TRUE yapıyoruz
                {
                    var result = _client.Write(path, val);
                    if (result.IsBad())
                    {
                        MessageBox.Show($"{path} yazma hatası: {result}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"İşlem sırasında hata oluştu: {ex.Message}");
            }
        }
        private void BtnStart_Click(object sender, RoutedEventArgs e) => SetVariableTrue(PATH_START);
        private void BtnPause_Click(object sender, RoutedEventArgs e) => SetVariableTrue(PATH_PAUSE);
        private void BtnAbort_Click(object sender, RoutedEventArgs e) => SetVariableTrue(PATH_ABORT);
        private void BtnReset_Click(object sender, RoutedEventArgs e) => SetVariableTrue(PATH_RESET);
        #endregion

    }
}