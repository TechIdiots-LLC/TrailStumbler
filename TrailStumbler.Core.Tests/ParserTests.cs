using System.Text;
using System.Text.Json;
using TrailStumbler.Core.GeoJson;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Parsing;
using Xunit;

namespace TrailStumbler.Core.Tests;

public class GeoJsonParserTests
{
    [Fact]
    public void ParsesFeatureCollectionWithMixedFamilies()
    {
        using var stream = File.OpenRead(Path.Combine("Fixtures", "sample.geojson"));
        var features = new GeoJsonParser().Parse(stream);

        Assert.Equal(2, features.Count);

        var point = Assert.Single(features, f => f.Family == GeometryFamily.Point);
        Assert.Equal("Trailhead Parking", point.Name);
        Assert.Equal("Main lot", point.Description);
        Assert.Equal(-75.3998184, point.Bbox.MinLon, 6);

        var line = Assert.Single(features, f => f.Family == GeometryFamily.Line);
        Assert.Equal("LineString", line.GeometryType);
        Assert.Contains("\"stroke\"", line.PropertiesJson);   // simplestyle carried through
        Assert.Equal(41.9993318, line.Bbox.MinLat, 6);
        Assert.Equal(42.0001, line.Bbox.MaxLat, 6);
    }
}

public class GpxParserTests
{
    [Fact]
    public void ParsesWaypointsAndTracks()
    {
        using var stream = File.OpenRead(Path.Combine("Fixtures", "sample.gpx"));
        var features = new GpxParser().Parse(stream);

        Assert.Equal(2, features.Count);

        var wpt = Assert.Single(features, f => f.GeometryType == "Point");
        Assert.Equal("The Roz B&B", wpt.Name);
        Assert.StartsWith("[-75.4050674,43.6680908,312.5", wpt.CoordinatesJson);

        var trk = Assert.Single(features, f => f.GeometryType == "LineString");
        Assert.Equal("Morning Ride", trk.Name);
        Assert.Contains("coordTimes", trk.PropertiesJson);
        Assert.Contains("2026-07-04T13:00:00Z", trk.PropertiesJson);

        // Coordinates must be valid JSON with 3 positions.
        using var coords = JsonDocument.Parse(trk.CoordinatesJson);
        Assert.Equal(3, coords.RootElement.GetArrayLength());
    }
}

public class GeoJsonBuilderTests
{
    [Fact]
    public void BuildsValidFeatureCollectionWithFid()
    {
        var features = new List<MapFeature>
        {
            new() { Id = 42, GeometryType = "Point", CoordinatesJson = "[-75.4,43.7]",
                    PropertiesJson = """{"name":"P1"}""" },
            new() { Id = 43, GeometryType = "LineString",
                    CoordinatesJson = "[[-78.03,41.99],[-78.04,42.0]]", PropertiesJson = "{}" },
        };

        var json = GeoJsonBuilder.BuildFeatureCollection(features);

        using var doc = JsonDocument.Parse(json);   // must be valid JSON
        var arr = doc.RootElement.GetProperty("features");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal(42, arr[0].GetProperty("properties").GetProperty("_fid").GetInt64());
        Assert.Equal("P1", arr[0].GetProperty("properties").GetProperty("name").GetString());
        Assert.Equal(43, arr[1].GetProperty("properties").GetProperty("_fid").GetInt64());
        Assert.Equal("LineString", arr[1].GetProperty("geometry").GetProperty("type").GetString());
    }

    [Fact]
    public void WhitespaceOnlyPropertiesProduceValidJson()
    {
        var features = new List<MapFeature>
        {
            new() { Id = 1, GeometryType = "Point", CoordinatesJson = "[0,0]", PropertiesJson = "{  }" },
        };
        using var doc = JsonDocument.Parse(GeoJsonBuilder.BuildFeatureCollection(features));
        Assert.Equal(1, doc.RootElement.GetProperty("features")[0]
            .GetProperty("properties").GetProperty("_fid").GetInt64());
    }
}

public class FormatDetectorTests
{
    [Theory]
    [InlineData("{ \"type\": \"FeatureCollection\" }", GisFormat.GeoJson)]
    [InlineData("<?xml version=\"1.0\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"></kml>", GisFormat.Kml)]
    [InlineData("<?xml version=\"1.0\"?><gpx version=\"1.1\"></gpx>", GisFormat.Gpx)]
    public void SniffsContent(string content, GisFormat expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        Assert.Equal(expected, FormatDetector.Sniff(stream));
    }

    [Fact]
    public void SniffsZipMagicAsKmz()
    {
        using var stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });
        Assert.Equal(GisFormat.Kmz, FormatDetector.Sniff(stream));
    }
}

public class DescriptionCleanerTests
{
    [Fact]
    public void StripsTrailingBrJunkLikeTheSedPipeline()
    {
        Assert.Equal("Trail Accessible Parking",
            DescriptionCleaner.CleanForDisplay("Trail Accessible Parking<br><br>"));
        Assert.Equal("UTV/ATV 60 Inches and Motorcycles",
            DescriptionCleaner.CleanProperty("UTV/ATV 60 Inches and Motorcycles<br><br>"));
        Assert.Equal("Line one\nLine two",
            DescriptionCleaner.CleanForDisplay("Line one<br>Line two"));
    }
}
