using MQTTnet;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Robot2 {
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window {
        private IMqttClient? _mqttClient;
        public MainWindow() {
            InitializeComponent();
            // SetupMqttClient();
            StartFillAnimation(ProgressBarEQ3, 10); // 10초
            StartFillAnimation(ProgressBarEQ1, 5);  // 5초
            StartFillAnimation(ProgressBarEQ2, 7);  // 7초

            //ResetFillAnimation(ProgressBarEQ3);
        }

        //#region mqtt
        //private void SetupMqttClient() {
        //    var mqttFactory = new MqttClientFactory();
        //    _mqttClient = mqttFactory.CreateMqttClient();

        //    _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceived;
        //    _mqttClient.ConnectedAsync += async (e) => {
        //        Dispatcher.Invoke(() => {
        //            StatusText.Text = "연결됨";
        //            StatusText.Foreground = System.Windows.Media.Brushes.Green;
        //        });

        //        var topic = RCP.MakeSubAllCmdTopic(_robotId);
        //        await _mqttClient.SubscribeAsync(topic);

        //        //StartStatusReporting();
        //    };

        //    _mqttClient.DisconnectedAsync += async (e) => {
        //        Dispatcher.Invoke(() => {
        //            StatusText.Text = "연결 끊김";
        //            StatusText.Foreground = System.Windows.Media.Brushes.Red;
        //            ConnectButton.Content = "MQTT 연결";
        //        });
        //        await StopStatusReporting();
        //    };
        //}

        //private async void ConnectButton_Click(object sender, RoutedEventArgs e) {
        //    try {
        //        if (_mqttClient == null) return;

        //        if (!_mqttClient.IsConnected) {
        //            var options = new MqttClientOptionsBuilder()
        //                .WithTcpServer("localhost", 1883) // MQTT 브로커 주소
        //                .WithClientId("r1" + Guid.NewGuid().ToString()[..8])
        //                .WithCleanSession()
        //                .Build();

        //            await _mqttClient.ConnectAsync(options);
        //            ConnectButton.Content = "연결 해제";
        //        } else {
        //            await _mqttClient.DisconnectAsync();
        //            StatusText.Text = "연결 안됨";
        //            StatusText.Foreground = System.Windows.Media.Brushes.Red;
        //            ConnectButton.Content = "MQTT 연결";
        //        }
        //    } catch (Exception ex) {
        //        MessageBox.Show($"MQTT 연결 오류: {ex.Message}\n\nMQTT 브로커가 localhost:1883에서 실행 중인지 확인하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //}

        //private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e) {
        //    var message = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

        //    Dispatcher.Invoke(() => AddLog($"명령 수신 : {_seq++}"));
        //    var topic = e.ApplicationMessage.Topic;
        //    if (topic.Contains("/cmd/sync")) {
        //        var syncCommand = JsonSerializer.Deserialize(message, RcpContext.Default.RcpSyncCommand);
        //        await HandleSyncCommand(syncCommand);
        //    } else if (topic.Contains("/cmd/pick")) {
        //        var cmd = JsonSerializer.Deserialize(message, RcpContext.Default.RcpPickCommand);
        //        if (_rcpStatus.Mode == RcpMode.A && !_isProcessing) await HandlePickCommand(cmd);
        //    } else if (topic.Contains("/cmd/place")) {
        //        var cmd = JsonSerializer.Deserialize(message, RcpContext.Default.RcpPlaceCommand);
        //        if (_rcpStatus.Mode == RcpMode.A && !_isProcessing) await HandlePlaceCommand(cmd);
        //    } else if (topic.Contains("/cmd/auto")) {
        //        var cmd = JsonSerializer.Deserialize(message, RcpContext.Default.RcpAutoCommand);
        //        await HandleAutoCommand(cmd);
        //    } else if (topic.Contains("/cmd/manual")) {
        //        var cmd = JsonSerializer.Deserialize(message, RcpContext.Default.RcpManualCommand);
        //        await HandleManualCommand(cmd);
        //    } else if (topic.Contains("/cmd/status")) {
        //        var cmd = JsonSerializer.Deserialize(message, RcpContext.Default.RcpStatusCommand);
        //        await HandleStatusCommand(cmd);
        //    }
        //}

        //private async Task QueueMessage(MqttApplicationMessage msg) {
        //    _messageQueue.Enqueue(msg);

        //    if (!_isProcessingQueue) {
        //        await ProcessMessageQueue();
        //    }
        //}

        //private async Task ProcessMessageQueue() {
        //    _isProcessingQueue = true;

        //    try {
        //        while (_messageQueue.Count > 0) {
        //            var msg = _messageQueue.Dequeue();
        //            await _mqttClient!.PublishAsync(msg);
        //            lock (_statusLock) {
        //                _lastStatusReportTime = DateTime.Now;
        //            }
        //        }
        //    } catch (Exception ex) {
        //    } finally {
        //        _isProcessingQueue = false;
        //    }
        //}

        //protected override async void OnClosed(EventArgs e) {
        //    _operationCts?.Cancel();
        //    _operationCts?.Dispose();
        //    await StopStatusReporting();
        //    if (_mqttClient?.IsConnected == true) {
        //        await _mqttClient.DisconnectAsync();
        //    }
        //    _mqttClient?.Dispose();
        //    base.OnClosed(e);
        //}
        //#endregion mqtt

        #region handlecommand
        #endregion handlecommand

        #region animation
        private void StartFillAnimation(Rectangle progressBar, double durationSeconds) {
            var animation = new DoubleAnimation {
                From = 0,
                To = 100,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                EasingFunction = new QuadraticEase()
            };
            progressBar.BeginAnimation(Rectangle.WidthProperty, animation);
        }

        private void ResetFillAnimation(Rectangle progressBar) {
            progressBar.BeginAnimation(Rectangle.WidthProperty, null); // 애니메이션 제거
            progressBar.Width = 0; // 초기값으로
        }
        #endregion animation

        #region etc
        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
        #endregion etc
    }
}
