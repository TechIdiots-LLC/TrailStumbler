namespace TrailStumbler.Services;

/// <summary>Default per-layer colors, assigned round-robin by existing layer count.</summary>
public static class LayerPalette
{
    private static readonly string[] Colors =
    [
        "#E53935", "#1E88E5", "#43A047", "#FB8C00",
        "#8E24AA", "#00ACC1", "#F4511E", "#3949AB",
    ];

    public static string ColorFor(int index) => Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];
}
