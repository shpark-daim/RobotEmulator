using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Robot2;

public partial class RobotPanel : UserControl {
    public static readonly DependencyProperty RobotNameProperty =
        DependencyProperty.Register(nameof(RobotName), typeof(string), typeof(RobotPanel), new PropertyMetadata(""));
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(string), typeof(RobotPanel), new PropertyMetadata("M"));
    public static readonly DependencyProperty WorkingStateProperty =
        DependencyProperty.Register(nameof(WorkingState), typeof(string), typeof(RobotPanel), new PropertyMetadata("I"));
    public static readonly DependencyProperty CompletionReasonProperty =
        DependencyProperty.Register(nameof(CompletionReason), typeof(string), typeof(RobotPanel), new PropertyMetadata(null));
    public static readonly DependencyProperty JobIdProperty =
        DependencyProperty.Register(nameof(JobId), typeof(string), typeof(RobotPanel), new PropertyMetadata(null));
    public static readonly DependencyProperty RecipeIdProperty =
        DependencyProperty.Register(nameof(RecipeId), typeof(string), typeof(RobotPanel), new PropertyMetadata(null));
    public static readonly DependencyProperty SequenceProperty =
        DependencyProperty.Register(nameof(Sequence), typeof(string), typeof(RobotPanel), new PropertyMetadata("0"));
    public static readonly DependencyProperty EventSequenceProperty =
        DependencyProperty.Register(nameof(EventSequence), typeof(string), typeof(RobotPanel), new PropertyMetadata("0"));

    public string RobotName { get => (string)GetValue(RobotNameProperty); set => SetValue(RobotNameProperty, value); }
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public string WorkingState { get => (string)GetValue(WorkingStateProperty); set => SetValue(WorkingStateProperty, value); }
    public string? CompletionReason { get => (string?)GetValue(CompletionReasonProperty); set => SetValue(CompletionReasonProperty, value); }
    public string? JobId { get => (string?)GetValue(JobIdProperty); set => SetValue(JobIdProperty, value); }
    public string? RecipeId { get => (string?)GetValue(RecipeIdProperty); set => SetValue(RecipeIdProperty, value); }
    public string Sequence { get => (string)GetValue(SequenceProperty); set => SetValue(SequenceProperty, value); }
    public string EventSequence { get => (string)GetValue(EventSequenceProperty); set => SetValue(EventSequenceProperty, value); }

    public event EventHandler? ModeButtonClicked;
    public event EventHandler<bool>? ProductResultChanged;
    public event EventHandler? AnimationCompleted;

    private Storyboard? _storyboard;

    public RobotPanel() {
        InitializeComponent();
        Loaded += (_, _) => InitializeAnimation();
    }

    private void InitializeAnimation() {
        var animation = new DoubleAnimation {
            From = 0,
            To = 100,
            Duration = TimeSpan.FromSeconds(30),
            EasingFunction = new QuadraticEase()
        };
        _storyboard = new Storyboard();
        _storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, ProgressBarRect);
        Storyboard.SetTargetProperty(animation, new PropertyPath(Rectangle.WidthProperty));
        _storyboard.Completed += (_, _) => AnimationCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void StartAnimation() => _storyboard?.Begin();
    public void PauseAnimation() => _storyboard?.Pause();
    public void ResumeAnimation() => _storyboard?.Resume();
    public void StopAnimation() => _storyboard?.Stop();

    public void SetAutoMode() {
        ModeButton.Content = "M";
        ModeButton.Background = Brushes.LightYellow;
        ModeButton.BorderBrush = Brushes.Orange;
    }

    public void SetManualMode() {
        ModeButton.Content = "A";
        ModeButton.Background = Brushes.LightBlue;
        ModeButton.BorderBrush = Brushes.SlateGray;
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
        => ModeButtonClicked?.Invoke(this, e);

    private void ProductResult_Click(object sender, RoutedEventArgs e)
        => ProductResultChanged?.Invoke(this, ProductResultCheckBox.IsChecked ?? true);
}
