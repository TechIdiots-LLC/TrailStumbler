// GPX 1.1 writer. Pattern ported from VistumblerCS ExportService.ExportToGpxAsync (~line 628).
using System.Globalization;
using System.Text.Json;
using System.Xml;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Export;

/// <summary>A layer for <see cref="GpxWriter.WriteLayersAsync"/>: its features, and a recorded track's own points (times and all).</summary>
public sealed record GpxLayer(MapLayer Layer, IReadOnlyList<MapFeature> Features, IReadOnlyList<RecordingPoint>? Points = null);

public static class GpxWriter
{
    private const string Ns = "http://www.topografix.com/GPX/1/1";

    /// <summary>A recorded track.</summary>
    public static async Task WriteAsync(Stream stream, string trackName, IReadOnlyList<RecordingPoint> points)
    {
        await using var writer = Create(stream);
        await WriteStartAsync(writer);
        await WriteRecordedTrackAsync(writer, trackName, points);
        await WriteEndAsync(writer);
    }

    /// <summary>
    /// Several layers in one file: points as waypoints (first, as GPX 1.1 has them), a recorded track from its points,
    /// and each other line as a track (a multi-part line a segment a part). Areas have no GPX form and are left out.
    /// </summary>
    public static async Task WriteLayersAsync(Stream stream, IReadOnlyList<GpxLayer> layers)
    {
        await using var writer = Create(stream);
        await WriteStartAsync(writer);

        foreach (var layer in layers)
            foreach (var f in layer.Features.Where(f => f.GeometryType == "Point"))
            {
                if (Coordinates(f.CoordinatesJson) is not [var p]) continue;
                await writer.WriteStartElementAsync(null, "wpt", Ns);
                await WriteLatLonAsync(writer, p.Lat, p.Lon, p.Ele);
                await WriteNameAsync(writer, f.Name, f.Description);
                await writer.WriteEndElementAsync();
            }

        foreach (var layer in layers)
        {
            if (layer.Points is { Count: > 0 } points)
            {
                await WriteRecordedTrackAsync(writer, layer.Layer.Name, points);
                continue;
            }
            foreach (var f in layer.Features)
            {
                var parts = f.GeometryType switch
                {
                    "LineString" => [Coordinates(f.CoordinatesJson)],
                    "MultiLineString" => Parts(f.CoordinatesJson),
                    _ => [],
                };
                parts = parts.Where(p => p.Count >= 2).ToList();
                if (parts.Count == 0) continue;
                await writer.WriteStartElementAsync(null, "trk", Ns);
                await WriteNameAsync(writer, f.Name.Length > 0 ? f.Name : layer.Layer.Name, f.Description);
                foreach (var part in parts)
                {
                    await writer.WriteStartElementAsync(null, "trkseg", Ns);
                    foreach (var p in part)
                    {
                        await writer.WriteStartElementAsync(null, "trkpt", Ns);
                        await WriteLatLonAsync(writer, p.Lat, p.Lon, p.Ele);
                        await writer.WriteEndElementAsync();
                    }
                    await writer.WriteEndElementAsync(); // trkseg
                }
                await writer.WriteEndElementAsync(); // trk
            }
        }
        await WriteEndAsync(writer);
    }

    private static XmlWriter Create(Stream stream) => XmlWriter.Create(stream, new XmlWriterSettings
    {
        Async = true,
        Encoding = System.Text.Encoding.UTF8,
        Indent = true,
        IndentChars = "  ",
    });

    private static async Task WriteStartAsync(XmlWriter writer)
    {
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "gpx", Ns);
        await writer.WriteAttributeStringAsync(null, "version", null, "1.1");
        await writer.WriteAttributeStringAsync(null, "creator", null, "TrailStumbler");
        await writer.WriteAttributeStringAsync("xmlns", "xsi", null,
            "http://www.w3.org/2001/XMLSchema-instance");
        await writer.WriteAttributeStringAsync("xsi", "schemaLocation", null,
            "http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd");
    }

    private static async Task WriteEndAsync(XmlWriter writer)
    {
        await writer.WriteEndElementAsync(); // gpx
        await writer.WriteEndDocumentAsync();
    }

    private static async Task WriteRecordedTrackAsync(XmlWriter writer, string trackName, IReadOnlyList<RecordingPoint> points)
    {
        await writer.WriteStartElementAsync(null, "trk", Ns);
        await writer.WriteElementStringAsync(null, "name", Ns, trackName);
        await writer.WriteStartElementAsync(null, "trkseg", Ns);
        foreach (var pt in points)
        {
            await writer.WriteStartElementAsync(null, "trkpt", Ns);
            await WriteLatLonAsync(writer, pt.Lat, pt.Lon, pt.Ele);
            await writer.WriteElementStringAsync(null, "time", Ns,
                new DateTime(pt.TimestampTicks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            await writer.WriteEndElementAsync(); // trkpt
        }
        await writer.WriteEndElementAsync(); // trkseg
        await writer.WriteEndElementAsync(); // trk
    }

    // lat/lon attributes, and ele (which comes first among a point's elements)
    private static async Task WriteLatLonAsync(XmlWriter writer, double lat, double lon, double? ele)
    {
        await writer.WriteAttributeStringAsync(null, "lat", null, lat.ToString("F6", CultureInfo.InvariantCulture));
        await writer.WriteAttributeStringAsync(null, "lon", null, lon.ToString("F6", CultureInfo.InvariantCulture));
        if (ele.HasValue)
            await writer.WriteElementStringAsync(null, "ele", Ns, ele.Value.ToString("F1", CultureInfo.InvariantCulture));
    }

    private static async Task WriteNameAsync(XmlWriter writer, string name, string description)
    {
        if (name.Length > 0) await writer.WriteElementStringAsync(null, "name", Ns, name);
        if (description.Length > 0) await writer.WriteElementStringAsync(null, "desc", Ns, description);
    }

    // [lon, lat, ele?] or [[lon, lat, ele?], ...]
    private static List<(double Lon, double Lat, double? Ele)> Coordinates(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return Points(d.RootElement);
        }
        catch (JsonException) { return []; }
    }

    private static List<List<(double Lon, double Lat, double? Ele)>> Parts(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.EnumerateArray().Select(Points).ToList();
        }
        catch (JsonException) { return []; }
    }

    private static List<(double Lon, double Lat, double? Ele)> Points(JsonElement e)
    {
        static (double, double, double?) One(JsonElement p) =>
            (p[0].GetDouble(), p[1].GetDouble(), p.GetArrayLength() > 2 ? p[2].GetDouble() : null);
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() == 0) return [];
        return e[0].ValueKind == JsonValueKind.Number ? [One(e)] : e.EnumerateArray().Select(One).ToList();
    }
}
