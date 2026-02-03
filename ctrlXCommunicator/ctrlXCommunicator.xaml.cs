using comm.datalayer;
using Datalayer;
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
    public partial class ctrlXCommunicator : Window
    {
        // Data Layer Bağlantı Nesneleri
        private DatalayerSystem _system;
        private IClient _client;

        // Async Döngü Kontrolleri
        private CancellationTokenSource _pingCts;
        private CancellationTokenSource _readCts;
        private volatile bool _connectionHealthy;

        // Sabit PLC Değişken Yolları (Kendi projenize göre güncelleyin)
        private const string PATH_START = "plc/app/Application/sym/GVL_CUSTOMER/stSetUp/Vacuum1_Feedback";
        private const string PATH_PAUSE = "plc/app/Application/sym/GVL_CUSTOMER/stSetUp/Vacuum1_Use";
        private const string PATH_ABORT = "plc/app/Application/sym/GVL_CUSTOMER/stSetUp/Vacuum2_Feedback";
        private const string PATH_RESET = "plc/app/Application/sym/GVL_CUSTOMER/stSetUp/Vacuum2_Use";

        // Durum Yönetimi
        private string _currentBrowsingPath = "";
        private string _selectedMonitoringPath = "";
        private DLR_VARIANT_TYPE _currentSelectedType;

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

            // Arka plan görevlerini başlat
            Task.Run(() => PingMonitorAsync(_pingCts.Token));
            Task.Run(() => ReadLoopAsync(_readCts.Token));

            // PROGRAM AÇILIR AÇILMAZ OTOMATİK BAĞLANMAYI TETİKLE
            Task.Run(() => {
                if (Connect())
                    SetConnectionState("Connected", Brushes.LimeGreen);
            });
        }

        #region BAĞLANTI YÖNETİMİ

        private bool Connect()
        {
            try
            {
                Disconnect();
                _system = new DatalayerSystem();
                _system.Start(startBroker: false);

                string targetIp = "192.168.1.1"; // Buraya varsayılan IP'nizi yazın

                // Eğer UI thread'i müsaitse oradaki IP'yi al, yoksa varsayılanı kullan
                Dispatcher.Invoke(() => {
                    if (!string.IsNullOrEmpty(IpAddressInput.Text))
                        targetIp = IpAddressInput.Text;
                });

                var remote = new Remote(ip: targetIp, sslPort: 443).ToString();
                _client = _system.Factory.CreateClient(remote);

                if (_client.Ping().IsGood())
                {
                    _connectionHealthy = true;
                    Dispatcher.Invoke(() => BrowseNodes(""));
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        private void Disconnect()
        {
            _connectionHealthy = false;
            _client?.Dispose();
            _system?.Stop();
            _system?.Dispose();
            _client = null;
            _system = null;
        }

        private void BtnApplyIp_Click(object sender, RoutedEventArgs e)
        {
            _connectionHealthy = false;
            Task.Run(() =>
            {
                if (Connect()) SetConnectionState("Connected", Brushes.LimeGreen);
                else SetConnectionState("Failed", Brushes.Red);
            });
        }

        #endregion

        #region ANA DÖNGÜ (OKUMA VE GÖRSEL GÜNCELLEME)

        private async Task ReadLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Sadece bağlantı gerçekten "sağlıklı" ise okuma yap
                if (_connectionHealthy && _client != null)
                {
                    try
                    {
                        // 1. MEVCUT SEÇİLİ NODE OKUMA
                        if (!string.IsNullOrEmpty(_selectedMonitoringPath))
                        {
                            // Metodu aşağıda güncelledik
                            ReadSelectedNode();
                        }

                        // 2. BUTON DURUMLARINI GÜNCELLEME
                        UpdateControlButtonsState();
                    }
                    catch (Exception)
                    {
                        // Okuma hatası olursa bağlantıyı kontrol et
                        _connectionHealthy = _client.Ping().IsGood();
                    }
                }

                await Task.Delay(500, token); // 500ms yenileme hızı
            }
        }

        private void ReadSelectedNode()
        {
            // Client üzerinden path'i oku
            var readResult = _client.Read(_selectedMonitoringPath);

            if (readResult.Item1.IsGood())
            {
                // Item2 bize IVariant döndürür
                using (IVariant val = readResult.Item2)
                {
                    if (val == null) return;

                    // Daha önce düzelttiğimiz ProcessVariantValue metodunu çağırıyoruz
                    string displayValue = ProcessVariantValue(val);

                    // Veri tipini ayıkla
                    string fullType = val.DataType.ToString();
                    string shortType = fullType.Contains("_")
                                       ? fullType.Substring(fullType.LastIndexOf('_') + 1)
                                       : fullType;

                    // UI Güncelleme
                    Dispatcher.Invoke(() =>
                    {
                        ValueText.Text = displayValue;
                        ValueText.Foreground = Brushes.Black;
                        ResultStatusText.Text = $"Type: {shortType}";
                    });
                }
            }
            else
            {
                // Okuma başarısızsa UI'ı temizle veya hata göster
                Dispatcher.Invoke(() =>
                {
                    ValueText.Text = "---";
                    ValueText.Foreground = Brushes.Gray;
                });
            }
        }

        private void UpdateControlButtonsState()
        {
            UpdateButtonStyleSimplified(BtnStart, PATH_START, Colors.LimeGreen, Color.FromRgb(40, 167, 69));
            UpdateButtonStyleSimplified(BtnPause, PATH_PAUSE, Colors.Yellow, Color.FromRgb(255, 193, 7));
            UpdateButtonStyleSimplified(BtnAbort, PATH_ABORT, Colors.Red, Color.FromRgb(220, 53, 69));
            UpdateButtonStyleSimplified(BtnReset, PATH_RESET, Colors.Cyan, Color.FromRgb(108, 117, 125));
        }

        private void UpdateButtonStyleSimplified(Button btn, string path, Color activeColor, Color defaultColor)
        {
            var result = _client.Read(path);
            if (result.Item1.IsGood())
            {
                using (var val = result.Item2)
                {
                    bool isActive = val.ToBool();
                    Dispatcher.Invoke(() =>
                    {
                        if (isActive)
                        {
                            // 1. Arka planı daha canlı yap
                            btn.Background = new SolidColorBrush(activeColor);

                            // 2. Yazıyı kalınlaştır ve rengini belirginleştir
                            btn.FontWeight = FontWeights.Bold;
                            btn.Foreground = (activeColor == Colors.Yellow) ? Brushes.Black : Brushes.White;

                            // 3. Kalın bir kenarlık ekle
                            btn.BorderThickness = new Thickness(3);
                            btn.BorderBrush = Brushes.White;

                            // 4. Parlama (Glow) Efekti Ekle - Bu butonu "yanıyor" gibi gösterir
                            var glowEffect = new System.Windows.Media.Effects.DropShadowEffect
                            {
                                Color = activeColor,
                                BlurRadius = 20,      // Parlama genişliği
                                ShadowDepth = 0,      // Gölge yönü (0 = her yöne parlama)
                                Opacity = 0.8         // Parlama yoğunluğu
                            };
                            btn.Effect = glowEffect;
                        }
                        else
                        {
                            // Normale dön
                            btn.Background = new SolidColorBrush(defaultColor);
                            btn.FontWeight = FontWeights.Normal;
                            btn.Foreground = (defaultColor == Colors.Yellow) ? Brushes.Black : Brushes.White;
                            btn.BorderThickness = new Thickness(0);
                            btn.Effect = null; // Parlamayı kaldır
                        }
                    });
                }
            }
        }

        #endregion

        #region KONTROL BUTONLARI CLICK

        private void SetVariableTrue(string path)
        {
            if (_client == null || !_connectionHealthy) return;
            using (var val = new Variant(true))
            {
                _client.Write(path, val);
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e) => SetVariableTrue(PATH_START);
        private void BtnPause_Click(object sender, RoutedEventArgs e) => SetVariableTrue(PATH_PAUSE);
        private void BtnAbort_Click(object sender, RoutedEventArgs e) => SetVariableTrue(PATH_ABORT);
        private void BtnReset_Click(object sender, RoutedEventArgs e) => SetVariableTrue(PATH_RESET);

        #endregion

        #region BROWSER VE YAZMA MANTIĞI

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
                    NodeListBox.ItemsSource = nodes?.OrderBy(n => n).ToList() ?? new List<string>();
                }
            }
        }

        private void NodeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedName = NodeListBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedName) || selectedName.Contains("(")) return;

            _selectedMonitoringPath = string.IsNullOrEmpty(_currentBrowsingPath) ? selectedName : $"{_currentBrowsingPath}/{selectedName}";
            CurrentPathText.Text = _selectedMonitoringPath;
            UpdateWriteUI();
        }

        private void UpdateWriteUI()
        {
            var readResult = _client.Read(_selectedMonitoringPath);
            if (readResult.Item1.IsGood())
            {
                using (var val = readResult.Item2)
                {
                    _currentSelectedType = val.DataType;
                    WritePanel.Visibility = Visibility.Visible;
                    BoolWriteCombo.Visibility = val.IsBool ? Visibility.Visible : Visibility.Collapsed;
                    ValueWriteInput.Visibility = val.IsBool ? Visibility.Collapsed : Visibility.Visible;
                    if (val.IsBool) BoolWriteCombo.SelectedIndex = val.ToBool() ? 0 : 1;
                    else ValueWriteInput.Text = val.ToString();
                }
            }
        }

        private void BtnWrite_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null) return;
            try
            {
                Variant writeValue = null;
                if (_currentSelectedType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_BOOL8)
                    writeValue = new Variant(BoolWriteCombo.SelectedIndex == 0);
                else if (_currentSelectedType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_INT16)
                    writeValue = new Variant(short.Parse(ValueWriteInput.Text));
                else if (_currentSelectedType == DLR_VARIANT_TYPE.DLR_VARIANT_TYPE_FLOAT32)
                    writeValue = new Variant(float.Parse(ValueWriteInput.Text));
                else
                    writeValue = new Variant(ValueWriteInput.Text);

                using (writeValue)
                {
                    var res = _client.Write(_selectedMonitoringPath, writeValue);
                    if (res.IsBad()) WriteErrorText.Text = res.ToString();
                }
            }
            catch (Exception ex) { WriteErrorText.Text = ex.Message; }
        }

        private string ProcessVariantValue(IVariant val)
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

        #region YARDIMCI METOTLAR

        private async Task PingMonitorAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Eğer bağlantı yoksa otomatik olarak bağlanmayı dene
                if (_client == null || !_connectionHealthy)
                {
                    SetConnectionState("Auto-Connecting...", Brushes.Orange);
                    if (Connect())
                    {
                        SetConnectionState("Connected", Brushes.LimeGreen);
                    }
                }
                else
                {
                    // Bağlıysa pingle, koparsa temizle ki bir sonraki turda tekrar bağlansın
                    if (!_client.Ping().IsGood())
                    {
                        _connectionHealthy = false;
                        SetConnectionState("Disconnected", Brushes.Red);
                        Disconnect();
                    }
                }
                await Task.Delay(3000, token); // 3 saniyede bir kontrol et
            }
        }

        private void SetConnectionState(string text, Brush color)
        {
            Dispatcher.Invoke(() => { ConnectionStatusText.Text = text; ConnectionLed.Fill = color; });
        }

        private void NodeListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selectedName = NodeListBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedName) || selectedName.Contains("(")) return;
            string newPath = string.IsNullOrEmpty(_currentBrowsingPath) ? selectedName : $"{_currentBrowsingPath}/{selectedName}";
            BrowseNodes(newPath);
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentBrowsingPath)) return;
            var parts = _currentBrowsingPath.Split('/').ToList();
            if (parts.Count > 1) { parts.RemoveAt(parts.Count - 1); BrowseNodes(string.Join("/", parts)); }
            else BrowseNodes("");
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => BrowseNodes(_currentBrowsingPath);

        private void CtrlXCommunicator_Closed(object sender, EventArgs e)
        {
            _pingCts?.Cancel();
            _readCts?.Cancel();
            Disconnect();
        }
        #endregion
    }
}