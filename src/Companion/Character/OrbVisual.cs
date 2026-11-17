using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Companion.Character;

/// <summary>The brushes and glow of a built orb, so the animator can recolour it (thinking, step 2).</summary>
internal sealed record OrbParts(Grid Root, Ellipse Glow, RadialGradientBrush GlowBrush, RadialGradientBrush CoreBrush, Canvas Particles);

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

    /// <summary>Light blue, for "talking" when the reply carries no expression.</summary>
    public static readonly OrbPalette Talking = Make(0x7F, 0xD0, 0xFF, 0x60, 0xB8, 0xFF, 0x30, 0x90, 0xFF, 0xC4, 0xE8, 0xFF, 0x4F, 0xA8, 0xFF);
    /// <summary>Turquoise: [happy].</summary>
    public static readonly OrbPalette Happy = Make(0x30, 0xE8, 0xCC, 0x20, 0xC8, 0xB0, 0x10, 0xA0, 0x90, 0xB0, 0xF5, 0xE8, 0x1F, 0xC8, 0xB0);
    /// <summary>Pink: [teasing].</summary>
    public static readonly OrbPalette Teasing = Make(0xFF, 0x70, 0xC0, 0xE8, 0x50, 0xA8, 0xC0, 0x30, 0x90, 0xFF, 0xC4, 0xE8, 0xFF, 0x4D, 0xB0);
    /// <summary>Warm pink: [flirty].</summary>
    public static readonly OrbPalette Flirty = Make(0xFF, 0x8C, 0xA8, 0xF0, 0x6A, 0x88, 0xD0, 0x40, 0x60, 0xFF, 0xD0, 0xD8, 0xFF, 0x6B, 0x8A);
    /// <summary>Orange: [concerned].</summary>
    public static readonly OrbPalette Concerned = Make(0xFF, 0xA8, 0x40, 0xE8, 0x88, 0x20, 0xC0, 0x60, 0x10, 0xFF, 0xDC, 0xA8, 0xFF, 0x8C, 0x1A);

    public static OrbPalette For(Expression e) => e switch
    {
        Expression.Happy => Happy, Expression.Teasing => Teasing, Expression.Flirty => Flirty, Expression.Concerned => Concerned, _ => Idle,
    };

    /// <summary>Glow centre / mid / edge (RGB) and core mid / edge (RGB); alphas follow the other palettes.</summary>
    private static OrbPalette Make(byte gr, byte gg, byte gb, byte mr, byte mg, byte mb, byte er, byte eg, byte eb,
        byte cr, byte cg, byte cb, byte dr, byte dg, byte db) => new(
        new[]
        {
            Color.FromArgb(0x90, gr, gg, gb), Color.FromArgb(0x50, mr, mg, mb),
            Color.FromArgb(0x00, er, eg, eb), Color.FromArgb(0x00, er, eg, eb),
        },
        new[] { Colors.White, Color.FromRgb(cr, cg, cb), Color.FromRgb(dr, dg, db) });
}

/// <summary>The orb drawing (glow + core + thinking particles), sized by a scale factor. Single source for the look.</summary>
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

        // Thinking particles: a few dots on a circle around the orb, inside the glow so nothing leaves the grab ring.
        // Hidden and not animated unless she is thinking (OrbAnimator), so they cost nothing the rest of the time.
        var particles = new Canvas
        {
            Width = Size, Height = Size, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0),
        };
        var dot = new SolidColorBrush(Color.FromArgb(0xD0, 0xF0, 0xE8, 0xFF)); dot.Freeze();
        for (int i = 0; i < OrbTuning.ParticleCount; i++)
        {
            double a = 2 * Math.PI * i / OrbTuning.ParticleCount;
            double size = OrbTuning.ParticleSize * (i % 2 == 0 ? 1.0 : 0.7);
            var e = new Ellipse { Width = size, Height = size, Fill = dot };
            Canvas.SetLeft(e, Size / 2 + OrbTuning.ParticleRadius * Math.Cos(a) * scale - size / 2);
            Canvas.SetTop(e, Size / 2 + OrbTuning.ParticleRadius * Math.Sin(a) * scale - size / 2);
            particles.Children.Add(e);
        }
        grid.Children.Add(particles);
        return new OrbParts(grid, glow, glowBrush, coreBrush, particles);
    }

    /// <summary>Recolours the gradients in place (the stop count never changes). One re-render, no animation.</summary>
    public static void Apply(OrbParts parts, OrbPalette palette)
    {
        for (int i = 0; i < palette.Glow.Length; i++) parts.GlowBrush.GradientStops[i].Color = palette.Glow[i];
        for (int i = 0; i < palette.Core.Length; i++) parts.CoreBrush.GradientStops[i].Color = palette.Core[i];
    }
}
