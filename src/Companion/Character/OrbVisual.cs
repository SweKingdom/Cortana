using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Companion.Character;

/// <summary>The brushes and glow of a built orb, so the animator can recolour it (thinking, step 2).</summary>
internal sealed record OrbParts(Grid Root, Ellipse Glow, RadialGradientBrush GlowBrush, RadialGradientBrush CoreBrush);

/// <summary>Stop colours of the orb gradients for one state. Glow stops: centre, mid, two transparent edge stops. Core: highlight, mid, edge.</summary>
internal sealed record OrbPalette(Color[] Glow, Color[] Core)
{
    public static readonly OrbPalette Idle = new(
        new[]
        {
            Color.FromArgb(0x90, 0x40, 0xA0, 0xFF), Color.FromArgb(0x50, 0x30, 0x80, 0xFF),
            Color.FromArgb(0x00, 0x10, 0x40, 0xFF), Color.FromArgb(0x00, 0x10, 0x40, 0xFF),
        },
        new[] { Colors.White, Color.FromRgb(0x9F, 0xD4, 0xFF), Color.FromRgb(0x2F, 0x7D, 0xFF) });

    /// <summary>Purple, for "thinking" (step 2: tint only; spinning particles come in step 3, D-051).</summary>
    public static readonly OrbPalette Thinking = new(
        new[]
        {
            Color.FromArgb(0x90, 0xA0, 0x70, 0xFF), Color.FromArgb(0x50, 0x80, 0x50, 0xE0),
            Color.FromArgb(0x00, 0x60, 0x30, 0xD0), Color.FromArgb(0x00, 0x60, 0x30, 0xD0),
        },
        new[] { Colors.White, Color.FromRgb(0xD6, 0xC2, 0xFF), Color.FromRgb(0x7A, 0x4D, 0xFF) });
}

/// <summary>The orb drawing (glow + core), sized by a scale factor. Single source for the look.</summary>
internal static class OrbVisual
{
    public const double Size = 168;       // window and control size (see OrbTuning.BreathAmplitude for the limit)
    public const double CoreSize = 104;
    public const double GlowSize = 152;   // ends exactly at the grab ring (D-049) so no click-swallowing band
    public const double RingSize = 152;   // CoreSize + 24 px each side

    /// <summary>Builds a Size x Size grid with the glow and core scaled by <paramref name="scale"/>.</summary>
    public static Grid Build(double scale) => BuildParts(scale).Root;

    public static OrbParts BuildParts(double scale)
    {
        var p = OrbPalette.Idle;
        var glowBrush = new RadialGradientBrush(new GradientStopCollection
        {
            new GradientStop(p.Glow[0], 0.0),
            new GradientStop(p.Glow[1], 0.45),
            // alpha reaches 0 at 92 % of the radius, so even at the top of the breath nothing outside the ring is visible
            new GradientStop(p.Glow[2], 0.92),
            new GradientStop(p.Glow[3], 1.0),
        });
        var coreBrush = new RadialGradientBrush(new GradientStopCollection
        {
            new GradientStop(p.Core[0], 0.0),
            new GradientStop(p.Core[1], 0.35),
            new GradientStop(p.Core[2], 1.0),
        }) { GradientOrigin = new Point(0.4, 0.35) };

        var glow = new Ellipse { Width = GlowSize * scale, Height = GlowSize * scale, Opacity = OrbTuning.GlowOpacityMin, Fill = glowBrush };
        var core = new Ellipse { Width = CoreSize * scale, Height = CoreSize * scale, Fill = coreBrush };
        var grid = new Grid { Width = Size, Height = Size, Background = null };
        grid.Children.Add(glow);
        grid.Children.Add(core);
        return new OrbParts(grid, glow, glowBrush, coreBrush);
    }

    /// <summary>Recolours the gradients in place (the stop count never changes). One re-render, no animation.</summary>
    public static void Apply(OrbParts parts, OrbPalette palette)
    {
        for (int i = 0; i < palette.Glow.Length; i++) parts.GlowBrush.GradientStops[i].Color = palette.Glow[i];
        for (int i = 0; i < palette.Core.Length; i++) parts.CoreBrush.GradientStops[i].Color = palette.Core[i];
    }
}
