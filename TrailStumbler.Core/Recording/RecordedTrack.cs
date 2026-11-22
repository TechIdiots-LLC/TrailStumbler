using System.Globalization;
using System.Text;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Recording;

/// <summary>A recorded track's line as saved: its kept points, in stretches split where points were left out.</summary>
public sealed record RecordedTrackGeometry(
    string GeometryType, string CoordinatesJson, string CoordTimesJson,
    double DistanceMeters, int KeptPoints, double MinLat, double MinLon, double MaxLat, double MaxLon);

/// <summary>
/// The line a recorded track is drawn, measured and exported as, from its points: the stretches of points not left
/// out (<see cref="RecordingPoint.Excluded"/>), so a stretch left out is a gap, not a straight line across it. One
/// stretch is a LineString, more a MultiLineString; a lone point between two left out is no stretch.
/// </summary>
public static class RecordedTrack
{
    /// <summary>The kept stretches of at least two points, in order.</summary>
    public static List<List<RecordingPoint>> Stretches(IReadOnlyList<RecordingPoint> points)
    {
        var stretches = new List<List<RecordingPoint>>();
        List<RecordingPoint>? current = null;
        foreach (var p in points)
        {
            if (p.Excluded) { current = null; continue; }
            if (current is null) stretches.Add(current = []);
            current.Add(p);
        }
        stretches.RemoveAll(s => s.Count < 2);
        return stretches;
    }

    /// <summary>The track's geometry, or null when fewer than two points in a row are kept.</summary>
    public static RecordedTrackGeometry? Build(IReadOnlyList<RecordingPoint> points)
    {
        var stretches = Stretches(points);
        if (stretches.Count == 0) return null;

        var coords = new StringBuilder();
        var times = new StringBuilder();
        double distance = 0, minLat = double.MaxValue, minLon = double.MaxValue, maxLat = double.MinValue, maxLon = double.MinValue;
        bool multi = stretches.Count > 1;
        if (multi) { coords.Append('['); times.Append('['); }
        for (int s = 0; s < stretches.Count; s++)
        {
            if (s > 0) { coords.Append(','); times.Append(','); }
            coords.Append('['); times.Append('[');
            var stretch = stretches[s];
            for (int i = 0; i < stretch.Count; i++)
            {
                var p = stretch[i];
                if (i > 0) { coords.Append(','); times.Append(','); }
                coords.Append('[').Append(p.Lon.ToString("F6", CultureInfo.InvariantCulture))
                      .Append(',').Append(p.Lat.ToString("F6", CultureInfo.InvariantCulture));
                if (p.Ele.HasValue) coords.Append(',').Append(p.Ele.Value.ToString("F1", CultureInfo.InvariantCulture));
                coords.Append(']');
                times.Append('"').Append(new DateTime(p.TimestampTicks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ")).Append('"');
                if (i > 0) distance += Meters(stretch[i - 1], p);
                minLat = Math.Min(minLat, p.Lat); maxLat = Math.Max(maxLat, p.Lat);
                minLon = Math.Min(minLon, p.Lon); maxLon = Math.Max(maxLon, p.Lon);
            }
            coords.Append(']'); times.Append(']');
        }
        if (multi) { coords.Append(']'); times.Append(']'); }
        return new RecordedTrackGeometry(multi ? "MultiLineString" : "LineString", coords.ToString(), times.ToString(),
            distance, stretches.Sum(s => s.Count), minLat, minLon, maxLat, maxLon);
    }

    public static double Meters(RecordingPoint a, RecordingPoint b)
    {
        const double R = 6_371_000;
        double dLat = (b.Lat - a.Lat) * Math.PI / 180, dLon = (b.Lon - a.Lon) * Math.PI / 180;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(a.Lat * Math.PI / 180) * Math.Cos(b.Lat * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }
}
