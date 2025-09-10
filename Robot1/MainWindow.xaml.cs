using MQTTnet;
using Rcp;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
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

    private string _home = "home";
    private Point _initialRobotBasePosition;
    private (string Id, Point Point) _currentRobotArmPosition;
    private bool _isProductVisible = false;
    private Dictionary<string, Point> _positions = [];
    private readonly Queue<MqttApplicationMessage> _messageQueue = new();
    private bool _isProcessingQueue = false;
    private int _duration = 5000;
    public MainWindow() {
        InitializeComponent();
        InitializePosition();
        SetupMqttClient();
    }

    #region mqtt
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
        } else if (topic.Contains("/cmd/pick")) {
            var cmd = JsonSerializer.Deserialize<RcpPickCommand>(message);
            await HandlePickCommand(cmd);
        } else if (topic.Contains("/cmd/place")) {
            var cmd = JsonSerializer.Deserialize<RcpPlaceCommand>(message);
            await HandlePlaceCommand(cmd);
        }
    }

    private async Task QueueMessage(MqttApplicationMessage msg) {
        _messageQueue.Enqueue(msg);

        if (!_isProcessingQueue) {
            await ProcessMessageQueue();
        }
    }

    private async Task ProcessMessageQueue() {
        _isProcessingQueue = true;

        try {
            while (_messageQueue.Count > 0) {
                var msg = _messageQueue.Dequeue();
                await _mqttClient!.PublishAsync(msg);
                lock (_statusLock) {
                    _lastStatusReportTime = DateTime.Now;
                }
                _rcpStatus = _rcpStatus with { Sequence = _rcpStatus.Sequence + 1 };
            }
        } catch (Exception ex) {
            AddLog($"메시지 큐 처리 오류: {ex.Message}");
        } finally {
            _isProcessingQueue = false;
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
    #endregion mqtt

    #region handle command
    private async Task HandleSyncCommand(RcpSyncCommand? cmd) {
        if (cmd is { }) {
            await Dispatcher.InvokeAsync(() => AddLog($"Sync 명령 처리: Id={cmd.Id}, Sequence={cmd.Sequence}"));
            if (cmd.Sequence > _rcpStatus.Sequence) {
                _rcpStatus = _rcpStatus with { Sequence = cmd.Sequence - 1 };
            }
            await SendStatus();
        }
    }

    private async Task HandlePickCommand(RcpPickCommand? cmd) {
        if (_isMoving) {
            Dispatcher.Invoke(() => AddLog("로봇이 이동 중입니다. 명령을 무시합니다."));

            return;
        } else if (cmd is { }) {
            await Dispatcher.InvokeAsync(() => AddLog($"Pick 명령 처리: Pickup={cmd.PickupId}"));

            var from = _home;
            var to = cmd.PickupId;
            await MoveRobotArm(from, to);

            _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.K };
            await SendStatus();
            if (!_rcpStatus.CarrierPresent) {
                await CreateProductAtPosition(to);
                await PickAnimation();
                Dispatcher.Invoke(() => AddLog($"{to}에서 pick 완료"));

                _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.I, CarrierPresent = true };
                await SendStatus();
            }
            _isMoving = false;
        }
    }

    private async Task HandlePlaceCommand(RcpPlaceCommand? cmd) {
        if (_isMoving) {
            Dispatcher.Invoke(() => AddLog("로봇이 이동 중입니다. 명령을 무시합니다."));

            return;
        } else if (cmd is { }) {
            await Dispatcher.InvokeAsync(() => AddLog($"Place 명령 처리: Dropoff={cmd.DropoffId}"));

            var from = _currentRobotArmPosition.Id;
            var to = cmd.DropoffId;
            await MoveRobotArmWithProduct(from, to);

            _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.L };
            await SendStatus();
            if (_rcpStatus.CarrierPresent) {
                await PlaceAnimation();
                await HideProduct();
                Dispatcher.Invoke(() => AddLog($"{to}에 place 완료"));

                _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.I, CarrierPresent = false };
                await SendStatus();
            }

            await MoveRobotArm(to, _home);
            _isMoving = false;
        }
    }

    #endregion handle command

    #region draw
    private async Task InitializePosition() {
        //_initialRobotBasePosition = new Point(Canvas.GetLeft(RobotArm), Canvas.GetTop(RobotArm));

        _positions.TryAdd("home", new Point(_initialRobotBasePosition.X + 10, _initialRobotBasePosition.Y + 10));
        _positions.TryAdd("s5_0", new Point(Canvas.GetLeft(PositionA) + 20, Canvas.GetTop(PositionA) + 20));
        _positions.TryAdd("s5_1", new Point(Canvas.GetLeft(PositionB) + 20, Canvas.GetTop(PositionB) + 20));
        _positions.TryAdd("s3", new Point(Canvas.GetLeft(PositionC) + 20, Canvas.GetTop(PositionC) + 20));
        _positions.TryAdd("s4", new Point(Canvas.GetLeft(PositionD) + 20, Canvas.GetTop(PositionD) + 20));

        _currentRobotArmPosition = (_home, _positions["home"]);

        await HideProduct();
    }

    private async Task MoveRobotArm(string from, string to) {
        var fromPos = GetPositionFromUI(from);
        var toPos = GetPositionFromUI(to);

        if (fromPos == null || toPos == null) {
            Dispatcher.Invoke(() => AddLog($"알 수 없는 위치: {from} 또는 {to}"));
            return;
        }

        _isMoving = true;
        Dispatcher.Invoke(() => AddLog($"로봇 팔 {from}에서 {to}로 이동 시작"));

        _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.M };
        await SendStatus();
        //await MoveRobotArmToPosition(toPos.Value);
        _currentRobotArmPosition = (to, toPos.Value);

        Dispatcher.Invoke(() => AddLog($"로봇 팔 {to} 도착"));
    }

    private async Task MoveRobotArmWithProduct(string from, string to) {
        var fromPos = GetPositionFromUI(from);
        var toPos = GetPositionFromUI(to);

        if (fromPos == null || toPos == null) {
            Dispatcher.Invoke(() => AddLog($"알 수 없는 위치: {from} 또는 {to}"));
            return;
        }

        _isMoving = true;
        Dispatcher.Invoke(() => AddLog($"로봇 팔과 Product {from}에서 {to}로 이동 시작"));
        _rcpStatus = _rcpStatus with { WorkingState = RcpWorkingState.M };
        await SendStatus();
        //var robotTask = MoveRobotArmToPosition(toPos.Value);
        var productTask = MoveProductToPosition(toPos.Value);

        await Task.WhenAll(/*robotTask, */productTask);
        _currentRobotArmPosition = (to, toPos.Value);

        Dispatcher.Invoke(() => AddLog($"로봇 팔과 Product {to} 도착"));
    }

    //private Task<bool> MoveRobotArmToPosition(Point targetPos) {
    //    var tcs = new TaskCompletionSource<bool>();
    //    Dispatcher.InvokeAsync(() => {
    //        var currentGripperPos = new Point(
    //        Canvas.GetLeft(RobotArm) + 80, // Gripper의 현재 X
    //        Canvas.GetTop(RobotArm) + 55   // Gripper의 현재 Y
    //    );

    //        var animationX = new DoubleAnimation {
    //            From = Canvas.GetLeft(RobotArm),
    //            To = targetPos.X - 80,
    //            Duration = TimeSpan.FromSeconds(2.5),
    //            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
    //        };

    //        var animationY = new DoubleAnimation {
    //            From = Canvas.GetTop(RobotArm),
    //            To = targetPos.Y - 55,
    //            Duration = TimeSpan.FromSeconds(2.5),
    //            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
    //        };

    //        animationX.Completed += (s, e) => {
    //            tcs.SetResult(true);
    //        };

    //        RobotArm.BeginAnimation(Canvas.LeftProperty, animationX);
    //        RobotArm.BeginAnimation(Canvas.TopProperty, animationY);
    //    });
    //    return tcs.Task;
    //}

    private Task<bool> MoveProductToPosition(Point targetPos) {
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.InvokeAsync(() => {
            var animationX = new DoubleAnimation {
                From = Canvas.GetLeft(Product),
                To = targetPos.X - 10, // Product 중심을 목표점에 맞춤
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            var animationY = new DoubleAnimation {
                From = Canvas.GetTop(Product),
                To = targetPos.Y - 10,
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            animationX.Completed += (s, e) => {
                tcs.SetResult(true);
            };

            Product.BeginAnimation(Canvas.LeftProperty, animationX);
            Product.BeginAnimation(Canvas.TopProperty, animationY);
        });
        return tcs.Task;
    }

    private async Task CreateProductAtPosition(string position) {
        var pos = GetPositionFromUI(position);
        if (pos.HasValue) {
            await Dispatcher.InvokeAsync(() => {
                Canvas.SetLeft(Product, pos.Value.X - 10);
                Canvas.SetTop(Product, pos.Value.Y - 10);
                Product.Visibility = Visibility.Visible;
                _isProductVisible = true;
            });
        }
    }

    private async Task HideProduct() {
        await Dispatcher.InvokeAsync(() => {
            Product.Visibility = Visibility.Hidden;
            _isProductVisible = false;
        });
    }

    // 현재 로봇 팔 위치를 문자열로 반환
    private string GetCurrentRobotArmPositionString() {
        // 현재 위치와 가장 가까운 등록된 위치 찾기
        foreach (var kvp in _positions) {
            var distance = Math.Sqrt(
                Math.Pow(_currentRobotArmPosition.Point.X - kvp.Value.X, 2) +
                Math.Pow(_currentRobotArmPosition.Point.Y - kvp.Value.Y, 2)
            );
            if (distance < 5) { // 5픽셀 오차 범위
                return kvp.Key;
            }
        }
        return $"({_currentRobotArmPosition.Point.X}, {_currentRobotArmPosition.Point.Y})";
    }

    private Point? GetPositionFromUI(string position) {
        return _positions.ContainsKey(position) ? _positions[position] : null;
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
                Duration = TimeSpan.FromMilliseconds(_duration),
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
                Duration = TimeSpan.FromMilliseconds(_duration),
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
    #endregion draw

    #region status report
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

    private async Task SendStatus() {
        if (_mqttClient?.IsConnected != true) return;
        // rcpStatus stale?
        var msg = new MqttApplicationMessageBuilder()
                    .WithTopic(RCP.MakeStatusTopic(_robotId))
                    .WithPayload(JsonSerializer.Serialize(_rcpStatus))
                    .Build();

        await QueueMessage(msg);
    }
    #endregion status report

    private void AddLog(string message) {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogText.Text += $"\n[{timestamp}] {message}";

        // 스크롤을 맨 아래로
        if (LogText.Parent is ScrollViewer scrollViewer) {
            scrollViewer.ScrollToEnd();
        }
    }
}