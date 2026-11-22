using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using TrailStumbler.Core.Export;
using TrailStumbler.Core.GeoJson;
using TrailStumbler.Core.Models;
using Xunit;

namespace TrailStumbler.Core.Tests;

public class MultiLayerExportTests
{
    private static readonly MapLayer Trails = new() { Id = 1, Name = "Trails", ColorHex = "#008000", GeometryFamily = (int)GeometryFamily.Line };
    private static readonly MapLayer Places = new() { Id = 2, Name = "Places", ColorHex = "#FF0000", GeometryFamily = (int)GeometryFamily.Point };
    private static readonly MapLayer Ride = new() { Id = 3, Name = "Saturday ride", ColorHex = "#FFFF66", GeometryFamily = (int)GeometryFamily.Line, IsRecordedTrack = true };

    private static readonly MapFeature Line = new() { Id = 10, LayerId = 1, Name = "Moose Muck", Description = "Corridor", GeometryType = "LineString", CoordinatesJson = "[[-71.5,44.5],[-71.49,44.51,300]]", PropertiesJson = "{}" };
    private static readonly MapFeature TwoParts = new() { Id = 11, LayerId = 1, Name = "Split Trail", GeometryType = "MultiLineString", CoordinatesJson = "[[[-71.5,44.5],[-71.4,44.5]],[[-71.3,44.5],[-71.2,44.5]]]", PropertiesJson = "{}" };
    private static readonly MapFeature Area = new() { Id = 12, LayerId = 1, Name = "Pit", GeometryType = "Polygon", CoordinatesJson = "[[[-71.5,44.5],[-71.4,44.5],[-71.4,44.6],[-71.5,44.5]]]", PropertiesJson = "{}" };
    private static readonly MapFeature Gas = new() { Id = 20, LayerId = 2, Name = "Youngs Store", Description = "Fuel", GeometryType = "Point", CoordinatesJson = "[-71.45,44.55]", PropertiesJson = "{\"category\":\"fuel\"}" };
    private static readonly MapFeature RideLine = new() { Id = 30, LayerId = 3, Name = "Saturday ride", GeometryType = "LineString", CoordinatesJson = "[[-71.5,44.5],[-71.49,44.51]]", PropertiesJson = "{}" };

    private static XDocument Xml(MemoryStream ms) => XDocument.Load(new MemoryStream(ms.ToArray()));   // with its byte order mark

    [Fact]
    public async Task OneGpxHasEveryLayersWaypointsThenTracks()
    {
        var ridePoints = new List<RecordingPoint>
        {
            new() { Lat = 44.5, Lon = -71.5, Ele = 210, TimestampTicks = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc).Ticks },
            new() { Lat = 44.51, Lon = -71.49, TimestampTicks = new DateTime(2026, 11, 21, 14, 1, 0, DateTimeKind.Utc).Ticks },
        };
        using var ms = new MemoryStream();
        await GpxWriter.WriteLayersAsync(ms, [new(Trails, [Line, TwoParts, Area]), new(Places, [Gas]), new(Ride, [RideLine], ridePoints)]);
        var gpx = Xml(ms).Root!;
        XNamespace ns = "http://www.topografix.com/GPX/1/1";
        var kinds = gpx.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(["wpt", "trk", "trk", "trk"], kinds);   // the waypoint first; the area left out
        Assert.Equal("Youngs Store", gpx.Element(ns + "wpt")!.Element(ns + "name")!.Value);
        var tracks = gpx.Elements(ns + "trk").ToList();
        Assert.Equal("Corridor", tracks[0].Element(ns + "desc")!.Value);
        Assert.Equal("300.0", tracks[0].Descendants(ns + "trkpt").Last().Element(ns + "ele")!.Value);
        Assert.Equal(2, tracks[1].Elements(ns + "trkseg").Count());   // a multi-part line, a segment a part
        Assert.Equal("2026-11-21T14:00:00Z", tracks[2].Descendants(ns + "time").First().Value);   // the ride's own points
    }

    [Fact]
    public async Task OneKmlHasAFolderALayer()
    {
        using var ms = new MemoryStream();
        await KmlWriter.WriteLayersAsync(ms, "My layers", [(Trails, [Line, TwoParts]), (Places, [Gas])]);
        XNamespace k = "http://www.opengis.net/kml/2.2";
        var folders = Xml(ms).Descendants(k + "Folder").ToList();
        Assert.Equal(["Trails", "Places"], folders.Select(f => f.Element(k + "name")!.Value));
        Assert.Equal(2, folders[0].Descendants(k + "Placemark").Count());
        Assert.Single(folders[0].Descendants(k + "MultiGeometry"));
        Assert.Equal(2, Xml(ms).Descendants(k + "Style").Select(s => s.Attribute("id")!.Value).Distinct().Count());
    }

    [Fact]
    public void OneGeoJsonTagsEachFeatureWithItsLayer()
    {
        using var doc = JsonDocument.Parse(GeoJsonBuilder.BuildFeatureCollection([("Trails", [Line]), ("Places", [Gas])]));
        var layers = doc.RootElement.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("properties").GetProperty("layer").GetString());
        Assert.Equal(["Trails", "Places"], layers);
        Assert.Equal("fuel", doc.RootElement.GetProperty("features")[1].GetProperty("properties").GetProperty("category").GetString());
    }
}
