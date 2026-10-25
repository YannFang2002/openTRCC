using System.Text.Json.Serialization;

namespace OpenTRCC.Core.Themes;

/// <summary>A 320x240 theme document. JSON is the source of truth; see <see cref="ThemeJson"/>.</summary>
public sealed record ThemeDocument
{
    public int SchemaVersion { get; init; } = 1;

    public string Id { get; init; } = "untitled";

    public string Name { get; init; } = "Untitled";

    public ThemeCanvas Canvas { get; init; } = new();

    public IReadOnlyList<Layer> Layers { get; init; } = Array.Empty<Layer>();
}

public sealed record ThemeCanvas
{
    public int Width { get; init; } = 320;

    public int Height { get; init; } = 240;

    public ThemeBackground Background { get; init; } = new();
}

public enum BackgroundKind
{
    Solid = 0,
    Gradient = 1,
    Image = 2,

    /// <summary>Animated, generated at runtime (aurora / waves / particles / stars / grid).</summary>
    Procedural = 3,
}

public sealed record ThemeBackground
{
    public BackgroundKind Kind { get; init; } = BackgroundKind.Solid;

    /// <summary>One colour for solid, two or more for a gradient.</summary>
    public IReadOnlyList<string> Colors { get; init; } = new[] { "#0B1020" };

    /// <summary>Gradient direction in degrees, 0 = left to right, 90 = top to bottom.</summary>
    public double Angle { get; init; } = 135;

    public string? Asset { get; init; }

    public string Fit { get; init; } = "cover";

    public double Dim { get; init; }

    public double Blur { get; init; }

    /// <summary>Procedural preset: aurora | waves | particles | stars | grid.</summary>
    public string Preset { get; init; } = "aurora";

    /// <summary>Animation speed multiplier for procedural backgrounds.</summary>
    public double Speed { get; init; } = 1;

    /// <summary>Extra zoom for image/GIF/video backgrounds (1 = fit, &gt;1 crops in).</summary>
    public double Zoom { get; init; } = 1;

    /// <summary>Horizontal offset in canvas pixels (positive moves right).</summary>
    public double OffsetX { get; init; }

    /// <summary>Vertical offset in canvas pixels (positive moves down).</summary>
    public double OffsetY { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextLayer), "text")]
[JsonDerivedType(typeof(BarLayer), "bar")]
[JsonDerivedType(typeof(RingLayer), "ring")]
[JsonDerivedType(typeof(SparklineLayer), "sparkline")]
[JsonDerivedType(typeof(ImageLayer), "image")]
[JsonDerivedType(typeof(RectLayer), "rect")]
public abstract record Layer
{
    public string Id { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double W { get; init; }

    public double H { get; init; }

    public bool Visible { get; init; } = true;

    public double Opacity { get; init; } = 1;
}

public sealed record TextLayer : Layer
{
    /// <summary>Text with <c>{sensor.id:format}</c> tokens.</summary>
    public string Text { get; init; } = string.Empty;

    public FontSpec Font { get; init; } = new();

    public string Color { get; init; } = "#FFFFFF";

    /// <summary>left | center | right.</summary>
    public string Align { get; init; } = "left";

    /// <summary>top | middle | bottom.</summary>
    public string VerticalAlign { get; init; } = "top";

    public bool Shadow { get; init; }

    /// <summary>Skip drawing entirely when a token has no value (keeps big numbers from showing a dash artifact).</summary>
    public bool HideWhenMissing { get; init; }
}

public sealed record FontSpec
{
    public string Family { get; init; } = "Segoe UI";

    public double Size { get; init; } = 24;

    public int Weight { get; init; } = 400;

    public double LetterSpacing { get; init; }
}

public sealed record BarLayer : Layer
{
    /// <summary>Sensor token or literal number, e.g. <c>{gpu.load.core}</c>.</summary>
    public string Value { get; init; } = string.Empty;

    public double Min { get; init; }

    public double Max { get; init; } = 100;

    public string Track { get; init; } = "#1F2937";

    public string Fill { get; init; } = "#5EEAD4";

    public double Radius { get; init; } = 4;

    /// <summary>horizontal | vertical.</summary>
    public string Orientation { get; init; } = "horizontal";
}

public sealed record RingLayer : Layer
{
    public string Value { get; init; } = string.Empty;

    public double Min { get; init; }

    public double Max { get; init; } = 100;

    public double Thickness { get; init; } = 10;

    public string Track { get; init; } = "#1F2937";

    public IReadOnlyList<string> Fill { get; init; } = new[] { "#5EEAD4" };

    /// <summary>Clockwise degrees where the arc starts; 135 puts the gap at the bottom.</summary>
    public double StartAngle { get; init; } = 135;

    public double SweepAngle { get; init; } = 270;

    /// <summary>round | butt.</summary>
    public string Cap { get; init; } = "round";
}

public sealed record SparklineLayer : Layer
{
    public string Source { get; init; } = string.Empty;

    public int Samples { get; init; } = 60;

    public double Min { get; init; }

    public double Max { get; init; } = 144;

    public string Stroke { get; init; } = "#5EEAD4";

    public double LineWidth { get; init; } = 1.5;

    public string? Fill { get; init; }

    public bool Smooth { get; init; } = true;
}

/// <summary>Solid or translucent rounded rectangle, typically a backdrop for text.</summary>
public sealed record RectLayer : Layer
{
    public string Fill { get; init; } = "#B3000000";

    public double Radius { get; init; } = 8;
}

public sealed record ImageLayer : Layer
{
    /// <summary>Path relative to the theme file.</summary>
    public string Asset { get; init; } = string.Empty;

    /// <summary>contain | cover | stretch | tile.</summary>
    public string Fit { get; init; } = "contain";

    public string? Tint { get; init; }
}
