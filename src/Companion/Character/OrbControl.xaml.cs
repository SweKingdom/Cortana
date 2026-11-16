using System.Windows.Controls;

namespace Companion.Character;

public partial class OrbControl : UserControl
{
    public OrbAnimator Animator { get; }

    public OrbControl()
    {
        InitializeComponent();
        Host.Children.Add(OrbVisual.Build(1.0));
        Animator = new OrbAnimator(Host);
    }
}
