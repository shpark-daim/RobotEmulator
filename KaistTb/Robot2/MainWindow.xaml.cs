using KaistRcp;
using System.Windows;

namespace Robot2 {
    public partial class MainWindow : Window {
        private MqttService? _mqttService;
        private readonly string[] _robotName = ["EQ1", "EQ2", "EQ3", "EQ4", "H1"];
        private readonly Dictionary<string, Robot> _robotWorkers = [];
        private readonly Dictionary<string, RobotPanel> _robotPanels = [];

        public MainWindow() {
            InitializeMqtt();
            InitializeComponent();
            _ = InitializeRobots();

            ConnectButton.Click += async (sender, e) => await ConnectButton_Click(sender, e);
            AutoButton.Click += async (sender, e) => await AutoButtonClicked(sender, e);
            ManualButton.Click += async (sender, e) => await ManualButtonClicked(sender, e);
        }

        #region mqtt
        private void InitializeMqtt() {
            _mqttService = new MqttService("localhost", 1883);
            _mqttService.ConnectionChanged += (s, isConnected) => {
                Dispatcher.Invoke(() => {
                    StatusText.Text = isConnected ? "연결됨" : "연결 안됨";
                    StatusText.Foreground = isConnected ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
                    ConnectButton.Content = isConnected ? "연결 해제" : "연결";
                });
            };
            _mqttService.CommandReceived += async (target, cmd) => await HandleCommands(target, cmd);
        }

        private async Task ConnectButton_Click(object sender, RoutedEventArgs e) {
            try {
                if (!_mqttService!.IsConnected)
                    await _mqttService.ConnectAsync();
                else
                    await _mqttService.DisconnectAsync();
            } catch (Exception) { }
        }
        #endregion mqtt

        #region handlecommand
        private async Task HandleCommands(string target, RcpCommand cmd) {
            await _robotWorkers[target].WriteChannel(cmd);
        }
        #endregion handlecommand

        #region handle buttons
        private async Task AutoButtonClicked(object sender, RoutedEventArgs e) {
            foreach (var worker in _robotWorkers.Values) {
                if (worker.Mode == RcpMode.A) continue;
                await worker.WriteChannel(new RcpAutoCommand());
            }
        }

        private async Task ManualButtonClicked(object sender, RoutedEventArgs e) {
            foreach (var worker in _robotWorkers.Values) {
                if (worker.Mode == RcpMode.M) continue;
                await worker.WriteChannel(new RcpManualCommand());
            }
        }

        private async void EQnModeButtonClicked(object sender, EventArgs e) {
            if (sender is RobotPanel panel)
                await ToggleRobotMode(panel.RobotName);
        }

        private void EQnProductResultChanged(object sender, bool isChecked) {
            if (sender is RobotPanel panel && _robotWorkers.TryGetValue(panel.RobotName, out var robot))
                robot.ProductResultOk = isChecked;
        }

        private void ChangeModeContent(string id, RcpMode mode) {
            Dispatcher.Invoke(() => {
                var panel = _robotPanels[id];
                panel.Mode = mode.ToString();
                if (mode == RcpMode.A) panel.SetAutoMode();
                else panel.SetManualMode();
            });
        }

        private void ChangeWorkingState(string id, RcpWorkingState workingState) {
            Dispatcher.Invoke(() => {
                var panel = _robotPanels[id];
                panel.WorkingState = workingState.ToString();
                if (workingState == RcpWorkingState.P) panel.PauseAnimation();
                if (workingState == RcpWorkingState.M) panel.ResumeAnimation();
                if (workingState == RcpWorkingState.S) panel.StopAnimation();
                if (workingState == RcpWorkingState.A) panel.StopAnimation();
            });
        }

        private void ChangeCompletionReason(string id, string? completionReason) {
            Dispatcher.Invoke(() => _robotPanels[id].CompletionReason = completionReason);
        }

        private void ChangeJobId(string id, string? jobId) {
            Dispatcher.Invoke(() => _robotPanels[id].JobId = jobId);
        }

        private void ChangeRecipe(string id, string? recipeId) {
            Dispatcher.Invoke(() => {
                _robotPanels[id].RecipeId = recipeId;
                if (recipeId is null) _robotPanels[id].StopAnimation();
                else _robotPanels[id].StartAnimation();
            });
        }

        private void ChangeSequence(string id, long sequence) {
            Dispatcher.Invoke(() => _robotPanels[id].Sequence = sequence.ToString());
        }

        private void ChangeEventSequence(string id, long eventSeq) {
            Dispatcher.Invoke(() => _robotPanels[id].EventSequence = eventSeq.ToString());
        }
        #endregion handle buttons

        #region etc
        private async Task InitializeRobots() {
            _robotPanels["EQ1"] = EQ1;
            _robotPanels["EQ2"] = EQ2;
            _robotPanels["EQ3"] = EQ3;
            _robotPanels["EQ4"] = EQ4;
            _robotPanels["H1"] = H1;

            foreach (var robot in _robotName) {
                var robotWorker = new Robot(robot, _mqttService!);
                robotWorker.ModeChanged += (s, e) => ChangeModeContent(e.Id, e.Mode);
                robotWorker.WorkingStateChanged += (s, e) => ChangeWorkingState(e.Id, e.WorkingState);
                robotWorker.CompletionReasonChanged += (s, e) => ChangeCompletionReason(e.Id, e.CompletionReason);
                robotWorker.JobIdChanged += (s, e) => ChangeJobId(e.Id, e.JobId);
                robotWorker.RecipeChanged += (s, e) => ChangeRecipe(e.Id, e.RecipeId);
                robotWorker.SequenceChanged += (s, e) => ChangeSequence(e.Id, e.Sequence);
                robotWorker.EventSequenceChanged += (s, e) => ChangeEventSequence(e.Id, e.EventSequence);
                _robotWorkers.Add(robot, robotWorker);
                robotWorker.RunWorkerAsync();

                _robotPanels[robot].AnimationCompleted += (_, _) => {
                    using var _ = robotWorker.WriteChannel(new RcpCompletedCommand());
                };
                await robotWorker.WriteChannel(new RcpStatusCommand());
            }
        }

        private async Task ToggleRobotMode(string robotId) {
            var robot = _robotWorkers[robotId];
            RcpCommand command = robot.Mode == RcpMode.A
                ? new RcpManualCommand()
                : new RcpAutoCommand();
            await robot.WriteChannel(command);
        }
        #endregion etc
    }
}
