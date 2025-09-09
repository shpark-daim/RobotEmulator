using MQTTnet;
using Rcp;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using RCP = Rcp.Rcp;

namespace Robot1;
public partial class MainWindow : Window {
    private IMqttClient? _mqttClient;
    private Timer? _statusReportTimer;
    private bool _isMoving = false;
    private int _seq = 0;
    private RcpStatus<JsonElement?> _rcpStatus = new(
        Id: _robotId,
        Sequence: 0,
        EventSeq: 0,
        Mode: RcpMode.A,
        WorkingState: RcpWorkingState.I,
        ErrorCodes: [],
        CarrierPresent: false
    );
    // fixme : target (load robot from config)
    private const string _robotId = "r1";
    private DateTime _lastStatusReportTime = DateTime.MinValue;
    private readonly object _statusLock = new object();

    public MainWindow() {
        InitializeComponent();
        SetupMqttClient();
    }

    private void SetupMqttClient() {
        var mqttFactory = new MqttClientFactory();
        _mqttClient = mqttFactory.CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceived;
        _mqttClient.ConnectedAsync += async (e) => {
            Dispatcher.Invoke(() => {
                StatusText.Text = "연결됨";
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
                AddLog("MQTT 브로커에 연결되었습니다.");
            });

            var topic = RCP.MakeSubAllCmdTopic(_robotId);
            await _mqttClient.SubscribeAsync(topic);
            Dispatcher.Invoke(() => AddLog($"토픽 {topic} 구독 완료"));

            StartStatusReporting();
        };

        _mqttClient.DisconnectedAsync += async (e) => {
            Dispatcher.Invoke(() => {
                StatusText.Text = "연결 끊김";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                ConnectButton.Content = "MQTT 연결";
                AddLog("MQTT 브로커 연결이 끊어졌습니다.");
            });
            await StopStatusReporting();
        };
    }

    private void StartStatusReporting() {
        _statusReportTimer = new Timer(CheckAndReportStatus, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Dispatcher.Invoke(() => {
            AddLog("상태 보고 시작");
        });
    }

    private async void CheckAndReportStatus(object? state) {
        lock (_statusLock) {
            var timeSinceLastReport = DateTime.Now - _lastStatusReportTime;
            if (timeSinceLastReport.TotalSeconds < 2) return;
        }

        await SendStatus();
    }

    private async Task StopStatusReporting() {
        if (_statusReportTimer != null) {
            await _statusReportTimer.DisposeAsync();
            _statusReportTimer = null;
            AddLog("상태 보고 중지");
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) {
        try {
            if (_mqttClient == null) return;

            if (!_mqttClient.IsConnected) {
                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer("localhost", 1883) // MQTT 브로커 주소
                    .WithClientId("r1" + Guid.NewGuid().ToString()[..8])
                    .WithCleanSession()
                    .Build();

                await _mqttClient.ConnectAsync(options);
                ConnectButton.Content = "연결 해제";
                AddLog("MQTT 브로커 연결 시도 중...");
            } else {
                await _mqttClient.DisconnectAsync();
                StatusText.Text = "연결 안됨";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                ConnectButton.Content = "MQTT 연결";
                AddLog("MQTT 브로커 연결을 해제했습니다.");
            }
        } catch (Exception ex) {
            AddLog($"연결 오류: {ex.Message}");
            MessageBox.Show($"MQTT 연결 오류: {ex.Message}\n\nMQTT 브로커가 localhost:1883에서 실행 중인지 확인하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e) {
        var message = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

        Dispatcher.Invoke(() => AddLog($"명령 수신 : {_seq++}"));
        var topic = e.ApplicationMessage.Topic;

        if (topic.Contains("/cmd/sync")) {
            var syncCommand = JsonSerializer.Deserialize<RcpSyncCommand>(message);
            await HandleSyncCommand(syncCommand);
        } else if (topic.Contains("/cmd/transfer")) {
            var transferCommand = JsonSerializer.Deserialize<RcpTransferCommand>(message);
            await HandleTransferCommand(transferCommand);
        }
    }

    private async Task HandleSyncCommand(RcpSyncCommand? cmd) {
        if (cmd is { }) {
            await Dispatcher.InvokeAsync(() => AddLog($"Sync 명령 처리: Id={cmd.Id}, Sequence={cmd.Sequence}"));
            if (cmd.Sequence > _rcpStatus.Sequence) {
                _rcpStatus = _rcpStatus with { Sequence = cmd.Sequence - 1 };
            }
            await SendStatus();
        }
    }

    private async Task HandleTransferCommand(RcpTransferCommand? transferCommand) {
        if (transferCommand != null && !string.IsNullOrEmpty(transferCommand.Source) && !string.IsNullOrEmpty(transferCommand.Dest)) {
            (string from, string to) = Positioning(transferCommand);
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) {
                Dispatcher.Invoke(() => AddLog("알 수 없는 위치입니다. 명령을 무시합니다."));
            } else {
                Dispatcher.Invoke(() => AddLog($"이동 명령: {from} -> {to}"));
            }
            await Dispatcher.InvokeAsync(() => MoveRobot(from, to));
        } else { Dispatcher.Invoke(() => AddLog("잘못된 transfer 명령입니다. 명령을 무시합니다.")); }
    }

    #region transfer
    private static (string From, string To) Positioning(RcpTransferCommand cmd) {
        var from = "";
        var to = "";
        if (cmd.Source == "s5") {
            if (cmd.Pickupslot == 0) from = "A";
            if (cmd.Pickupslot == 1) from = "B";
            to = "C";
        }

        if (cmd.Source == "s4") {
            from = "D";
            if (cmd.Dest == "s5" && cmd.Dropoffslot == 0) to = "A";
            if (cmd.Dest == "s5" && cmd.Dropoffslot == 1) to = "B";
        }

        // fixme : 삭제
        if (cmd.Source == "s3") from = "C";
        if (cmd.Dest == "s4") to = "D";
        return (from, to);
    }

    private void MoveRobot(string from, string to) {
        if (_isMoving) {
            AddLog("로봇이 이동 중입니다. 명령을 무시합니다.");
            return;
        }

        if (from != to) {
            MoveRobotWithWaypoint(from, to);
        }
    }

    private async void MoveRobotWithWaypoint(string from, string to) {
        var fromPos = GetPositionFromUI(from);
        var toPos = GetPositionFromUI(to);

        if (fromPos == null || toPos == null) {
            AddLog($"알 수 없는 위치: {from} 또는 {to}");
            return;
        }

        _isMoving = true;

        // 현재 로봇 위치 확인
        var currentX = Canvas.GetLeft(Product);
        var currentY = Canvas.GetTop(Product);
        var currentPos = new Point(currentX + 10, currentY + 10); // 로봇 중심점

        // from으로 이동
        if (Math.Abs(currentPos.X - fromPos.Value.X) > 5 || Math.Abs(currentPos.Y - fromPos.Value.Y) > 5) {
            AddLog($"현재 위치에서 {from}으로 이동 시작");
            _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.M };
            await MoveRobotToPosition(fromPos.Value);


            AddLog($"{from} 도착");
            _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.K };
            // fixme
            if (!_rcpStatus.CarrierPresent) {
                await PickAnimation();
                AddLog($"{from}에서 pick 완료");
                _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.I, CarrierPresent = true };
            }
            //await Task.Delay(300); // 잠시 대기
            //_rcpStatus = _rcpStatus with { CarrierPresent = true };
        }

        // from에서 to로 이동
        AddLog($"{from}에서 {to}로 이동 시작");
        _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.M };
        await MoveRobotToPosition(toPos.Value);
        AddLog($"{to} 도착");

        _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.L };
        if (_rcpStatus.CarrierPresent) {
            await PlaceAnimation(); // 물건 내려놓기
            AddLog($"{to}에 place 완료");
            _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.I, CarrierPresent = false };
        }
        //await Task.Delay(300); // 잠시 대기
        //_rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.I, CarrierPresent = false };

        _isMoving = false;
    }

    private Task<bool> MoveRobotToPosition(Point targetPos) {
        var tcs = new TaskCompletionSource<bool>();

        // 로봇을 네모 중심으로 이동 (로봇 크기 20x20의 중심점 보정)
        var animationX = new DoubleAnimation {
            From = Canvas.GetLeft(Product),
            To = targetPos.X - 10, // 로봇 중심을 네모 중심에 맞추기
            Duration = TimeSpan.FromSeconds(2.5), // 각 구간 2.5초씩
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var animationY = new DoubleAnimation {
            From = Canvas.GetTop(Product),
            To = targetPos.Y - 10, // 로봇 중심을 네모 중심에 맞추기
            Duration = TimeSpan.FromSeconds(2.5),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        // 로봇 라벨도 함께 이동
        var labelAnimationX = new DoubleAnimation {
            From = Canvas.GetLeft(ProductLabel),
            To = targetPos.X + 15, // 라벨을 로봇 오른쪽에 배치
            Duration = TimeSpan.FromSeconds(2.5),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var labelAnimationY = new DoubleAnimation {
            From = Canvas.GetTop(ProductLabel),
            To = targetPos.Y - 10, // 라벨을 로봇과 같은 높이에
            Duration = TimeSpan.FromSeconds(2.5),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        // 애니메이션 완료 이벤트
        animationX.Completed += (s, e) => {
            tcs.SetResult(true);
        };

        // 애니메이션 시작
        Product.BeginAnimation(Canvas.LeftProperty, animationX);
        Product.BeginAnimation(Canvas.TopProperty, animationY);
        ProductLabel.BeginAnimation(Canvas.LeftProperty, labelAnimationX);
        ProductLabel.BeginAnimation(Canvas.TopProperty, labelAnimationY);

        return tcs.Task;
    }

    private Point? GetPositionFromUI(string position) {
        return position switch {
            "A" => new Point(Canvas.GetLeft(PositionA) + 20, Canvas.GetTop(PositionA) + 20), // 네모 중심 (40/2 = 20)
            "B" => new Point(Canvas.GetLeft(PositionB) + 20, Canvas.GetTop(PositionB) + 20), // 네모 중심
            "C" => new Point(Canvas.GetLeft(PositionC) + 20, MoveCanvas.ActualHeight - 40 + 20), // Canvas 높이에서 역산 후 중심
            "D" => new Point(Canvas.GetLeft(PositionD) + 20, MoveCanvas.ActualHeight - 40 + 20), // Canvas 높이에서 역산 후 중심
            _ => null
        };
    }

    private async Task PickAnimation() {
        await Dispatcher.InvokeAsync(async () => {
            AddLog("물건 픽업 중...");

            // 1. Product가 아래로 내려가는 애니메이션 (집는 동작)
            var downAnimation = new DoubleAnimation {
                From = Canvas.GetTop(Product),
                To = Canvas.GetTop(Product) + 15,
                Duration = TimeSpan.FromSeconds(1.0),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var downTask = new TaskCompletionSource<bool>();
            downAnimation.Completed += (s, e) => downTask.SetResult(true);

            Product.BeginAnimation(Canvas.TopProperty, downAnimation);
            await downTask.Task;

            // 2. 잠시 대기 (집는 시간)
            await Task.Delay(800);

            // 3. Product가 다시 위로 올라가는 애니메이션
            var upAnimation = new DoubleAnimation {
                From = Canvas.GetTop(Product),
                To = Canvas.GetTop(Product) - 15,
                Duration = TimeSpan.FromSeconds(1.0),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var upTask = new TaskCompletionSource<bool>();
            upAnimation.Completed += (s, e) => upTask.SetResult(true);

            Product.BeginAnimation(Canvas.TopProperty, upAnimation);
            await upTask.Task;
        });
    }

    private async Task PlaceAnimation() {
        await Dispatcher.InvokeAsync(async () => {
            AddLog("물건 배치 중...");

            // 1. Product가 아래로 내려가는 애니메이션 (놓는 동작)
            var downAnimation = new DoubleAnimation {
                From = Canvas.GetTop(Product),
                To = Canvas.GetTop(Product) + 15,
                Duration = TimeSpan.FromSeconds(1.0),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var downTask = new TaskCompletionSource<bool>();
            downAnimation.Completed += (s, e) => downTask.SetResult(true);

            Product.BeginAnimation(Canvas.TopProperty, downAnimation);
            await downTask.Task;

            // 2. 잠시 대기 (놓는 시간)
            await Task.Delay(1200);

            // 3. Product가 다시 위로 올라가는 애니메이션
            var upAnimation = new DoubleAnimation {
                From = Canvas.GetTop(Product),
                To = Canvas.GetTop(Product) - 15,
                Duration = TimeSpan.FromSeconds(1.0),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var upTask = new TaskCompletionSource<bool>();
            upAnimation.Completed += (s, e) => upTask.SetResult(true);

            Product.BeginAnimation(Canvas.TopProperty, upAnimation);
            await upTask.Task;
        });
    }
    #endregion transfer

    private async Task SendStatus() {
        if (_mqttClient?.IsConnected != true) return;
        // rcpStatus stale?
        var msg = new MqttApplicationMessageBuilder()
                    .WithTopic(RCP.MakeStatusTopic(_robotId))
                    .WithPayload(JsonSerializer.Serialize(_rcpStatus))
                    .Build();

        await _mqttClient.PublishAsync(msg);
        lock (_statusLock) {
            _lastStatusReportTime = DateTime.Now;
        }
        _rcpStatus = _rcpStatus with { Sequence = _rcpStatus.Sequence + 1 };
    }

    private void AddLog(string message) {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogText.Text += $"\n[{timestamp}] {message}";

        // 스크롤을 맨 아래로
        if (LogText.Parent is ScrollViewer scrollViewer) {
            scrollViewer.ScrollToEnd();
        }
    }

    protected override async void OnClosed(EventArgs e) {
        await StopStatusReporting();
        if (_mqttClient?.IsConnected == true) {
            await _mqttClient.DisconnectAsync();
        }
        _mqttClient?.Dispose();
        base.OnClosed(e);
    }
}