using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Robot2; 
public class RobotUIControls {
    public required Button ModeButton { get; set; }
    public required TextBox ModeText { get; set; }
    public required TextBox WorkingState { get; set; }
    public required TextBox CompletionReason { get; set; }
    public required TextBox JobId { get; set; }
    public required TextBox RecipeId { get; set; }
    public required TextBox Sequence { get; set; }
    public required TextBox EventSequence { get; set; }
    public required Rectangle ProgressBar { get; set; }
    public Storyboard? ProgressBarStoryboard { get; set; }
}
