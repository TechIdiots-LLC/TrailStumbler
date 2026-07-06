namespace TrailStumbler.ViewModels;

/// <summary>Normalized info for the tap-to-inspect popup.</summary>
public class MapFeatureInfo
{
    public string Title { get; set; } = "";
    public string LayerName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
}
