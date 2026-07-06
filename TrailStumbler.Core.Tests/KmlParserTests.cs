using System.IO.Compression;
using System.Text.Json;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Parsing;
using Xunit;

namespace TrailStumbler.Core.Tests;

public class KmlParserTests
{
    private static List<ParsedFeature> ParseFixture()
    {
        using var stream = File.OpenRead(Path.Combine("Fixtures", "mixed.kml"));
        return new KmlParser().Parse(stream);
    }

    [Fact]
    public void ParsesMixedPointAndLineFamilies()
    {
        var features = ParseFixture();
        Assert.Equal(3, features.Count);
        Assert.Equal(1, features.Count(f => f.Family == GeometryFamily.Point));
        Assert.Equal(2, features.Count(f => f.Family == GeometryFamily.Line));
    }

    [Fact]
    public void ResolvesLineStyleToSimplestyleStroke()
    {
        var line = ParseFixture().Single(f => f.Name == "Dutton Hollow Road");
        using var props = JsonDocument.Parse(line.PropertiesJson);
        // KML FF008000 is AABBGGRR â†’ #008000 (green)
        Assert.Equal("#008000", props.RootElement.GetProperty("stroke").GetString());
        Assert.Equal(4, props.RootElement.GetProperty("stroke-width").GetDouble());
    }

    [Fact]
    public void ResolvesIconStyleToCategory()
    {
        var point = ParseFixture().Single(f => f.Family == GeometryFamily.Point);
        using var props = JsonDocument.Parse(point.PropertiesJson);
        Assert.Equal("parking", props.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public void CleansBrJunkFromDescriptions()
    {
        var features = ParseFixture();
        var point = features.Single(f => f.Family == GeometryFamily.Point);
        Assert.Equal("Trail Accessible Parking", point.Description);
        using var props = JsonDocument.Parse(point.PropertiesJson);
        Assert.Equal("Trail Accessible Parking", props.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void ParsesGxTrackWithCoordTimes()
    {
        var track = ParseFixture().Single(f => f.Name == "Track Sample");
        Assert.Equal("LineString", track.GeometryType);
        using var props = JsonDocument.Parse(track.PropertiesJson);
        var times = props.RootElement.GetProperty("coordTimes");
        Assert.Equal(2, times.GetArrayLength());
        Assert.Equal("2023-04-18T12:00:00Z", times[0].GetString());
        // Elevation carried as 3rd coordinate
        using var coords = JsonDocument.Parse(track.CoordinatesJson);
        Assert.Equal(3, coords.RootElement[0].GetArrayLength());
    }

    [Theory]
    [InlineData("FF008000", "#008000")]   // green trail
    [InlineData("FFFF0000", "#0000FF")]   // KML blue channel first â†’ blue
    [InlineData("FF000000", "#000000")]
    [InlineData(null, null)]
    [InlineData("nothex!!", null)]
    public void ConvertsKmlColors(string? kml, string? expected)
        => Assert.Equal(expected, KmlParser.KmlColorToHex(kml));
}

public class KmzParserTests
{
    [Fact]
    public void UnzipsAndParsesNonDocKmlEntry()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("atv-test.kml");   // deliberately not doc.kml
            using var es = entry.Open();
            using var src = File.OpenRead(Path.Combine("Fixtures", "mixed.kml"));
            src.CopyTo(es);
        }
        ms.Position = 0;

        var features = new KmzParser().Parse(ms);
        Assert.Equal(3, features.Count);
    }

    [Fact]
    public void ParsesRealGpsTrailmastersKmzWhenPresent()
    {
        // Integration check against the real-world package (skipped where absent).
        var path = @"C:\Users\Andrew\Documents\GitHub\GPS Trailmasters\KMZ\GPS Trailmasters ATV New York.kmz";
        if (!File.Exists(path)) return;

        using var stream = File.OpenRead(path);
        var features = new KmzParser().Parse(stream);

        Assert.True(features.Count > 100, $"expected >100 features, got {features.Count}");
        Assert.Contains(features, f => f.Family == GeometryFamily.Point);
        Assert.Contains(features, f => f.Family == GeometryFamily.Line);

        // Trail lines must carry resolved stroke colors from the shared styles.
        var line = features.First(f => f.Family == GeometryFamily.Line);
        using var props = JsonDocument.Parse(line.PropertiesJson);
        Assert.True(props.RootElement.TryGetProperty("stroke", out var stroke));
        Assert.StartsWith("#", stroke.GetString());

        // Points carry a category from their icon.
        Assert.Contains(features.Where(f => f.Family == GeometryFamily.Point), f =>
        {
            using var p = JsonDocument.Parse(f.PropertiesJson);
            return p.RootElement.TryGetProperty("category", out _);
        });

        // No description should retain the trailing <br><br> junk.
        Assert.DoesNotContain(features, f => f.Description.Contains("<br", StringComparison.OrdinalIgnoreCase));
    }
}
