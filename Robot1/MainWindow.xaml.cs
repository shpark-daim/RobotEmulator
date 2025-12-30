using MQTTnet;
using Rcp;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using RCP = Rcp.Rcp;

namespace Robot1;
public partial class MainWindow : Window {
    private IMqttClient? _mqttClient;
    private bool _reconciled = false;
    private int _seq = 0;
    // fixme : target (load robot from config)
    private readonly static string _home = "home";
    private const string _robotId = "r1";
    private readonly Dictionary<string, Point> _positions = [];

    private Timer? _statusReportTimer;
    private DateTime _lastStatusReportTime = DateTime.MinValue;
    private readonly Lock _statusLock = new();

    private bool _isProcessingQueue = false;
    private readonly Queue<MqttApplicationMessage> _messageQueue = new();

    // animation
    private readonly int _duration = 2000;
    private readonly int _verticalMovementLength = 10;
    private readonly int _centerOfProduct = 10;
    private DoubleAnimation? _currentAnimationX = null;
    private DoubleAnimation? _currentAnimationY = null;
    private DispatcherTimer? _positionUpdateTimer = null;
    private TaskCompletionSource<bool>? _currentMovingRobotArmTcs = null;
    private CancellationTokenSource _operationCts = new();

    // runtime
    private bool _isProcessing = false;
    private Point _currentRobotArmPosition;
    private RcpStatus<JsonElement?> _rcpStatus = new(
        Id: _robotId,
        Sequence: 0,
        EventSeq: 0,
        Mode: RcpMode.M,
        WorkingState: RcpWorkingState.I,
        ErrorCodes: [],
        CarrierPresent: false,
        CarrierIds: [
            new(
                DropoffId: "s5_0",
                BarcodeValue: ""
            ),
            new(
                DropoffId: "s5_1",
                BarcodeValue: ""
            ),
            new(
                DropoffId: "s3",
                BarcodeValue: ""
            ),
            new(
                DropoffId: "s4",
                BarcodeValue: ""
            ),
        ]
    );
    private readonly Dictionary<string, Canvas?> _positionProducts = new() {
        { "s5_1", null },
        { "s5_0", null },
        { "s3", null },
        { "s4", null }
    };

    public MainWindow() {
        InitializeComponent();
        _ = InitializePosition();
        SetupMqttClient();
        AutoButton.Click += async (sender, e) => await AutoButtonClicked(sender, e);
        ManualButton.Click += async (sender, e) => await ManualButtonClicked(sender, e);
        ErrorButton.Click += async (sender, e) => await ErrorButtonClicked(sender, e);
        InitializeButton.Click += async (sender, e) => await InitializeButtonClicked(sender, e);
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
            var syncCommand = JsonSerializer.Deserialize(message, RcpContext.Default.RcpSyncCommand);
            await HandleSyncCommand(syncCommand);
        } else if (topic.Contains("/cmd/pick")) {
            var cmd = JsonSerializer.Deserialize(message, RcpContext.Default.RcpPickCommand);
            if (_rcpStatus.Mode == RcpMode.A && !_isProcessing) await HandlePickCommand(cmd);
        } else if (topic.Contains("/cmd/place")) {
            var cmd = JsonSerializer.Deserialize(message, RcpContext.Default.RcpPlaceCommand);
            if (_rcpStatus.Mode == RcpMode.A && !_isProcessing) await HandlePlaceCommand(cmd);
        } else if (topic.Contains("/cmd/mode")) {
            var cmd = JsonSerializer.Deserialize(message, RcpContext.Default.RcpModeCommand);
            await HandleModeCommand(cmd);
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
            }
        } catch (Exception ex) {
            AddLog($"메시지 큐 처리 오류: {ex.Message}");
        } finally {
            _isProcessingQueue = false;
        }
    }

    protected override async void OnClosed(EventArgs e) {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
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
            await Dispatcher.InvokeAsync(() => AddLog($"Sync 명령 처리: Sequence={cmd.Sequence}"));
            if (cmd.Sequence > _rcpStatus.Sequence) {
                _rcpStatus = _rcpStatus with {
                    Sequence = cmd.Sequence,
                    EventSeq = cmd.Sequence
                };
                _reconciled = true;
            }
            await SendStatus();
        }
    }

    private async Task HandlePickCommand(RcpPickCommand? cmd) {
        if (cmd is { }) {
            if (cmd.RefSeq != _rcpStatus.EventSeq) {
                Dispatcher.Invoke(() => AddLog($"Ignore pick command: {cmd.RefSeq}, {_rcpStatus.Sequence}"));
                return;
            }
            try {
                _isProcessing = true;
                await Dispatcher.InvokeAsync(() => AddLog($"Pick 명령 처리: Pickup={cmd.PickupId}"));

                var to = cmd.PickupId;
                await MoveAnimation(to, _operationCts.Token);

                if (!_rcpStatus.CarrierPresent) {
                    AddProductToPosition(to, GenerateBoxId());

                    _rcpStatus = _rcpStatus with {
                        EventSeq = _rcpStatus.Sequence,
                        WorkingState = RcpWorkingState.K
                    };
                    await SendStatus();

                    await PickAnimation(to, _operationCts.Token);
                }
                Dispatcher.Invoke(() => AddLog($"{to}에서 pick 완료"));
                _rcpStatus = _rcpStatus with {
                    EventSeq = _rcpStatus.Sequence,
                    WorkingState = RcpWorkingState.I,
                    CarrierPresent = true
                };
                await SendStatus();
            } catch (OperationCanceledException) {
                await Dispatcher.InvokeAsync(() => AddLog("Pick 명령이 취소되었습니다."));
            } catch (Exception ex) {
                await Dispatcher.InvokeAsync(() => AddLog($"Pick 명령 오류: {ex.Message}"));
            } finally {
                _isProcessing = false;
            }
        }
    }

    private async Task HandlePlaceCommand(RcpPlaceCommand? cmd) {
        if (cmd is { }) {
            if (cmd.RefSeq != _rcpStatus.EventSeq) {
                Dispatcher.Invoke(() => AddLog($"Ignore place command: {cmd.RefSeq}, {_rcpStatus.Sequence}"));
                return;
            }
            try {
                _isProcessing = true;
                await Dispatcher.InvokeAsync(() => AddLog($"Place 명령 처리: Dropoff={cmd.DropoffId}"));

                var from = GetCurrentPosition(_currentRobotArmPosition);
                var to = cmd.DropoffId;
                await MoveAnimation(from, to, _operationCts.Token);

                if (_rcpStatus.CarrierPresent) {
                    _rcpStatus = _rcpStatus with {
                        EventSeq = _rcpStatus.Sequence,
                        WorkingState = RcpWorkingState.L
                    };
                    await SendStatus();

                    await PlaceAnimation(to, _operationCts.Token);
                }
                Dispatcher.Invoke(() => AddLog($"{to}에 place 완료"));
                _rcpStatus = _rcpStatus with {
                    EventSeq = _rcpStatus.Sequence,
                    WorkingState = RcpWorkingState.M,
                    CarrierPresent = false,
                    CarrierIds = [.. _rcpStatus.CarrierIds!.Select(c => {
                        var hasProduct = _positionProducts.TryGetValue(c.DropoffId, out var product) && product is { };
                        return c with { BarcodeValue = hasProduct ? "BoxTest" : "" };
                    })]
                };
                await SendStatus();

                // back to home
                await MoveAnimation(_home, _operationCts.Token);
                Dispatcher.Invoke(() => AddLog($"home 도착"));
                _rcpStatus = _rcpStatus with {
                    EventSeq = _rcpStatus.Sequence,
                    WorkingState = RcpWorkingState.I
                };
                await SendStatus();
            } catch (OperationCanceledException) {
                await Dispatcher.InvokeAsync(() => AddLog("Place 명령이 취소되었습니다."));
            } catch (Exception ex) {
                await Dispatcher.InvokeAsync(() => AddLog($"Place 명령 오류: {ex.Message}"));
            } finally {
                _isProcessing = false;
            }
        }

        string GetProductId(string to) {
            if (to == "s5_0") return TextBoxB.Text;
            if (to == "s5_1") return TextBoxA.Text;
            if (to == "s3") return TextBoxC.Text;
            if (to == "s4") return TextBoxD.Text;
            return "";
        }
    }

    private async Task HandleModeCommand(RcpModeCommand? cmd) {
        if (cmd is { }) {
            await Dispatcher.InvokeAsync(() => AddLog($"Mode 명령 처리: Mode={cmd.Mode}"));
            switch (cmd.Mode) {
            case RcpMode.M:
                if (_rcpStatus.ErrorCodes.Count == 0) await ChangeToManualMode();
                break;
            case RcpMode.A:
                if (_rcpStatus.Mode == RcpMode.M) await ChangeToAutoMode();
                break;
            case RcpMode.E:
                // todo?
                break;
            default:
                Dispatcher.Invoke(() => AddLog($"알 수 없는 모드: {cmd.Mode}"));
                break;
            }
        }
    }
    #endregion handle command

    #region change mode
    private async Task ChangeToManualMode() {
        await Dispatcher.InvokeAsync(() => {
            AddLog("Change to Manual Mode");
            Arm1.Stroke = new SolidColorBrush(Colors.Orange);
            Arm2.Stroke = new SolidColorBrush(Colors.Orange);
            GripperFork.Stroke = new SolidColorBrush(Colors.Orange);
            return Task.CompletedTask;
        });
        _rcpStatus = _rcpStatus with {
            Mode = RcpMode.M,
            EventSeq = _rcpStatus.Sequence,
            WorkingState = RcpWorkingState.I,
            ErrorCodes = []
        };
        await SendStatus();
    }

    private async Task ChangeToAutoMode() {
        await Dispatcher.InvokeAsync(() => {
            AddLog("Change to Auto Mode");
            Arm1.Stroke = new SolidColorBrush(Colors.SlateGray);
            Arm2.Stroke = new SolidColorBrush(Colors.SlateGray);
            GripperFork.Stroke = new SolidColorBrush(Colors.SlateGray);
        });
        _rcpStatus = _rcpStatus with {
            Mode = RcpMode.A,
            EventSeq = _rcpStatus.Sequence,
            WorkingState = RcpWorkingState.I,
        };
        await SendStatus();
        _isProcessing = false;
    }

    private async Task ChangeToErrorMode() {
        await Dispatcher.InvokeAsync(() => {
            AddLog("Change to Error Mode");
            Arm1.Stroke = new SolidColorBrush(Colors.Red);
            Arm2.Stroke = new SolidColorBrush(Colors.Red);
            GripperFork.Stroke = new SolidColorBrush(Colors.Red);
        });
        _rcpStatus = _rcpStatus with {
            Mode = RcpMode.E,
            EventSeq = _rcpStatus.Sequence,
            WorkingState = RcpWorkingState.I,
            ErrorCodes = [0, 1]
        };
        await SendStatus();
        _isProcessing = false;
    }
    #endregion change mode

    #region position
    private async Task InitializePosition() {
        var initialRobotPosition = new Point(Canvas.GetLeft(Arm2) + Arm2.X2, Canvas.GetTop(Arm2) + Arm2.Y2);
        _positions.TryAdd("home", new Point(initialRobotPosition.X, initialRobotPosition.Y));
        _positions.TryAdd("s5_0", new Point(Canvas.GetLeft(PositionB) + 20, Canvas.GetTop(PositionB)));
        _positions.TryAdd("s5_1", new Point(Canvas.GetLeft(PositionA) + 20, Canvas.GetTop(PositionA)));
        _positions.TryAdd("s3", new Point(Canvas.GetLeft(PositionC) + 20, Canvas.GetTop(PositionC)));
        _positions.TryAdd("s4", new Point(Canvas.GetLeft(PositionD) + 20, Canvas.GetTop(PositionD)));
        _currentRobotArmPosition = _positions["home"];
        await Task.Delay(0);
    }

    private void StartPositionUpdateTimer() {
        // 기존 타이머가 있다면 정지
        StopPositionUpdateTimer();

        _positionUpdateTimer = new DispatcherTimer {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _positionUpdateTimer.Tick += (s, e) => {
            UpdateCurrentPosition();
        };
        _positionUpdateTimer.Start();
    }

    private void StopPositionUpdateTimer() {
        if (_positionUpdateTimer != null) {
            _positionUpdateTimer.Stop();
            _positionUpdateTimer = null;
        }
    }

    private string GetCurrentPosition(Point point) {
        return _positions.FirstOrDefault(kvp => kvp.Value == point).Key;
    }

    private void UpdateCurrentPosition() {
        try {
            var currentLeft = Canvas.GetLeft(Arm2);
            var currentTop = Canvas.GetTop(Arm2);
            _currentRobotArmPosition = new Point(currentLeft, currentTop);
        } catch (Exception ex) {
            AddLog($"위치 업데이트 오류: {ex.Message}");
        }
    }

    private Point? GetPositionFromUI(string position) {
        return _positions.TryGetValue(position, out Point value) ? value : null;
    }

    #endregion position

    #region draw
    private async Task MoveAnimation(string to, CancellationToken ct = default) {
        var toPos = GetPositionFromUI(to);

        if (toPos == null) {
            Dispatcher.Invoke(() => AddLog($"알 수 없는 위치: {to}"));
            return;
        }

        if (_currentRobotArmPosition != toPos) {
            _isProcessing = true;
            Dispatcher.Invoke(() => AddLog($"로봇 팔 {to}로 이동 시작"));

            _rcpStatus = _rcpStatus with {
                EventSeq = _rcpStatus.Sequence,
                WorkingState = RcpWorkingState.M
            };
            await SendStatus();

            var armToPos = new Point(toPos.Value.X, toPos.Value.Y);
            var results = await MoveRobotArmAnimation(armToPos, ct);
        }

        Dispatcher.Invoke(() => AddLog($"로봇 팔 {to} 도착"));
    }

    private async Task MoveAnimation(string from, string to, CancellationToken ct = default) {
        var fromPos = GetPositionFromUI(from);
        var toPos = GetPositionFromUI(to);
        if (fromPos == null || toPos == null) {
            Dispatcher.Invoke(() => AddLog($"알 수 없는 위치: {from} 또는 {to}"));
            return;
        }
        if (_currentRobotArmPosition != toPos) {
            _isProcessing = true;
            Dispatcher.Invoke(() => AddLog($"로봇 팔 {from}에서 {to}로 이동 시작"));
            _rcpStatus = _rcpStatus with {
                EventSeq = _rcpStatus.Sequence,
                WorkingState = RcpWorkingState.M
            };
            await SendStatus();

            var armToPos = new Point(toPos.Value.X, toPos.Value.Y);
            var armTask = MoveRobotArmAnimation(armToPos, ct);
            var productTask = MoveProductAnimation(from, to, ct);
            var results = await Task.WhenAll(armTask, productTask);
        }
        Dispatcher.Invoke(() => AddLog($"로봇 팔 {to} 도착"));
    }

    private async Task PickAnimation(string to, CancellationToken ct = default) {
        var downPoint = new Point(_currentRobotArmPosition.X, _currentRobotArmPosition.Y + _verticalMovementLength);
        await MoveRobotArmAnimation(downPoint, ct);

        var upPoint = new Point(_currentRobotArmPosition.X, _currentRobotArmPosition.Y - _verticalMovementLength);
        var productTask = PickingUpAnimation(to, ct);
        var armTask = MoveRobotArmAnimation(upPoint, ct);
        await Task.WhenAll(armTask, productTask);
    }

    private Task<bool> PickingUpAnimation(string to, CancellationToken ct) {
        var product = _positionProducts[to]!;
        Dispatcher.Invoke(() => {
            if (ct.IsCancellationRequested) {
                return;
            }
            AddLog("물건 픽업 중...");

            var animationY = new DoubleAnimation {
                From = Canvas.GetTop(product),
                To = Canvas.GetTop(product) - _verticalMovementLength,
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            _currentAnimationY = animationY;

            animationY.Completed += (s, e) => {
                if (!ct.IsCancellationRequested) {
                    _currentAnimationY = null;
                }
            };

            product.BeginAnimation(Canvas.TopProperty, animationY);
        });
        return Task.FromResult(true);
    }

    private async Task PlaceAnimation(string to, CancellationToken ct = default) {
        var downPoint = new Point(_currentRobotArmPosition.X, _currentRobotArmPosition.Y + _verticalMovementLength);
        var productTask = PlacingAnimation(to, ct);
        var armTask = MoveRobotArmAnimation(downPoint, ct);
        await Task.WhenAll(armTask, productTask);

        var upPoint = new Point(_currentRobotArmPosition.X, _currentRobotArmPosition.Y - _verticalMovementLength);
        await MoveRobotArmAnimation(upPoint, ct);
    }

    private Task<bool> PlacingAnimation(string to, CancellationToken ct) {
        var product = _positionProducts[to]!;
        Dispatcher.Invoke(() => {
            if (ct.IsCancellationRequested) {
                return;
            }
            AddLog("물건 배치 중...");

            var animationY = new DoubleAnimation {
                From = Canvas.GetTop(product),
                To = Canvas.GetTop(product) + _verticalMovementLength,
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            _currentAnimationY = animationY;

            animationY.Completed += (s, e) => {
                if (!ct.IsCancellationRequested) {
                    _currentAnimationY = null;
                }
            };

            product.BeginAnimation(Canvas.TopProperty, animationY);
        });
        return Task.FromResult(true);
    }

    private Task<bool> MoveRobotArmAnimation(Point targetPos, CancellationToken ct) {
        var tcs = new TaskCompletionSource<bool>();
        _currentMovingRobotArmTcs = tcs;

        ct.Register(() => tcs.TrySetCanceled(ct));

        Dispatcher.InvokeAsync(() => {
            if (ct.IsCancellationRequested) {
                tcs.TrySetCanceled(ct);
                // registration.Dispose();
                return;
            }
            AddLog("Moving RobotArm...");

            double newX2 = targetPos.X - Canvas.GetLeft(Arm2); // targetPos.X - 190
            double newY2 = targetPos.Y - Canvas.GetTop(Arm2);  // targetPos.Y - 45

            // Arm2 끝점 애니메이션
            var arm2EndX = new DoubleAnimation {
                From = Arm2.X2,
                To = newX2,
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            var arm2EndY = new DoubleAnimation {
                From = Arm2.Y2,
                To = newY2,
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            // 그리퍼를 목표 위치로
            var gripperX = new DoubleAnimation {
                From = Canvas.GetLeft(Gripper),
                To = targetPos.X,
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            var gripperY = new DoubleAnimation {
                From = Canvas.GetTop(Gripper),
                To = targetPos.Y,
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            arm2EndX.Completed += (s, e) => {
                if (tcs == _currentMovingRobotArmTcs && !ct.IsCancellationRequested) {
                    tcs.TrySetResult(true);
                    _currentMovingRobotArmTcs = null;
                }
            };

            // 애니메이션 실행
            Arm2.BeginAnimation(Line.X2Property, arm2EndX);
            Arm2.BeginAnimation(Line.Y2Property, arm2EndY);
            Gripper.BeginAnimation(Canvas.LeftProperty, gripperX);
            Gripper.BeginAnimation(Canvas.TopProperty, gripperY);

        });
        _currentRobotArmPosition = targetPos;
        return tcs.Task;
    }

    private Task<bool> MoveProductAnimation(string from, string to, CancellationToken ct) {
        var toPos = GetPositionFromUI(to);
        if (toPos == null) {
            Dispatcher.Invoke(() => AddLog($"알 수 없는 위치: {to}"));
            return Task.FromResult(false);
        }

        var product = _positionProducts[from]!;
        Dispatcher.Invoke(() => {
            if (ct.IsCancellationRequested) {
                // registration.Dispose();
                return;
            }
            AddLog("Moving Product...");
            var animationX = new DoubleAnimation {
                From = Canvas.GetLeft(product),
                To = toPos.Value.X - 10, // Product 중심을 목표점에 맞춤
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            var animationY = new DoubleAnimation {
                From = Canvas.GetTop(product),
                To = toPos.Value.Y + _verticalMovementLength,
                Duration = TimeSpan.FromMilliseconds(_duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            _currentAnimationX = animationX;
            _currentAnimationY = animationY;

            // StartPositionUpdateTimer();

            animationX.Completed += (s, e) => {
                if (!ct.IsCancellationRequested) {
                    //_currentMovingRobotArmTcs = null;
                    _currentAnimationX = null;
                    _currentAnimationY = null;
                }
            };

            product.BeginAnimation(Canvas.LeftProperty, animationX);
            product.BeginAnimation(Canvas.TopProperty, animationY);
        });
        _positionProducts[from] = null;
        _positionProducts[to] = product;
        // status update
        return Task.FromResult(true);
    }

    private void StopAnimationAtCurrentPosition() {
        Dispatcher.Invoke(() => {
            try {
                var armCurLeft = Arm2.X2;
                var armCurTop = Arm2.Y2;
                var gripperCurLeft = Canvas.GetLeft(Gripper);
                var gripperCurTop = Canvas.GetTop(Gripper);
                Arm2.BeginAnimation(Line.X2Property, null);
                Arm2.BeginAnimation(Line.Y2Property, null);
                Gripper.BeginAnimation(Canvas.LeftProperty, null);
                Gripper.BeginAnimation(Canvas.TopProperty, null);
                Arm2.X2 = armCurLeft;
                Arm2.Y2 = armCurTop;
                Canvas.SetLeft(Gripper, gripperCurLeft);
                Canvas.SetTop(Gripper, gripperCurTop);


                _positionProducts.Values.ToList().ForEach(product => {
                    if (product != null) {
                        var productCurLeft = Canvas.GetLeft(product);
                        var productCurTop = Canvas.GetTop(product);
                        product.BeginAnimation(Canvas.LeftProperty, null);
                        product.BeginAnimation(Canvas.TopProperty, null);
                        Canvas.SetLeft(product, productCurLeft);
                        Canvas.SetTop(product, productCurTop);
                    }
                });

                if (_currentMovingRobotArmTcs != null && !_currentMovingRobotArmTcs.Task.IsCompleted) {
                    _currentMovingRobotArmTcs.TrySetResult(false); // 또는 SetCanceled()
                    _currentMovingRobotArmTcs = null;
                }

                _currentAnimationX = null;
                _currentAnimationY = null;
                _isProcessing = false;

            } catch (Exception ex) {
                AddLog($"애니메이션 정지 중 오류: {ex.Message}");

                _isProcessing = false;
                _currentMovingRobotArmTcs?.TrySetResult(false);
                _currentMovingRobotArmTcs = null;
                _currentAnimationX = null;
                _currentAnimationY = null;
            }
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
                    .WithPayload(JsonSerializer.Serialize(_rcpStatus, s_jsonOptions))
                    .Build();
        await QueueMessage(msg);
        if (_reconciled) _rcpStatus = _rcpStatus with { Sequence = _rcpStatus.Sequence + 1 };
    }
    #endregion status report

    #region robot mode buttons
    private async Task AutoButtonClicked(object sender, RoutedEventArgs e) {
        await Dispatcher.InvokeAsync(() => {
            AddLog("Auto 버튼 클릭 - Auto 모드로 전환");
        });
        await ChangeToAutoMode();
    }

    private async Task ManualButtonClicked(object sender, RoutedEventArgs e) {
        await Dispatcher.InvokeAsync(() => {
            AddLog("Manual 버튼 클릭 - Manual 모드로 전환");
        });
        await ChangeToManualMode();
    }

    private async Task ErrorButtonClicked(object sender, RoutedEventArgs e) {
        AddLog("Error 버튼 클릭 - Error 모드로 전환");

        _operationCts.Cancel();
        _operationCts.Dispose();

        StopAnimationAtCurrentPosition();
        await ChangeToErrorMode();
        _operationCts = new CancellationTokenSource();
    }

    private async Task InitializeButtonClicked(object sender, RoutedEventArgs e) {
        AddLog("Initialize 버튼 클릭 - 초기 모드로 전환");

        await MoveAnimation(_home, _operationCts.Token);
        _rcpStatus = _rcpStatus with {
            EventSeq = _rcpStatus.Sequence,
            WorkingState = RcpWorkingState.I
        };
        await SendStatus();
        await ChangeToManualMode();
        _operationCts = new CancellationTokenSource();
    }
    #endregion robot mode buttons

    #region box install remove buttons
    private void BtnAddA_Click(object sender, RoutedEventArgs e) {

        AddProductToPosition("s5_1", TextBoxA.Text);
    }

    private void BtnAddB_Click(object sender, RoutedEventArgs e) {
        AddProductToPosition("s5_0", TextBoxB.Text);
    }

    private void BtnAddC_Click(object sender, RoutedEventArgs e) {
        AddProductToPosition("s3", TextBoxC.Text);
    }

    private void BtnAddD_Click(object sender, RoutedEventArgs e) {
        AddProductToPosition("s4", TextBoxD.Text);
    }

    private void BtnRemoveA_Click(object sender, RoutedEventArgs e) {
        RemoveProductFromPosition("s5_1");
    }

    private void BtnRemoveB_Click(object sender, RoutedEventArgs e) {
        RemoveProductFromPosition("s5_0");
    }

    private void BtnRemoveC_Click(object sender, RoutedEventArgs e) {
        RemoveProductFromPosition("s3");
    }

    private void BtnRemoveD_Click(object sender, RoutedEventArgs e) {
        RemoveProductFromPosition("s4");
    }

    private Canvas? AddProductToPosition(string position, string productId) {
        if (string.IsNullOrWhiteSpace(productId)) {
            MessageBox.Show("Product ID를 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        // 이미 해당 위치에 Product가 있으면 제거
        if (_positionProducts[position] != null) {
            RemoveProductFromPosition(position);
        }

        Canvas product = CreateProductBox();
        var pos = GetPositionFromUI(position);
        if (pos.HasValue) {
            Dispatcher.Invoke(() => {
                Canvas.SetLeft(product, pos.Value.X - _centerOfProduct);
                Canvas.SetTop(product, pos.Value.Y + _verticalMovementLength * 2);
            });
        }

        // MoveCanvas에 추가
        Dispatcher.Invoke(() => {
            MoveCanvas.Children.Add(product);
        });
        _positionProducts[position] = product;
        var carrier = _rcpStatus.CarrierIds!.FirstOrDefault(c => c.DropoffId == position);
        if (carrier != null) {
            _rcpStatus = _rcpStatus with {
                CarrierIds = [.. _rcpStatus.CarrierIds!.Select(c => c.DropoffId == position ? c with { BarcodeValue = productId } : c)]
            };
        }
        return product;
    }

    private void RemoveProductFromPosition(string position) {
        if (_positionProducts[position] != null) {
            Dispatcher.Invoke(() => {
                MoveCanvas.Children.Remove(_positionProducts[position]);
            });
            _positionProducts[position] = null;
            var carrier = _rcpStatus.CarrierIds!.FirstOrDefault(c => c.DropoffId == position);
            if (carrier != null) {
                _rcpStatus = _rcpStatus with {
                    CarrierIds = [.. _rcpStatus.CarrierIds!.Select(c => c.DropoffId == position ? c with { BarcodeValue = "" } : c)]
                };
            }

        }
    }

    private Canvas CreateProductBox() {
        Canvas? product = null;
        Dispatcher.Invoke(() => {
            product = new Canvas();

            // 메인 박스 면
            Rectangle front = new() {
                Name = "Front",
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush(Colors.SandyBrown),
                Stroke = new SolidColorBrush(Colors.SaddleBrown),
                StrokeThickness = 1
            };

            // 상단 면 (3D 효과)
            Polygon upperSide = new() {
                Name = "UpperSide",
                Fill = new SolidColorBrush(Colors.BurlyWood),
                Stroke = new SolidColorBrush(Colors.SaddleBrown),
                StrokeThickness = 1,
                Points = new PointCollection { new Point(0, 0), new Point(5, -3), new Point(25, -3), new Point(20, 0) }
            };

            // 오른쪽 면 (3D 효과)
            Polygon rightSide = new() {
                Name = "RightSide",
                Fill = new SolidColorBrush(Colors.Tan),
                Stroke = new SolidColorBrush(Colors.SaddleBrown),
                StrokeThickness = 1,
                Points = new PointCollection { new Point(20, 0), new Point(25, -3), new Point(25, 17), new Point(20, 20) }
            };

            product.Children.Add(front);
            product.Children.Add(upperSide);
            product.Children.Add(rightSide);
        });
        return product!;
    }

    private static string GenerateBoxId() {
        var random = new Random();
        int randomNumber = random.Next(10, 100);
        return $"Box{randomNumber}";
    }
    #endregion box install remove buttons

    private void AddLog(string message) {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogText.Text += $"\n[{timestamp}] {message}";

        // 스크롤을 맨 아래로
        if (LogText.Parent is ScrollViewer scrollViewer) {
            scrollViewer.ScrollToEnd();
        }
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}