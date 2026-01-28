using KaistRcp;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Robot2 {
    public partial class MainWindow : Window {
        private MqttService? _mqttService;
        private readonly string[] _robotName = { "EQ1", "EQ2", "EQ3" };
        private Dictionary<string, Robot> _robotWorkers = [];
        private Dictionary<string, RobotUIControls> _robotControls = [];
        public MainWindow() {
            InitializeMqtt();
            InitializeComponent();
            _ = InitializeRobots();

            ConnectButton.Click += async (sender, e) => await ConnectButton_Click(sender, e);
            AutoButton.Click += async (sender, e) => await AutoButtonClicked(sender, e);
            ManualButton.Click += async (sender, e) => await ManualButtonClicked(sender, e);
            EQ2_ModeButton.Click += async (sender, e) => await EQ2ModeButtonClicked();
            EQ3_ModeButton.Click += async (sender, e) => await EQ3ModeButtonClicked();
            EQ1_ModeButton.Click += async (sender, e) => await EQ1ModeButtonClicked();
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
            _mqttService.CommandReceived += async (target, cmd) => {
                await HandleCommands(target, cmd);
            };
        }

        private async Task ConnectButton_Click(object sender, RoutedEventArgs e) {
            try {
                if (!_mqttService!.IsConnected) {
                    await _mqttService.ConnectAsync();
                } else {
                    await _mqttService.DisconnectAsync();
                }
            } catch (Exception) {
            }
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

        private async Task EQ2ModeButtonClicked() {
            await ToggleRobotMode("EQ2");
        }

        private async Task EQ3ModeButtonClicked() {
            await ToggleRobotMode("EQ3");
        }

        private async Task EQ1ModeButtonClicked() {
            await ToggleRobotMode("EQ1");
        }

        private void ChangeModeContent(string id, RcpMode mode) {
            var isAuto = mode == RcpMode.A;
            UpdateRobotUI(id, controls => {
                controls.ModeButton.Content = isAuto ? "M" : "A";
                controls.ModeButton.Background = isAuto
                    ? System.Windows.Media.Brushes.LightYellow
                    : System.Windows.Media.Brushes.LightBlue;
                controls.ModeButton.BorderBrush = isAuto
                    ? System.Windows.Media.Brushes.Orange
                    : System.Windows.Media.Brushes.SlateGray;
                controls.ModeText.Text = mode.ToString();
            });
        }

        private void ChangeWorkingState(string id, RcpWorkingState workingState) {
            UpdateRobotUI(id, controls => controls.WorkingState.Text = workingState.ToString());
            if (_robotControls.TryGetValue(id, out var controls)) {
                if (workingState == RcpWorkingState.P) PauseAnimation(id);
                if (workingState == RcpWorkingState.M) ResumeAnimation(id);
                if (workingState == RcpWorkingState.A) StopAnimation(id);
            }
        }

        private void ChangeCompletionReason(string id, string? completionReason) {
            UpdateRobotUI(id, controls => controls.CompletionReason.Text = completionReason);
        }

        private void ChangeJobId(string id, string? jobId) {
            UpdateRobotUI(id, controls => controls.JobId.Text = jobId);
        }

        private void ChangeRecipe(string id, string? recipeId) {
            UpdateRobotUI(id, controls => controls.RecipeId.Text = recipeId);

            if (_robotControls.TryGetValue(id, out var controls)) {
                if (recipeId is null) {
                    StopAnimation(id);
                    //ResetFillAnimation(controls.ProgressBar);
                } else {
                    StartAnimation(id);
                }
            }
        }

        private void ChangeSequence(string id, long sequence) {
            UpdateRobotUI(id, controls => { controls.Sequence.Text = sequence.ToString(); });
        }

        private void ChangeEventSequence(string id, long eventSeq) {
            UpdateRobotUI(id, controls => { controls.EventSequence.Text = eventSeq.ToString(); });
        }
        #endregion handle buttons

        #region animation
        private void StartAnimation(string target) {
            _robotControls[target].ProgressBarStoryboard!.Begin();
        }

        private void PauseAnimation(string target) {
            _robotControls[target].ProgressBarStoryboard!.Pause();
        }

        private void ResumeAnimation(string target) {
            _robotControls[target].ProgressBarStoryboard!.Resume();
        }

        private void StopAnimation(string target) {
            _robotControls[target].ProgressBarStoryboard!.Stop();
        }
        #endregion animation

        #region etc
        private async Task InitializeRobots() {
            _robotControls.TryAdd("EQ1",
                new RobotUIControls {
                    ModeButton = EQ1_ModeButton,
                    ModeText = EQ1_Mode,
                    WorkingState = EQ1_WorkingState,
                    CompletionReason = EQ1_CompletionReason,
                    JobId = EQ1_JobId,
                    RecipeId = EQ1_RecipeId,
                    Sequence = EQ1_Seq,
                    EventSequence = EQ1_EventSeq,
                    ProgressBar = ProgressBarEQ1
                }
                );
            _robotControls.TryAdd("EQ2",
                new RobotUIControls {
                    ModeButton = EQ2_ModeButton,
                    ModeText = EQ2_Mode,
                    WorkingState = EQ2_WorkingState,
                    CompletionReason = EQ2_CompletionReason,
                    JobId = EQ2_JobId,
                    RecipeId = EQ2_RecipeId,
                    Sequence = EQ2_Seq,
                    EventSequence = EQ2_EventSeq,
                    ProgressBar = ProgressBarEQ2
                }
                );
            _robotControls.TryAdd("EQ3",
                new RobotUIControls {
                    ModeButton = EQ3_ModeButton,
                    ModeText = EQ3_Mode,
                    WorkingState = EQ3_WorkingState,
                    CompletionReason = EQ3_CompletionReason,
                    JobId = EQ3_JobId,
                    RecipeId = EQ3_RecipeId,
                    Sequence = EQ3_Seq,
                    EventSequence = EQ3_EventSeq,
                    ProgressBar = ProgressBarEQ3
                }
                );

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

                CreateAnimationForProgressBar(_robotControls[robot].ProgressBar, robot);
                await robotWorker.WriteChannel(new RcpStatusCommand());
            }
        }

        private void CreateAnimationForProgressBar(Rectangle progressBar, string target) {
            var animation = new DoubleAnimation {
                From = 0,
                To = 100,
                Duration = TimeSpan.FromSeconds(30),
                EasingFunction = new QuadraticEase()
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, progressBar);
            Storyboard.SetTargetProperty(animation, new PropertyPath(Rectangle.WidthProperty));
            storyboard.Completed += (s, e) => {
                using var _ = _robotWorkers[target].WriteChannel(new RcpCompletedCommand());
            };
            _robotControls[target].ProgressBarStoryboard = storyboard;
        }

        private async Task ToggleRobotMode(string robotId) {
            var robot = _robotWorkers[robotId];
            RcpCommand command = robot.Mode == RcpMode.A
                ? new RcpManualCommand()
                : new RcpAutoCommand();

            await robot.WriteChannel(command);
        }

        private void UpdateRobotUI(string id, Action<RobotUIControls> updateAction) {
            if (!_robotControls.TryGetValue(id, out var controls)) return;

            Dispatcher.Invoke(() => updateAction(controls));
        }

        #endregion etc
    }
}
