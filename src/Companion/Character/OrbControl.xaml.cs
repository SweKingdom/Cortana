using System.Windows.Controls;

namespace Companion.Character;

public partial class OrbControl : UserControl
{
    internal OrbAnimator Animator { get; }

    public OrbControl()
    {
        InitializeComponent();
        var parts = OrbVisual.BuildParts(1.0);
        Host.Children.Add(parts.Root);
        Animator = new OrbAnimator(Host, parts);
    }
}
