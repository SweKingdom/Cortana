using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Companion.Character;

/// <summary>The orb drawing (glow + core), sized by a scale factor. Single source for the look.</summary>
internal static class OrbVisual
{
    public const double Size = 168;       // window and control size (see OrbTuning.BreathAmplitude for the limit)
    public const double CoreSize = 104;
    public const double GlowSize = 152;   // ends exactly at the grab ring (D-049) so no click-swallowing band
    public const double RingSize = 152;   // CoreSize + 24 px each side

    /// <summary>Builds a Size x Size grid with the glow and core scaled by <paramref name="scale"/>.</summary>
    public static Grid Build(double scale) => Build(scale, out _);

    public static Grid Build(double scale, out Ellipse glowEllipse)
    {
        var glow = new Ellipse
        {
            Width = GlowSize * scale, Height = GlowSize * scale, Opacity = OrbTuning.GlowOpacityMin,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x90, 0x40, 0xA0, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0x50, 0x30, 0x80, 0xFF), 0.45),
                // alpha reaches 0 at 92 % of the radius, so even at the top of the breath nothing outside the ring is visible
                new GradientStop(Color.FromArgb(0x00, 0x10, 0x40, 0xFF), 0.92),
                new GradientStop(Color.FromArgb(0x00, 0x10, 0x40, 0xFF), 1.0),
            }),
        };
        var core = new Ellipse
        {
            Width = CoreSize * scale, Height = CoreSize * scale,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Colors.White, 0.0),
                new GradientStop(Color.FromRgb(0x9F, 0xD4, 0xFF), 0.35),
                new GradientStop(Color.FromRgb(0x2F, 0x7D, 0xFF), 1.0),
            }) { GradientOrigin = new Point(0.4, 0.35) },
        };
        glowEllipse = glow;
        var grid = new Grid { Width = Size, Height = Size, Background = null };
        grid.Children.Add(glow);
        grid.Children.Add(core);
        return grid;
    }
}
