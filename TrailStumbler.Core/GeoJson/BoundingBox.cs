namespace TrailStumbler.Core.GeoJson;

/// <summary>Mutable lon/lat bounding-box accumulator.</summary>
public class BoundingBox
{
    public double MinLon { get; private set; } = double.PositiveInfinity;
    public double MinLat { get; private set; } = double.PositiveInfinity;
    public double MaxLon { get; private set; } = double.NegativeInfinity;
    public double MaxLat { get; private set; } = double.NegativeInfinity;

    public bool IsEmpty => double.IsPositiveInfinity(MinLon);

    public void Extend(double lon, double lat)
    {
        if (lon < MinLon) MinLon = lon;
        if (lat < MinLat) MinLat = lat;
        if (lon > MaxLon) MaxLon = lon;
        if (lat > MaxLat) MaxLat = lat;
    }

    public void Union(BoundingBox other)
    {
        if (other.IsEmpty) return;
        Extend(other.MinLon, other.MinLat);
        Extend(other.MaxLon, other.MaxLat);
    }
}
